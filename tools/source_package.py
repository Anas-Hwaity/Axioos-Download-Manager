from __future__ import annotations

import argparse
import hashlib
import io
import json
import subprocess
import tarfile
import zipfile
from pathlib import Path, PurePosixPath

from windows_path_compat import require_windows_paths

MANIFEST_NAME = "ADM-SOURCE-PACKAGE-MANIFEST.json"
FIXED_ZIP_TIME = (1980, 1, 1, 0, 0, 0)


def _git(root: Path, *args: str) -> str:
    return subprocess.check_output(
        ["git", *args], cwd=root, text=True, stderr=subprocess.STDOUT
    ).strip()


def tracked_files(root: Path) -> list[str]:
    raw = subprocess.check_output(["git", "ls-files", "-z"], cwd=root)
    return sorted(item.decode("utf-8", errors="surrogateescape") for item in raw.split(b"\0") if item)


def tracked_modes(root: Path) -> dict[str, int]:
    raw = subprocess.check_output(["git", "ls-files", "--stage", "-z"], cwd=root)
    modes: dict[str, int] = {}
    for record in raw.split(b"\0"):
        if not record:
            continue
        meta, raw_path = record.split(b"\t", 1)
        git_mode = meta.split(b" ", 1)[0]
        relative = raw_path.decode("utf-8", errors="surrogateescape")
        modes[relative] = 0o755 if git_mode == b"100755" else 0o644
    return modes


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def source_manifest(root: Path) -> dict:
    root = root.resolve()
    tracked = tracked_files(root)
    require_windows_paths(tracked, "tracked source paths")
    files = {}
    for relative in tracked:
        path = root / relative
        if not path.is_file():
            raise RuntimeError(f"tracked source missing before packaging: {relative}")
        files[relative] = {"sha256": _sha256(path), "bytes": path.stat().st_size}
    return {
        "schemaVersion": 1,
        "kind": "adm-engineering-source-package",
        "headCommit": _git(root, "rev-parse", "HEAD"),
        "headTree": _git(root, "rev-parse", "HEAD^{tree}"),
        "trackedFileCount": len(tracked),
        "trackedFiles": tracked,
        "files": files,
    }


def _zip_info(name: str, *, directory: bool) -> zipfile.ZipInfo:
    if directory and not name.endswith("/"):
        name += "/"
    info = zipfile.ZipInfo(name, FIXED_ZIP_TIME)
    info.create_system = 0
    info.external_attr = 0x10 if directory else 0x20
    info.compress_type = zipfile.ZIP_STORED if directory else zipfile.ZIP_DEFLATED
    info.flag_bits = 0
    return info


def _git_files(root: Path) -> list[Path]:
    git_dir = root / ".git"
    if not git_dir.is_dir():
        raise RuntimeError("source packaging requires a standalone Git checkout with a .git directory")
    files = []
    for path in git_dir.rglob("*"):
        if path.is_file() and path.name not in {"index.lock", "shallow.lock", "packed-refs.lock"}:
            files.append(path)
    return sorted(files, key=lambda p: p.relative_to(root).as_posix())


def _archive_paths(root: Path, tracked: list[str]) -> tuple[list[str], list[str]]:
    file_names = set(tracked)
    file_names.update(path.relative_to(root).as_posix() for path in _git_files(root))

    git_dir = root / ".git"
    directories = {".git"}
    directories.update(path.relative_to(root).as_posix() for path in git_dir.rglob("*") if path.is_dir())

    for relative in file_names:
        parent = PurePosixPath(relative).parent
        while str(parent) not in {".", ""}:
            directories.add(parent.as_posix())
            parent = parent.parent
    return sorted(directories), sorted(file_names)


def build_source_package(root: Path, output: Path, *, root_name: str = "adm-canonical") -> dict:
    root = root.resolve()
    output = output.resolve()
    if not (root / ".git").is_dir():
        raise RuntimeError(f"not a standalone Git checkout: {root}")
    status = _git(root, "status", "--porcelain=v1", "--untracked-files=all")
    if status:
        raise RuntimeError("refusing to package a dirty source tree")

    manifest = source_manifest(root)
    directories, file_names = _archive_paths(root, manifest["trackedFiles"])
    require_windows_paths([MANIFEST_NAME, root_name, *directories, *file_names], "archive paths")
    output.parent.mkdir(parents=True, exist_ok=True)
    temp = output.with_suffix(output.suffix + ".tmp")
    if temp.exists():
        temp.unlink()
    manifest_bytes = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")

    with zipfile.ZipFile(temp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9,
                         allowZip64=True) as archive:
        archive.writestr(_zip_info(MANIFEST_NAME, directory=False), manifest_bytes, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr(_zip_info(root_name, directory=True), b"")
        for relative in directories:
            archive.writestr(_zip_info(f"{root_name}/{relative}", directory=True), b"")
        for relative in file_names:
            path = root / relative
            archive.writestr(_zip_info(f"{root_name}/{relative}", directory=False), path.read_bytes(),
                             compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

    temp.replace(output)
    verify_source_package(output, manifest=manifest, root_name=root_name)
    return manifest


def verify_source_package(package: Path, *, manifest: dict | None = None,
                          root_name: str = "adm-canonical") -> dict:
    with zipfile.ZipFile(package) as archive:
        bad = archive.testzip()
        if bad:
            raise RuntimeError(f"ZIP CRC verification failed at {bad}")
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise RuntimeError("duplicate ZIP entries detected")
        require_windows_paths(names, "ZIP member paths")
        stored_manifest = json.loads(archive.read(MANIFEST_NAME))
        if manifest is not None and stored_manifest != manifest:
            raise RuntimeError("stored source manifest differs from generated manifest")
        tracked = stored_manifest["trackedFiles"]
        for relative in tracked:
            name = f"{root_name}/{relative}"
            try:
                info = archive.getinfo(name)
            except KeyError as exc:
                raise RuntimeError(f"tracked source missing from ZIP: {relative}") from exc
            if info.create_system != 0:
                raise RuntimeError(f"non-Windows ZIP metadata for tracked source: {relative}")
            data = archive.read(name)
            expected = stored_manifest["files"][relative]
            if hashlib.sha256(data).hexdigest() != expected["sha256"] or len(data) != expected["bytes"]:
                raise RuntimeError(f"tracked source hash/size mismatch in ZIP: {relative}")
        return stored_manifest



def _tar_info(name: str, *, directory: bool, size: int = 0, mode: int | None = None) -> tarfile.TarInfo:
    info = tarfile.TarInfo(name.rstrip("/") + ("/" if directory else ""))
    info.mtime = 0
    info.uid = 0
    info.gid = 0
    info.uname = ""
    info.gname = ""
    info.mode = 0o755 if directory else (0o644 if mode is None else mode)
    info.size = 0 if directory else size
    info.type = tarfile.DIRTYPE if directory else tarfile.REGTYPE
    return info


def build_source_tar(root: Path, output: Path, *, root_name: str = "adm-canonical") -> dict:
    root = root.resolve()
    output = output.resolve()
    if not (root / ".git").is_dir():
        raise RuntimeError(f"not a standalone Git checkout: {root}")
    status = _git(root, "status", "--porcelain=v1", "--untracked-files=all")
    if status:
        raise RuntimeError("refusing to package a dirty source tree")

    manifest = source_manifest(root)
    directories, file_names = _archive_paths(root, manifest["trackedFiles"])
    index_modes = tracked_modes(root)
    require_windows_paths([MANIFEST_NAME, root_name, *directories, *file_names], "archive paths")
    output.parent.mkdir(parents=True, exist_ok=True)
    temp = output.with_suffix(output.suffix + ".tmp")
    if temp.exists():
        temp.unlink()
    manifest_bytes = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")

    with tarfile.open(temp, "w", format=tarfile.USTAR_FORMAT) as archive:
        archive.addfile(_tar_info(MANIFEST_NAME, directory=False, size=len(manifest_bytes)), io.BytesIO(manifest_bytes))
        archive.addfile(_tar_info(root_name, directory=True))
        for relative in directories:
            archive.addfile(_tar_info(f"{root_name}/{relative}", directory=True))
        for relative in file_names:
            path = root / relative
            size = path.stat().st_size
            with path.open("rb") as stream:
                archive.addfile(_tar_info(f"{root_name}/{relative}", directory=False, size=size,
                                          mode=index_modes.get(relative, 0o644)), stream)

    temp.replace(output)
    verify_source_tar(output, manifest=manifest, root_name=root_name)
    return manifest


def verify_source_tar(package: Path, *, manifest: dict | None = None,
                      root_name: str = "adm-canonical") -> dict:
    with tarfile.open(package, "r:") as archive:
        members = archive.getmembers()
        names = [member.name for member in members]
        if len(names) != len(set(names)):
            raise RuntimeError("duplicate TAR entries detected")
        require_windows_paths(names, "TAR member paths")
        if not names or names[0] != MANIFEST_NAME:
            raise RuntimeError("TAR source manifest must be the first archive entry")
        for member in members:
            if member.pax_headers:
                raise RuntimeError(f"PAX metadata is forbidden in Windows handoff TAR: {member.name}")
        manifest_stream = archive.extractfile(members[0])
        if manifest_stream is None:
            raise RuntimeError("TAR source manifest is unreadable")
        stored_manifest = json.loads(manifest_stream.read().decode("utf-8"))
        if manifest is not None and stored_manifest != manifest:
            raise RuntimeError("stored source manifest differs from generated manifest")
        member_map = {member.name: member for member in members}
        for relative in stored_manifest["trackedFiles"]:
            name = f"{root_name}/{relative}"
            member = member_map.get(name)
            if member is None or not member.isfile():
                raise RuntimeError(f"tracked source missing from TAR: {relative}")
            stream = archive.extractfile(member)
            if stream is None:
                raise RuntimeError(f"tracked source unreadable from TAR: {relative}")
            data = stream.read()
            expected = stored_manifest["files"][relative]
            if hashlib.sha256(data).hexdigest() != expected["sha256"] or len(data) != expected["bytes"]:
                raise RuntimeError(f"tracked source hash/size mismatch in TAR: {relative}")
        return stored_manifest

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--root-name", default="adm-canonical")
    parser.add_argument("--format", choices=("zip", "tar"), default=None,
                        help="handoff archive format; defaults to tar for .tar output, zip otherwise")
    args = parser.parse_args()
    archive_format = args.format or ("tar" if args.output.suffix.lower() == ".tar" else "zip")
    if archive_format == "tar":
        manifest = build_source_tar(args.source, args.output, root_name=args.root_name)
    else:
        manifest = build_source_package(args.source, args.output, root_name=args.root_name)
    digest = hashlib.sha256(args.output.read_bytes()).hexdigest()
    print(json.dumps({
        "output": str(args.output.resolve()),
        "sha256": digest,
        "headCommit": manifest["headCommit"],
        "headTree": manifest["headTree"],
        "trackedFileCount": manifest["trackedFileCount"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
