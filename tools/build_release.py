import glob
import hashlib
import os
import shutil
import struct
import subprocess
import sys
import stat
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import setup_payload

ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.0.2"
PRODUCT = "Axioos Download Manager"
UPGRADE_CODE = "741CBBE2-3911-4192-A051-AFF85038EFD7"
OUTPUT = ROOT / "release-output"
STAGE = OUTPUT / "app"
APP = ROOT / "app" / "ADM" if (ROOT / "app" / "ADM").is_dir() else ROOT / "ADM"
INSTALLER = APP / "ADM.Win.Installer"
APP_PROJECT = APP / "ADM.Wpf.UI" / "ADM.Wpf.UI.csproj"
HOST_PROJECT = APP / "ADM.App.Host" / "ADM.App.Host.csproj"
SETUP_PROJECT = APP / "ADM.Setup" / "ADM.Setup.csproj"
OTHER_ARCHITECTURE_FOLDERS = ("x64", "arm64", "win-x64", "win-arm64", "win-arm")
MSI_NAME = f"admsetup-{VERSION}.msi"
SETUP_NAME = f"admsetup-{VERSION}.exe"
PORTABLE_NAME = f"Axioos-Download-Manager-{VERSION}-portable.zip"
REQUIRED_FILES = (
    "adm-app.exe",
    "WebShell/index.html",
    "WebShell/shell.js",
    "Lang/English.txt",
    "chrome-extension/manifest.json",
    "ADM.App.Host/adm-app-host.exe",
    "ADM.App.Host/adm_chrome.native_host.json",
)
FFMPEG_NAMES = ("ffmpeg-x86.exe", "ffmpeg.exe")


def say(text):
    print(text, flush=True)


def run(command, cwd=None, env=None):
    say("> " + " ".join(str(part) for part in command))
    subprocess.run([str(part) for part in command], cwd=str(cwd or ROOT), env=env, check=True)


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def find_ffmpeg():
    override = os.environ.get("AXIOOS_FFMPEG")
    if override and Path(override).is_file():
        return Path(override)
    folders = [Path.home() / ".adm-app-data"]
    home = os.environ.get("FFMPEG_HOME")
    if home:
        folders.append(Path(home))
    for folder in folders:
        for name in FFMPEG_NAMES:
            candidate = folder / name
            if candidate.is_file():
                return candidate
    for name in FFMPEG_NAMES:
        found = shutil.which(name)
        if found:
            return Path(found)
    return None


def find_wix_tool(name):
    found = shutil.which(name)
    if found:
        return Path(found)
    folders = []
    wix = os.environ.get("WIX")
    if wix:
        folders.append(Path(wix) / "bin")
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles")):
        if base:
            folders.extend(Path(item) / "bin" for item in sorted(glob.glob(str(Path(base) / "WiX Toolset v3*")), reverse=True))
    for folder in folders:
        candidate = folder / name
        if candidate.is_file():
            return candidate
    return None


def make_writable(function, path, _info):
    os.chmod(path, stat.S_IWRITE)
    function(path)


def runs_only_as_32_bit(executable):
    try:
        data = Path(executable).read_bytes()
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe:pe + 4] != b"PE\0\0":
            return False
        machine = struct.unpack_from("<H", data, pe + 4)[0]
        sections = struct.unpack_from("<H", data, pe + 6)[0]
        optional_size = struct.unpack_from("<H", data, pe + 20)[0]
        optional = pe + 24
        if machine != 0x14C or struct.unpack_from("<H", data, optional)[0] != 0x10B:
            return False
        clr_rva = struct.unpack_from("<I", data, optional + 96 + 14 * 8)[0]
        if clr_rva == 0:
            return True
        table = optional + optional_size
        for index in range(sections):
            entry = table + index * 40
            virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", data, entry + 8)
            if virtual_address <= clr_rva < virtual_address + max(virtual_size, raw_size):
                flags = struct.unpack_from("<I", data, raw_offset + (clr_rva - virtual_address) + 16)[0]
                return bool(flags & 0x2) and not flags & 0x20000
        return False
    except (OSError, struct.error, IndexError):
        return False


def trim_other_architectures():
    if os.environ.get("AXIOOS_KEEP_ALL_ARCH") == "1":
        say("TRIM: skipped on request")
        return 0
    roots = [(STAGE, STAGE / "adm-app.exe"), (STAGE / "ADM.App.Host", STAGE / "ADM.App.Host" / "adm-app-host.exe")]
    saved = 0
    for root, executable in roots:
        if not runs_only_as_32_bit(executable):
            say(f"TRIM: {executable.name} is not a 32 bit only program, so every native library is kept")
            continue
        for folder in sorted(root.rglob("*")):
            if not folder.is_dir() or folder.name.lower() not in OTHER_ARCHITECTURE_FOLDERS:
                continue
            if root == STAGE and (STAGE / "ADM.App.Host") in folder.parents:
                continue
            files = [item for item in folder.rglob("*") if item.is_file()]
            if not files or any(item.suffix.lower() != ".dll" for item in files):
                continue
            saved += sum(item.stat().st_size for item in files)
            shutil.rmtree(folder, onerror=make_writable)
            say(f"TRIM: removed {folder.relative_to(STAGE)} which a 32 bit program can never load")
    return saved


def repair_old_timestamps(folder):
    floor = 315619200
    repaired = 0
    for path in Path(folder).rglob("*"):
        try:
            if path.is_file() and path.stat().st_mtime < floor:
                os.utime(path, None)
                repaired += 1
        except OSError:
            continue
    return repaired


def build_app():
    if OUTPUT.exists():
        try:
            shutil.rmtree(OUTPUT, onerror=make_writable)
        except OSError as error:
            raise RuntimeError("Close Axioos and any window open in release-output, then run again. " + str(error))
    STAGE.mkdir(parents=True)
    common = ["-c", "Release", "-f", "net4.7.2", "-p:Platform=x86", "-p:UseSharedCompilation=false",
              "-p:AdmPhase5ModernizationEnabled=false", "-p:AdmPhase6X64Enabled=false"]
    run(["dotnet", "build", APP_PROJECT, *common, "-o", STAGE])
    run(["dotnet", "build", HOST_PROJECT, *common, "-o", STAGE / "ADM.App.Host"])
    tests = STAGE / "chrome-extension" / "test"
    if tests.exists():
        shutil.rmtree(tests)
    for symbol in STAGE.rglob("*.pdb"):
        symbol.unlink()
    for doc in STAGE.rglob("*.xml"):
        if doc.with_suffix(".dll").is_file():
            doc.unlink()
    missing = [name for name in REQUIRED_FILES if not (STAGE / name).is_file()]
    if missing:
        raise RuntimeError("The release build is missing: " + ", ".join(missing))
    repaired = repair_old_timestamps(STAGE)
    if repaired:
        say(f"DATES: {repaired} files carried a date before 1980 and now carry today's date, which archives and installers require")
    saved = trim_other_architectures()
    if saved:
        say(f"TRIM: {saved / 1048576:.1f} MB of unused native libraries left out")


def bundle_ffmpeg():
    if os.environ.get("AXIOOS_SKIP_FFMPEG") == "1":
        say("FFMPEG: skipped on request")
        return None
    source = find_ffmpeg()
    if source is None:
        say("FFMPEG: not found on this computer. The release is built without it, so HD video merging will ask users to install FFmpeg.")
        return None
    name = source.name if source.name.lower() in FFMPEG_NAMES else "ffmpeg.exe"
    target = STAGE / name
    shutil.copyfile(source, target)
    say(f"FFMPEG: bundled {source}")
    return source


def build_portable():
    target = OUTPUT / PORTABLE_NAME
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9, strict_timestamps=False) as archive:
        for path in sorted(STAGE.rglob("*")):
            if path.is_file():
                archive.write(path, Path("Axioos") / path.relative_to(STAGE))
    return target


def build_msi():
    tools = {name: find_wix_tool(name) for name in ("heat.exe", "candle.exe", "light.exe")}
    if not all(tools.values()):
        say("INSTALLER: WiX Toolset v3 was not found, so no installer was built.")
        say("INSTALLER: install it with: winget install --id WiXToolset.WiXToolset -e   then run BUILD-RELEASE.bat again.")
        return None
    work = OUTPUT / "wix"
    work.mkdir()
    env = dict(os.environ, BUILD_VER=VERSION, PRODUCT_UPGRADE_CODE=UPGRADE_CODE)
    env.pop("LEGACY_UPGRADE_CODE", None)
    harvest = work / "harvest.wxs"
    target = work / MSI_NAME
    run([tools["heat.exe"], "dir", STAGE, "-o", harvest, "-scom", "-frag", "-srd", "-sreg", "-gg", "-cg", "NET4", "-dr", "INSTALLFOLDER"], cwd=INSTALLER, env=env)
    run([tools["candle.exe"], "-dCabCompression=none", "product.wxs", harvest, "-o", str(work) + os.sep], cwd=INSTALLER, env=env)
    run([tools["light.exe"], "-ext", "WixUIExtension", "-ext", "WixUtilExtension", "-cultures:en-us", "-spdb",
         work / "product.wixobj", work / "harvest.wixobj", "-b", STAGE, "-out", target], cwd=INSTALLER, env=env)
    return target


def build_packed_setup(msi):
    if os.environ.get("AXIOOS_SETUP_PACKER", "").lower() == "iexpress":
        say("SETUP EXE: the Windows built in packer was requested")
        return None
    if not SETUP_PROJECT.is_file():
        say("SETUP EXE: the setup program source is missing, so the Windows built in packer is used")
        return None
    work = OUTPUT / "setup"
    payload = work / "payload.bin"
    target = OUTPUT / SETUP_NAME
    try:
        work.mkdir()
        say("SETUP EXE: packing the installer at the strongest setting. This takes a few minutes.")
        setup_payload.pack_file(msi, payload, say)
        run(["dotnet", "build", SETUP_PROJECT, "-c", "Release", "-p:UseSharedCompilation=false",
             f"-p:AxioosSetupPayload={payload}", "-o", work / "bin"])
        built = work / "bin" / "admsetup.exe"
        if not built.is_file():
            raise RuntimeError("the setup program was not produced")
        shutil.copyfile(built, target)
        check = subprocess.run([str(target), "--verify"], timeout=900)
        if check.returncode != 0:
            raise RuntimeError(f"the setup program failed its own unpack check with code {check.returncode}")
        say("SETUP EXE: the setup program unpacked its installer and the result matches byte for byte")
        return target
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, RuntimeError, OSError, ValueError, MemoryError) as error:
        say("SETUP EXE: the strongest packer could not be used, so the Windows built in packer is used. " + str(error))
        if target.exists():
            target.unlink()
        return None


def build_iexpress_setup(msi):
    system_root = Path(os.environ.get("SystemRoot", r"C:\Windows"))
    candidates = [system_root / "SysWOW64" / "iexpress.exe", system_root / "System32" / "iexpress.exe"]
    iexpress = next((item for item in candidates if item.is_file()), None)
    if iexpress is None:
        say("SETUP EXE: iexpress.exe was not found, so no setup program was built.")
        return None
    target = OUTPUT / SETUP_NAME
    sed = OUTPUT / "setup.sed"
    lines = [
        "[Version]",
        "Class=IEXPRESS",
        "SEDVersion=3",
        "[Options]",
        "PackagePurpose=InstallApp",
        "ShowInstallProgramWindow=0",
        "HideExtractAnimation=1",
        "UseLongFileName=1",
        "InsideCompressed=0",
        "CAB_FixedSize=0",
        "CAB_ResvCodeSigning=0",
        "RebootMode=N",
        "InstallPrompt=%InstallPrompt%",
        "DisplayLicense=%DisplayLicense%",
        "FinishMessage=%FinishMessage%",
        "TargetName=%TargetName%",
        "FriendlyName=%FriendlyName%",
        "AppLaunched=%AppLaunched%",
        "PostInstallCmd=%PostInstallCmd%",
        "AdminQuietInstCmd=%AdminQuietInstCmd%",
        "UserQuietInstCmd=%UserQuietInstCmd%",
        "SourceFiles=SourceFiles",
        "[Strings]",
        "InstallPrompt=",
        "DisplayLicense=",
        "FinishMessage=",
        f"TargetName={target}",
        f"FriendlyName={PRODUCT} {VERSION} Setup",
        f"AppLaunched=msiexec.exe /i {MSI_NAME}",
        "PostInstallCmd=<None>",
        f"AdminQuietInstCmd=msiexec.exe /i {MSI_NAME} /qn /norestart",
        f"UserQuietInstCmd=msiexec.exe /i {MSI_NAME} /qn /norestart",
        f'FILE0="{msi.name}"',
        "[SourceFiles]",
        f"SourceFiles0={msi.parent}{os.sep}",
        "[SourceFiles0]",
        "%FILE0%=",
    ]
    try:
        sed.write_text("\r\n".join(lines) + "\r\n", encoding="mbcs" if sys.platform == "win32" else "ascii")
        run([iexpress, "/N", "/Q", sed.name], cwd=OUTPUT)
    except (subprocess.CalledProcessError, OSError, UnicodeError) as error:
        say("SETUP EXE: could not be built. " + str(error))
        return None
    finally:
        if sed.exists():
            sed.unlink()
    if not target.is_file():
        say("SETUP EXE: iexpress did not produce the file.")
        return None
    return target


def build_setup_exe(msi):
    return build_packed_setup(msi) or build_iexpress_setup(msi)


def write_info(outputs, ffmpeg):
    info = OUTPUT / "RELEASE-INFO.txt"
    lines = [f"{PRODUCT} {VERSION}", ""]
    for path in outputs:
        lines.append(f"{path.name}  {path.stat().st_size} bytes  sha256 {sha256(path)}")
    lines.append("")
    if ffmpeg is not None:
        lines.append(f"Bundled FFmpeg: {ffmpeg.name}  sha256 {sha256(ffmpeg)}  from {ffmpeg}")
    else:
        lines.append("Bundled FFmpeg: none")
    info.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return info


def main():
    if sys.platform != "win32":
        say("The release must be built on Windows.")
        return 2
    if not shutil.which("dotnet"):
        say("The .NET SDK was not found.")
        return 2
    try:
        build_app()
        ffmpeg = bundle_ffmpeg()
        outputs = [build_portable()]
        msi = build_msi()
    except (subprocess.CalledProcessError, RuntimeError, OSError, ValueError) as error:
        say("RELEASE BUILD FAILED: " + str(error))
        return 1
    setup = None
    if msi is not None:
        setup = build_setup_exe(msi)
        if setup is not None:
            outputs.append(setup)
        else:
            fallback = OUTPUT / MSI_NAME
            shutil.copyfile(msi, fallback)
            outputs.append(fallback)
    info = write_info(outputs, ffmpeg)
    say("")
    say("RELEASE READY")
    for path in outputs:
        say(f"  {path}  ({path.stat().st_size / 1048576:.1f} MB)")
    say(f"  {info}")
    say(f"  App folder: {STAGE}")
    if setup is not None:
        say(f"  To get the plain MSI out of the setup program run: {setup.name} /extract <folder>")
    if msi is None:
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
