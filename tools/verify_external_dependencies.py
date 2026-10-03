#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlparse

_SHA256 = re.compile(r'^[0-9a-f]{64}$')

class VerificationError(RuntimeError):
    pass

def load_manifest(path: Path):
    data = json.loads(path.read_text(encoding='utf-8'))
    if data.get('schemaVersion') != 1:
        raise VerificationError('unsupported external dependency manifest schema')
    items = data.get('dependencies')
    if not isinstance(items, list):
        raise VerificationError('dependencies must be an array')
    return data

def get_dependency(data, dependency_id: str):
    matches = [x for x in data['dependencies'] if x.get('id') == dependency_id]
    if len(matches) != 1:
        raise VerificationError('dependency id must resolve exactly once: ' + dependency_id)
    return matches[0]

def validate_pin(item):
    if item.get('state') != 'pinned':
        raise VerificationError('dependency is not approved/pinned: ' + str(item.get('id')))
    version = item.get('version')
    url = item.get('url')
    digest = item.get('sha256')
    if not isinstance(version, str) or not version.strip():
        raise VerificationError('pinned dependency is missing version')
    if not isinstance(url, str) or urlparse(url).scheme != 'https':
        raise VerificationError('pinned dependency URL must use https')
    if not isinstance(digest, str) or not _SHA256.fullmatch(digest):
        raise VerificationError('pinned dependency SHA-256 must be 64 lowercase hexadecimal characters')
    return version, url, digest

def sha256(path: Path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def verify(manifest_path: Path, dependency_id: str, file_path: Path):
    data = load_manifest(manifest_path)
    item = get_dependency(data, dependency_id)
    _, _, expected = validate_pin(item)
    if not file_path.is_file():
        raise VerificationError('dependency file does not exist: ' + str(file_path))
    actual = sha256(file_path)
    if actual != expected:
        raise VerificationError('dependency SHA-256 mismatch')
    return item

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--id', required=True)
    parser.add_argument('--file', type=Path, required=True)
    args = parser.parse_args()
    try:
        item = verify(args.manifest, args.id, args.file)
    except VerificationError as ex:
        print('FAILED: ' + str(ex))
        return 2
    print('PASS: {0} {1}'.format(item['id'], item['version']))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
