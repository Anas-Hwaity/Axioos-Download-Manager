#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, subprocess
from pathlib import Path

SCHEMA = "adm-offline-provenance-v1"

def _git(root: Path, *args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()

def sha256(path: Path) -> str:
    h=hashlib.sha256()
    with path.open('rb') as f:
        for chunk in iter(lambda:f.read(1024*1024), b''): h.update(chunk)
    return h.hexdigest()

def build(root: Path, artifacts: list[Path]) -> dict:
    status=_git(root,"status","--porcelain")
    if status:
        raise RuntimeError("release provenance requires a clean Git tree")
    head=_git(root,"rev-parse","HEAD")
    tree=_git(root,"rev-parse","HEAD^{tree}")
    commit_time=int(_git(root,"show","-s","--format=%ct","HEAD"))
    rows=[]
    seen=set()
    for item in sorted((p.resolve() for p in artifacts), key=lambda p:p.name.lower()):
        if not item.is_file(): raise FileNotFoundError(item)
        if item.name in seen: raise RuntimeError(f"duplicate artifact basename: {item.name}")
        seen.add(item.name)
        rows.append({"name":item.name,"bytes":item.stat().st_size,"sha256":sha256(item)})
    return {
        "schema":SCHEMA,
        "source":{"headCommit":head,"headTree":tree,"commitUnixTime":commit_time,"dirty":False},
        "artifacts":rows,
        "attestation":{"signed":False,"note":"Offline provenance only; release signing/attestation remains a separate gate."}
    }

def main() -> int:
    ap=argparse.ArgumentParser()
    ap.add_argument("--root",type=Path,default=Path(__file__).resolve().parents[1])
    ap.add_argument("--output",type=Path,required=True)
    ap.add_argument("artifact",nargs="*",type=Path)
    ns=ap.parse_args()
    doc=build(ns.root.resolve(),ns.artifact)
    ns.output.parent.mkdir(parents=True,exist_ok=True)
    ns.output.write_text(json.dumps(doc,sort_keys=True,indent=2)+"\n",encoding="utf-8")
    return 0
if __name__=="__main__": raise SystemExit(main())
