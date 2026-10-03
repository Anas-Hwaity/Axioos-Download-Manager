#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path

LINE = re.compile(r'^([0-9a-f]{64})  ([^/\\]+)$')


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def build_manifest(paths):
    entries = []
    seen = set()
    for value in paths:
        path = Path(value)
        if path.is_symlink() or not path.is_file():
            raise ValueError(f'not-a-regular-release-artifact:{path}')
        name = path.name
        key = name.casefold()
        if key in seen:
            raise ValueError(f'duplicate-release-artifact-name:{name}')
        seen.add(key)
        entries.append({'name': name, 'sha256': sha256(path), 'bytes': path.stat().st_size})
    return sorted(entries, key=lambda item: (item['name'].casefold(), item['name']))


def write_manifest(paths, output):
    entries = build_manifest(paths)
    text = ''.join(f"{item['sha256']}  {item['name']}\n" for item in entries)
    Path(output).write_text(text, encoding='utf-8', newline='\n')
    return entries


def verify_manifest(manifest, directory):
    findings = []
    seen = set()
    for number, raw in enumerate(Path(manifest).read_text(encoding='utf-8').splitlines(), 1):
        match = LINE.fullmatch(raw)
        if not match:
            findings.append(f'invalid-manifest-line:{number}')
            continue
        expected, name = match.groups()
        key = name.casefold()
        if key in seen:
            findings.append(f'duplicate-manifest-entry:{name}')
            continue
        seen.add(key)
        path = Path(directory) / name
        if path.is_symlink() or not path.is_file():
            findings.append(f'missing-artifact:{name}')
        elif sha256(path) != expected:
            findings.append(f'sha256-mismatch:{name}')
    return sorted(set(findings))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path)
    parser.add_argument('--verify', type=Path)
    parser.add_argument('--directory', type=Path, default=Path.cwd())
    parser.add_argument('artifacts', nargs='*', type=Path)
    args = parser.parse_args()
    if bool(args.output) == bool(args.verify):
        parser.error('choose exactly one of --output or --verify')
    if args.output:
        if not args.artifacts:
            parser.error('at least one artifact is required')
        for item in write_manifest(args.artifacts, args.output):
            print(f"{item['sha256']}  {item['name']}")
        return 0
    findings = verify_manifest(args.verify, args.directory)
    for finding in findings:
        print(finding)
    return 0 if not findings else 2


if __name__ == '__main__':
    raise SystemExit(main())
