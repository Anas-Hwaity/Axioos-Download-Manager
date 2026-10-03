#!/usr/bin/env python3
import argparse
import json
import re
import subprocess
from pathlib import Path
from urllib.parse import urlparse
import dependency_lock_audit
import extension_permission_audit
import workflow_pin_audit

SHA256 = re.compile(r'^[0-9a-f]{64}$')
REQUIRED_DOCS = [
    'LICENSE', 'SECURITY.md', 'CONTRIBUTING.md',
    'docs/release/release-process.md',
    'docs/release/verification.md',
    'docs/release/third-party-notices.md',
    'docs/release/extension-store-package.md',
]
REQUIRED_RELEASE_TOOLS = [
    'tools/generate_sbom.py',
    'tools/release_checksums.py',
    'tools/generate_provenance.py',
    'tools/package_extension.py',
    'tools/workflow_pin_audit.py',
]


def evaluate(root: Path, public_release: bool):
    findings = []
    for rel in REQUIRED_DOCS:
        if not (root / rel).is_file(): findings.append('missing-required-document:' + rel)
    for rel in REQUIRED_RELEASE_TOOLS:
        if not (root / rel).is_file(): findings.append('missing-release-tool:' + rel)

    manifest_path = root / 'dependencies/external-binaries.json'
    try:
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        if manifest.get('schemaVersion') != 1: findings.append('unsupported-external-dependency-manifest-schema')
        dependencies = manifest.get('dependencies', [])
        if not isinstance(dependencies, list):
            findings.append('invalid-external-dependency-list')
            dependencies = []
        for item in dependencies:
            dep_id = str(item.get('id') or 'unknown')
            if item.get('state') != 'pinned':
                findings.append('external-dependency-not-pinned:' + dep_id)
                continue
            if not str(item.get('version') or '').strip(): findings.append('external-dependency-missing-version:' + dep_id)
            if urlparse(str(item.get('url') or '')).scheme != 'https': findings.append('external-dependency-non-https-url:' + dep_id)
            if not SHA256.fullmatch(str(item.get('sha256') or '')): findings.append('external-dependency-invalid-sha256:' + dep_id)
    except Exception:
        findings.append('invalid-external-dependency-manifest')

    if public_release:
        lock_audit = dependency_lock_audit.audit(root)
        findings.extend('dependency-lock:' + item for item in lock_audit.get('findings', []))

        permission_audit = extension_permission_audit.audit(root)
        findings.extend('extension-permission:' + item for item in permission_audit.get('findings', []))

        workflow_audit = workflow_pin_audit.audit(root)
        findings.extend('workflow-pin:' + item for item in workflow_audit.get('findings', []))

    if public_release:
        try:
            status = subprocess.run(['git','-C',str(root),'status','--porcelain=v1','--untracked-files=all'],
                                    text=True,capture_output=True,check=True).stdout.strip()
            if status: findings.append('git-working-tree-not-clean')
        except Exception:
            findings.append('git-cleanliness-unverifiable')
    return sorted(set(findings))


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--public',action='store_true')
    parser.add_argument('--json',action='store_true')
    args=parser.parse_args()
    findings=evaluate(args.root.resolve(),args.public)
    payload={'ok':not findings,'publicRelease':args.public,'findings':findings}
    print(json.dumps(payload,indent=2) if args.json else ('PASS' if not findings else '\n'.join('BLOCK: '+x for x in findings)))
    return 0 if not findings else 2

if __name__=='__main__': raise SystemExit(main())
