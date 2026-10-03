#!/usr/bin/env python3
from __future__ import annotations
import argparse, json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

def _sorted(value):
    return sorted(str(x) for x in (value or []))

def _audit_firefox(root:Path):
    findings=[]
    try: policy=json.loads((root/'dependencies/firefox-extension-permissions-policy.json').read_text(encoding='utf-8'))
    except Exception: return {'findings':['firefox-extension-permission-policy-invalid']}
    try: manifest=json.loads((root/'ADM/firefox-amo/manifest.json').read_text(encoding='utf-8'))
    except Exception: return {'findings':['firefox-extension-manifest-invalid']}
    if policy.get('schemaVersion') != 1: findings.append('firefox-extension-permission-policy-schema-unsupported')
    if manifest.get('manifest_version') != policy.get('manifestVersion'):
        findings.append('firefox-extension-manifest-version-policy-mismatch')
    actual_permissions=_sorted(manifest.get('permissions'))
    expected_permissions=_sorted(policy.get('permissions'))
    if actual_permissions != expected_permissions:
        findings.append('firefox-extension-permissions-drift')
    gecko=((manifest.get('browser_specific_settings') or {}).get('gecko') or {}).get('id')
    if gecko != policy.get('geckoId'):
        findings.append('firefox-extension-id-policy-mismatch')
    rationale=policy.get('rationale') or {}
    groups={
      'tabs':'tabs','cookies':'cookies','contextMenus':'contextMenus/menus','menus':'contextMenus/menus',
      'activeTab':'activeTab','webRequest':'webRequest/webRequestBlocking','webRequestBlocking':'webRequest/webRequestBlocking',
      '*://*/*':'*://*/*'
    }
    for permission in actual_permissions:
        group=groups.get(permission,permission)
        if group not in rationale:
            findings.append('firefox-extension-permission-rationale-missing:'+permission)
    return {
      'manifestVersion':manifest.get('manifest_version'),
      'permissions':actual_permissions,
      'geckoId':gecko,
      'findings':sorted(set(findings))
    }

def audit(root:Path=ROOT):
    findings=[]
    try: policy=json.loads((root/'dependencies/extension-permissions-policy.json').read_text(encoding='utf-8'))
    except Exception: return {'schemaVersion':1,'findings':['extension-permission-policy-invalid']}
    try: manifest=json.loads((root/'ADM/chrome-extension/manifest.json').read_text(encoding='utf-8'))
    except Exception: return {'schemaVersion':1,'findings':['extension-manifest-invalid']}
    if policy.get('schemaVersion') != 1: findings.append('extension-permission-policy-schema-unsupported')
    if manifest.get('manifest_version') != policy.get('manifestVersion'):
        findings.append('extension-manifest-version-policy-mismatch')
    actual_permissions=_sorted(manifest.get('permissions'))
    expected_permissions=_sorted(policy.get('permissions'))
    if actual_permissions != expected_permissions:
        findings.append('extension-permissions-drift')
    actual_hosts=_sorted(manifest.get('host_permissions'))
    expected_hosts=_sorted(policy.get('hostPermissions'))
    if actual_hosts != expected_hosts:
        findings.append('extension-host-permissions-drift')
    matches=[]
    for item in manifest.get('content_scripts') or []: matches.extend(item.get('matches') or [])
    if _sorted(set(matches)) != _sorted(set(policy.get('contentScriptMatches') or [])):
        findings.append('extension-content-script-matches-drift')
    rationale=policy.get('rationale') or {}
    for permission in actual_permissions:
        group=permission
        if permission in {'tabs','webNavigation'}: group='tabs/webNavigation'
        if permission in {'storage','alarms','contextMenus'}: group='storage/alarms/contextMenus'
        if group not in rationale:
            findings.append('extension-permission-rationale-missing:'+permission)
    firefox=_audit_firefox(root)
    findings.extend(firefox.get('findings') or [])
    return {
      'schemaVersion':1,
      'manifestVersion':manifest.get('manifest_version'),
      'permissions':actual_permissions,
      'hostPermissions':actual_hosts,
      'contentScriptMatches':_sorted(set(matches)),
      'firefox':firefox,
      'findings':sorted(set(findings))
    }

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,default=ROOT); ap.add_argument('--output',type=Path)
    args=ap.parse_args(); result=audit(args.root.resolve()); text=json.dumps(result,indent=2,sort_keys=True)+'\n'
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(text,encoding='utf-8')
    else: print(text,end='')
    return 0 if not result['findings'] else 2
if __name__=='__main__': raise SystemExit(main())
