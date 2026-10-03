#!/usr/bin/env python3
from __future__ import annotations
import argparse, base64, hashlib, json, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ADM/chrome-extension"
KEY_FILE = ROOT / "tools/unpacked-extension-public-key.txt"

def extension_id_from_key(encoded: str) -> str:
    raw = base64.b64decode(encoded.strip(), validate=True)
    digest = hashlib.sha256(raw).digest()[:16]
    alphabet = "abcdefghijklmnop"
    return "".join(alphabet[b >> 4] + alphabet[b & 0x0F] for b in digest)

def prepare(output: Path) -> dict:
    key = KEY_FILE.read_text(encoding="ascii").strip()
    extension_id = extension_id_from_key(key)
    if output.exists(): shutil.rmtree(output)
    shutil.copytree(SOURCE, output)
    manifest_path = output / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["key"] = key
    manifest_path.write_text(json.dumps(manifest, indent=4) + "\n", encoding="utf-8")
    metadata = {
        "schemaVersion": 1,
        "kind": "adm-unpacked-development-extension",
        "extensionId": extension_id,
        "allowedOrigin": f"chrome-extension://{extension_id}/",
        "source": str(SOURCE.relative_to(ROOT)).replace("\\", "/"),
    }
    (output / "ADM-UNPACKED-IDENTITY.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    return metadata

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/unpacked-chromium-extension")
    parser.add_argument("--register-host-exe", type=Path)
    parser.add_argument("--browser", choices=["edge", "chrome", "chromium", "all"], default="all")
    args = parser.parse_args()
    output = args.output.resolve()
    metadata = prepare(output)
    if args.register_host_exe is not None:
        if sys.platform != "win32":
            raise SystemExit("--register-host-exe is Windows-only")
        subprocess.run([
            sys.executable, str(ROOT / "tools/register_unpacked_native_host.py"),
            "--extension-dir", str(output),
            "--host-exe", str(args.register_host_exe.resolve()),
            "--browser", args.browser,
        ], check=True)
        metadata["nativeHostRegistered"] = True
        metadata["nativeHostExecutable"] = str(args.register_host_exe.resolve())
    print(json.dumps(metadata, indent=2))
    return 0

if __name__ == "__main__": raise SystemExit(main())
