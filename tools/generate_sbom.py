#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()


def spdx_id(source, name, version):
    slug = re.sub(r'[^A-Za-z0-9.-]+', '-', name).strip('-') or 'dependency'
    digest = hashlib.sha256(f'{source}\0{name}\0{version}'.encode()).hexdigest()[:12]
    return f'SPDXRef-Dependency-{slug[:80]}-{digest}'


def package(name, version, source, download='NOASSERTION', declared='NOASSERTION', comment=None):
    item = {
        'SPDXID': spdx_id(source, name, version),
        'name': name,
        'versionInfo': version or 'NOASSERTION',
        'downloadLocation': download or 'NOASSERTION',
        'filesAnalyzed': False,
        'licenseConcluded': 'NOASSERTION',
        'licenseDeclared': declared or 'NOASSERTION',
        'copyrightText': 'NOASSERTION',
        'externalRefs': [],
    }
    if comment:
        item['comment'] = comment
    return item


def nuget_packages(root):
    found = {}
    tracked = git(root, 'ls-files', '*.csproj', '*.props', '*.targets', '*.projitems').splitlines()
    for rel in tracked:
        path = root / rel
        try:
            tree = ET.parse(path)
        except ET.ParseError:
            continue
        for node in tree.iter():
            if node.tag.rsplit('}', 1)[-1] != 'PackageReference':
                continue
            name = (node.attrib.get('Include') or node.attrib.get('Update') or '').strip()
            version = (node.attrib.get('Version') or '').strip()
            if not version:
                child = next((x for x in node if x.tag.rsplit('}', 1)[-1] == 'Version'), None)
                version = (child.text or '').strip() if child is not None else ''
            if not name:
                continue
            key = (name.lower(), version)
            found[key] = package(name, version, 'nuget',
                                 f'https://www.nuget.org/packages/{name}/{version}' if version else 'NOASSERTION')
    return list(found.values())


def npm_packages(root):
    found = {}
    for rel in git(root, 'ls-files', '*package-lock.json').splitlines():
        path = root / rel
        try:
            data = json.loads(path.read_text(encoding='utf-8'))
        except Exception:
            continue
        entries = data.get('packages')
        if not isinstance(entries, dict):
            continue
        for key, meta in entries.items():
            if not key or not key.startswith('node_modules/') or not isinstance(meta, dict):
                continue
            name = key.split('/node_modules/')[-1]
            version = str(meta.get('version') or '')
            declared = str(meta.get('license') or 'NOASSERTION')
            download = str(meta.get('resolved') or 'NOASSERTION')
            pkg = package(name, version, 'npm', download, declared)
            pkg['externalRefs'] = [{
                'referenceCategory': 'PACKAGE-MANAGER',
                'referenceType': 'purl',
                'referenceLocator': f'pkg:npm/{name}@{version}' if version else f'pkg:npm/{name}',
            }]
            found[(name.lower(), version)] = pkg
    return list(found.values())


def external_packages(root):
    data = json.loads((root / 'dependencies/external-binaries.json').read_text(encoding='utf-8'))
    result = []
    for item in data.get('dependencies', []):
        name = str(item.get('id') or 'unknown')
        version = str(item.get('version') or '')
        state = str(item.get('state') or 'unknown')
        download = str(item.get('url') or 'NOASSERTION')
        declared = str(item.get('license') or 'NOASSERTION')
        note = str(item.get('notes') or '').strip()
        comment = f'state={state}' + (f'; {note}' if note else '')
        pkg = package(name, version, 'external-binary', download, declared, comment)
        if item.get('sha256'):
            pkg['checksums'] = [{'algorithm': 'SHA256', 'checksumValue': str(item['sha256'])}]
        result.append(pkg)
    return result


def build_sbom(root):
    root = Path(root).resolve()
    head = git(root, 'rev-parse', 'HEAD')
    created = git(root, 'show', '-s', '--format=%cI', 'HEAD')
    created = datetime.fromisoformat(created.replace('Z', '+00:00')).astimezone(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    deps = nuget_packages(root) + npm_packages(root) + external_packages(root)
    unique = {item['SPDXID']: item for item in deps}
    deps = sorted(unique.values(), key=lambda x: (x['name'].lower(), x['versionInfo'], x['SPDXID']))
    root_pkg = {
        'SPDXID': 'SPDXRef-Package-ADM',
        'name': 'ADM REBUILD',
        'versionInfo': head,
        'downloadLocation': 'NOASSERTION',
        'filesAnalyzed': False,
        'licenseConcluded': 'NOASSERTION',
        'licenseDeclared': 'GPL-2.0-only',
        'copyrightText': 'NOASSERTION',
    }
    relationships = [{
        'spdxElementId': 'SPDXRef-DOCUMENT',
        'relationshipType': 'DESCRIBES',
        'relatedSpdxElement': 'SPDXRef-Package-ADM',
    }]
    relationships.extend({
        'spdxElementId': 'SPDXRef-Package-ADM',
        'relationshipType': 'DEPENDS_ON',
        'relatedSpdxElement': item['SPDXID'],
    } for item in deps)
    return {
        'spdxVersion': 'SPDX-2.3',
        'dataLicense': 'CC0-1.0',
        'SPDXID': 'SPDXRef-DOCUMENT',
        'name': f'ADM-REBUILD-{head[:12]}',
        'documentNamespace': f'https://spdx.org/spdxdocs/adm-rebuild-{head}',
        'creationInfo': {
            'created': created,
            'creators': ['Tool: ADM deterministic SBOM generator'],
        },
        'documentDescribes': ['SPDXRef-Package-ADM'],
        'packages': [root_pkg, *deps],
        'relationships': relationships,
    }


def write_sbom(root, output):
    payload = build_sbom(root)
    Path(output).write_text(json.dumps(payload, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    return payload


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    payload = write_sbom(args.root.resolve(), args.output.resolve())
    print(json.dumps({'output': str(args.output.resolve()), 'packages': len(payload['packages']), 'head': payload['packages'][0]['versionInfo']}, indent=2))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
