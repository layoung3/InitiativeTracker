#!/usr/bin/env python3
"""CI-only adaptation of existing N9 Chromium tests; trust stays in a disposable profile."""
import argparse
import os
from pathlib import Path
import subprocess

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',type=Path,required=True)
parser.add_argument('--sandbox',type=Path,required=True)
args=parser.parse_args()
if os.environ.get('GITHUB_ACTIONS') != 'true' or 'RUNNER_TEMP' not in os.environ:
    raise SystemExit('This helper is only for an explicit disposable CI fixture')
runner=Path(os.environ['RUNNER_TEMP']).resolve();sandbox=args.sandbox.resolve()
if sandbox == runner or not sandbox.is_relative_to(runner):raise SystemExit('Disposable runner child required')
text=args.source.read_text()
for old,new in [('hub.owner-domain.org','hub-fixture.colophon.stream'),('https://hub-fixture.colophon.stream:5445','https://hub-fixture.colophon.stream'),
    ('Disposable Process Only Root','Disposable Kernel Test Root')]:
    if old not in text:raise SystemExit('Existing trusted-browser fixture contract changed')
    text=text.replace(old,new)
sandbox.mkdir(mode=0o700,exist_ok=True)
(sandbox/'n9-443.cjs').write_text(text)
(sandbox/'disposable-n9-browser-fixture').write_text('disposable container process trust only\n')
nss=sandbox/'browser-home/.pki/nssdb';nss.mkdir(mode=0o700,parents=True)
for command in [['certutil','-N','-d','sql:'+str(nss),'--empty-password'],
    ['certutil','-A','-d','sql:'+str(nss),'-n','Disposable Kernel Test Root','-t','C,,','-i',str(sandbox/'ca.pem')]]:
    subprocess.run(command,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
# Keep Playwright assertion/call logs (which can contain synthetic codes) out of
# CI logs. Emit only static test names/counters and enumerated browser errors.
(sandbox/'run.cjs').write_text('''
const {spawnSync}=require('node:child_process');
const result=spawnSync(process.execPath,['--test','--test-reporter=tap',__dirname+'/n9-443.cjs'],{env:process.env,encoding:'utf8',maxBuffer:2*1024*1024,timeout:240000});
for(const line of (result.stdout||'').split('\\n'))if(/^(?:ok |not ok |# (?:tests|pass|fail|skipped|duration_ms))/.test(line))console.log(line);
for(const code of new Set(((result.stdout||'')+(result.stderr||'')).match(/net::ERR_[A-Z_]+/g)||[]))console.log('Browser diagnostic: '+code);
if(result.status!==0)console.log('Disposable trusted 443 browser fixture failed; private call logs suppressed.');
process.exit(result.status===0?0:1);
''')
print('Prepared disposable 443 browser fixture; no OS trust store changed')
