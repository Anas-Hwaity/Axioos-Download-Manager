#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
EXACT=re.compile(r'^\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?$')
WINDOWS_RELEASE_PROJECTS={
    'ADM/ADM.App.Host/ADM.App.Host.csproj',
    'ADM/ADM.Wpf.UI/ADM.Wpf.UI.csproj',
    'ADM/NativeMessagingHost/NativeMessagingHost.csproj',
}


def package_version(node):
    v=node.attrib.get('Version')
    if v: return v.strip()
    for child in node:
        if child.tag.split('}')[-1]=='Version' and child.text:
            return child.text.strip()
    return ''


def audit(root:Path=ROOT):
    projects=[]; findings=[]
    for path in sorted((root/'ADM').rglob('*.csproj')):
        rel=path.relative_to(root).as_posix()
        if rel not in WINDOWS_RELEASE_PROJECTS: continue
        refs=[]
        try: xml=ET.parse(path).getroot()
        except ET.ParseError:
            findings.append(f'project-xml-invalid:{rel}')
            continue
        for node in xml.iter():
            if node.tag.split('}')[-1] != 'PackageReference': continue
            name=node.attrib.get('Include') or node.attrib.get('Update') or ''
            version=package_version(node)
            refs.append({'name':name,'version':version})
            low=version.lower()
            if not version:
                findings.append(f'package-version-missing:{rel}:{name}')
            elif any(x in version for x in ('*','[',']','(',')','$(')) or 'latest' in low:
                findings.append(f'package-version-not-exact:{rel}:{name}:{version}')
            elif 'preview' in low or 'alpha' in low or 'beta' in low or 'rc' in low:
                findings.append(f'package-version-prerelease:{rel}:{name}:{version}')
            elif not EXACT.fullmatch(version):
                findings.append(f'package-version-unrecognized:{rel}:{name}:{version}')
        lock=path.with_name('packages.lock.json')
        if refs and not lock.is_file(): findings.append(f'nuget-lock-missing:{rel}')
        projects.append({'project':rel,'packageReferences':refs,'lockFile':lock.relative_to(root).as_posix() if lock.is_file() else None})
    global_json=root/'global.json'
    sdk_version=None
    if not global_json.is_file():
        findings.append('dotnet-sdk-pin-missing:global.json')
    else:
        try:
            data=json.loads(global_json.read_text(encoding='utf-8'))
            sdk_version=str((data.get('sdk') or {}).get('version') or '').strip()
            if not sdk_version: findings.append('dotnet-sdk-version-missing:global.json')
        except Exception: findings.append('dotnet-sdk-pin-invalid:global.json')
    return {'schemaVersion':1,'projects':projects,'dotnetSdkVersion':sdk_version,'findings':sorted(set(findings))}


def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,default=ROOT); ap.add_argument('--output',type=Path)
    args=ap.parse_args(); result=audit(args.root.resolve())
    text=json.dumps(result,indent=2,sort_keys=True)+'\n'
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(text,encoding='utf-8')
    else: print(text,end='')
    return 0 if not result['findings'] else 2
if __name__=='__main__': raise SystemExit(main())
