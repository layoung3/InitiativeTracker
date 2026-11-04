#!/usr/bin/env python3
"""Run the Debian rootfs fixture using the reviewed unit's exact hardening.

CI-only: requires root, systemd PID1 and an exported disposable Debian image.
No production hosts, units, trust stores, interfaces or issuer state are used.
"""
import argparse
import configparser
from pathlib import Path
import subprocess
import uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rootfs', type=Path, required=True)
args = parser.parse_args()
root = args.rootfs.resolve(strict=True)
assert root != Path('/') and (root / 'repo/https-infra/tests/renewal_qualification.py').is_file()
unit = configparser.ConfigParser(interpolation=None)
unit.optionxform = str
unit.read(Path(__file__).parents[1] / 'templates/initiativetracker-certificate-renew.service.example')
service = dict(unit['Service'])
# Keep the same user/group and all hardening. ExecStart selects the harness;
# filesystem paths must refer to the disposable root instead of the runner.
service.pop('ExecStart')
# systemd.exec: an unprefixed ReadWritePaths is relative to the manager's root,
# even with RootDirectory. '+' rebases the *same* allowlist into that rootfs.
# https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html#ReadWritePaths=
service['ReadWritePaths'] = ' '.join('+' + path for path in service['ReadWritePaths'].split())
for name in ['initiativetracker-browser-tls', 'initiativetracker-issuer']:
    directory = root / 'var/lib' / name
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
command = ['systemd-run', '--wait', '--pipe', '--collect', '--unit=it-renewal-fixture-' + uuid.uuid4().hex,
           '--property=RootDirectory=' + str(root)]
command.extend('--property=' + key + '=' + value for key, value in service.items())
command += ['/usr/bin/python3', '-I', '/repo/https-infra/tests/renewal_qualification.py', '--check-sandbox',
            '--issuer', '/var/lib/initiativetracker-issuer', '--store', '/var/lib/initiativetracker-browser-tls']
subprocess.run(command, check=True)
print('PASS: reviewed renewal Service properties, actual systemd sandbox and disposable Debian rootfs')
