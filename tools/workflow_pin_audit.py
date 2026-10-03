#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
USE=re.compile(r'^\s*-?\s*uses:\s*([^\s#]+)',re.M)
FULL_SHA=re.compile(r'^[0-9a-fA-F]{40}$')

def audit(root:Path=ROOT):
    findings=[]; uses=[]
    workflows=root/'.github/workflows'
    for path in sorted(list(workflows.glob('*.yml'))+list(workflows.glob('*.yaml'))):
        text=path.read_text(encoding='utf-8')
        rel=path.relative_to(root).as_posix()
        for spec in USE.findall(text):
            if spec.startswith('./'):
                uses.append({'workflow':rel,'uses':spec,'local':True})
                continue
            if '@' not in spec:
                findings.append(f'action-ref-missing:{rel}:{spec}')
                uses.append({'workflow':rel,'uses':spec,'local':False})
                continue
            action,ref=spec.rsplit('@',1)
            uses.append({'workflow':rel,'uses':spec,'local':False})
            if not FULL_SHA.fullmatch(ref):
                findings.append(f'action-not-full-sha:{rel}:{action}@{ref}')
    return {'schemaVersion':1,'uses':uses,'findings':sorted(set(findings))}

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,default=ROOT); ap.add_argument('--output',type=Path)
    args=ap.parse_args(); result=audit(args.root.resolve()); text=json.dumps(result,indent=2,sort_keys=True)+'\n'
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(text,encoding='utf-8')
    else: print(text,end='')
    return 0 if not result['findings'] else 2
if __name__=='__main__': raise SystemExit(main())
