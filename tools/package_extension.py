#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, zipfile
from pathlib import Path
FIXED=(1980,1,1,0,0,0)
MANIFEST='ADM-EXTENSION-PACKAGE-MANIFEST.json'

def digest(data:bytes)->str: return hashlib.sha256(data).hexdigest()
def runtime_files(root:Path):
    for p in sorted(root.rglob('*'), key=lambda x:x.relative_to(root).as_posix()):
        if not p.is_file(): continue
        rel=p.relative_to(root).as_posix()
        if rel.startswith('test/') or rel.endswith('.test.mjs') or rel in {MANIFEST,'.DS_Store'}: continue
        yield rel,p

def _info(name:str):
    i=zipfile.ZipInfo(name,FIXED); i.compress_type=zipfile.ZIP_DEFLATED; i.create_system=3; i.external_attr=(0o100644<<16); return i

def build(root:Path,out:Path):
    rows=[]; payload=[]
    for rel,p in runtime_files(root):
        data=p.read_bytes(); payload.append((rel,data)); rows.append({'path':rel,'bytes':len(data),'sha256':digest(data)})
    names={r['path'] for r in rows}
    if 'manifest.json' not in names: raise RuntimeError('extension manifest.json is missing')
    doc={'schema':'adm-extension-package-v1','files':rows}
    m=(json.dumps(doc,sort_keys=True,indent=2)+'\n').encode()
    out.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(out,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9,strict_timestamps=True) as z:
        z.writestr(_info(MANIFEST),m)
        for rel,data in payload: z.writestr(_info(rel),data)
    return doc

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]/'ADM/chrome-extension'); ap.add_argument('--output',type=Path,required=True); ns=ap.parse_args(); build(ns.root.resolve(),ns.output); return 0
if __name__=='__main__': raise SystemExit(main())
