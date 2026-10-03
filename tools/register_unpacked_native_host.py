#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, os, sys
from pathlib import Path

HOST_NAME = "adm_chrome.native_host"
OFFICIAL_ORIGIN = "chrome-extension://akdmdglbephckgfmdffcdebnpjgamofc/"
REGISTRY_PATHS = {
    "edge": rf"Software\Microsoft\Edge\NativeMessagingHosts\{HOST_NAME}",
    "chrome": rf"Software\Google\Chrome\NativeMessagingHosts\{HOST_NAME}",
    "chromium": rf"Software\Chromium\NativeMessagingHosts\{HOST_NAME}",
}

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--extension-dir", type=Path, required=True)
    parser.add_argument("--host-exe", type=Path, required=True)
    parser.add_argument("--browser", choices=["edge", "chrome", "chromium", "all"], default="all")
    parser.add_argument("--remove", action="store_true")
    args = parser.parse_args()
    if sys.platform != "win32":
        raise SystemExit("This registration helper is Windows-only.")
    import winreg
    identity_path = args.extension_dir.resolve() / "ADM-UNPACKED-IDENTITY.json"
    identity = json.loads(identity_path.read_text(encoding="utf-8"))
    origin = identity.get("allowedOrigin")
    if not isinstance(origin, str) or not origin.startswith("chrome-extension://"):
        raise SystemExit("Invalid unpacked extension identity metadata.")
    host = args.host_exe.resolve()
    if not args.remove and not host.is_file():
        raise SystemExit(f"Native host executable does not exist: {host}")
    manifest = args.extension_dir.resolve() / "adm_chrome.native_host.dev.json"
    targets = list(REGISTRY_PATHS) if args.browser == "all" else [args.browser]
    if args.remove:
        for target in targets:
            try: winreg.DeleteKey(winreg.HKEY_CURRENT_USER, REGISTRY_PATHS[target])
            except FileNotFoundError: pass
        if manifest.exists(): manifest.unlink()
        return 0
    manifest.write_text(json.dumps({
        "name": HOST_NAME,
        "description": "ADM unpacked-development native messaging host",
        "path": str(host),
        "type": "stdio",
        "allowed_origins": list(dict.fromkeys([OFFICIAL_ORIGIN, origin])),
    }, indent=2) + "\n", encoding="utf-8")
    for target in targets:
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, REGISTRY_PATHS[target]) as key:
            winreg.SetValueEx(key, None, 0, winreg.REG_SZ, str(manifest))
    print(json.dumps({"registered": targets, "origin": origin, "manifest": str(manifest)}, indent=2))
    return 0

if __name__ == "__main__": raise SystemExit(main())
