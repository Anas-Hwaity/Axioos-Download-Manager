#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]

def package_refs():
    rows=[]
    for path in sorted((ROOT/'ADM').rglob('*.csproj')):
        try: root=ET.parse(path).getroot()
        except ET.ParseError: continue
        for node in root.iter():
            if node.tag.split('}')[-1] != 'PackageReference': continue
            name=node.attrib.get('Include') or node.attrib.get('Update')
            if not name: continue
            version=node.attrib.get('Version')
            if not version:
                for child in node:
                    if child.tag.split('}')[-1]=='Version' and child.text:
                        version=child.text.strip(); break
            rows.append({'kind':'nuget','name':name,'version':version or 'conditional/unspecified','declaredBy':str(path.relative_to(ROOT)).replace('\\','/')})
    return rows

def bundled_files():
    rows=[]
    patterns=('*.dll','*.exe','*.so','*.dylib','*.jar')
    seen=set()
    for pattern in patterns:
        for path in sorted((ROOT/'ADM').rglob(pattern)):
            if any(part in {'bin','obj'} for part in path.parts): continue
            rel=str(path.relative_to(ROOT)).replace('\\','/')
            if rel in seen: continue
            seen.add(rel)
            data=path.read_bytes()
            rows.append({'kind':'bundled-binary','path':rel,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
    return rows

def browser_manifest():
    path=ROOT/'ADM/chrome-extension/manifest.json'
    data=json.loads(path.read_text(encoding='utf-8'))
    return {'manifestVersion':data.get('manifest_version'),'permissions':sorted(data.get('permissions',[])),'hostPermissions':sorted(data.get('host_permissions',[]))}

def build():
    return {'schemaVersion':1,'nuget':package_refs(),'bundledBinaries':bundled_files(),'browserExtension':browser_manifest(),'licenseVerificationStatus':'requires-authoritative-release-review'}

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--output',type=Path); args=ap.parse_args()
    text=json.dumps(build(),indent=2,sort_keys=True)+'\n'
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(text,encoding='utf-8')
    else: print(text,end='')
    return 0
if __name__=='__main__': raise SystemExit(main())
