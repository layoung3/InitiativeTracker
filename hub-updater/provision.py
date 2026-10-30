"""Proposed fresh-device recipe, not a claim about the deployed sudo policy.

Called only by the installer after all original hashes have been verified.
No standalone apply CLI. Host injection is solely for disposable unit fixtures.
"""
import json
import re
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def sudo_policy(username):
    if not re.fullmatch(r'[a-z_][a-z0-9_-]{0,31}', username):
        raise ValueError('Unsupported sudoers username')
    return ('# Fresh-device InitiativeTracker GUI update entry point; no arguments.\n'
            + username + ' ALL=(root) NOPASSWD: /usr/local/sbin/initiativetracker-install-update ""\n')


def provision(host, desktop, manifest, package):
    host.mkdir('/var/cache/initiativetracker-updater', 0o755)
    host.mkdir('/var/cache/initiativetracker-updater/incoming', 0o755)
    host.mkdir('/var/backups/initiativetracker', 0o700)
    release = json.loads(manifest.read_text(encoding='utf-8-sig'))
    incoming = '/var/cache/initiativetracker-updater/incoming/'
    # Stage names its transfer release-manifest.json. The installed root updater
    # requires a final basename ending .manifest.json; preserve that contract.
    name = 'InitiativeTracker-' + release['version'] + '-linux-arm64.manifest.json'
    host.copy(manifest, incoming + name)
    host.copy(package, incoming + package.name)
    host.write('/etc/initiativetracker/updater.conf', 'MANIFEST_SOURCE=' + incoming + name + '\n', 0o644)
    for name, destination in (
        ('initiativetracker-install-update', '/usr/local/sbin/'),
        ('initiativetracker-check-update', '/usr/local/bin/'),
    ):
        host.copy(ROOT / 'runtime' / name, destination + name, 0o755)
    local = desktop.pw_dir + '/.local/lib/initiativetracker-hub'
    # Root owns the approved executable; desktop-owned parent paths are never
    # consulted by sudo. This launcher itself has no privileged arguments.
    host.mkdir(local, 0o755, (desktop.pw_uid, desktop.pw_gid))
    host.copy(ROOT / 'runtime/initiativetracker-hub-launch', local + '/initiativetracker-hub-launch', 0o755)
    policy = sudo_policy(desktop.pw_name)
    with tempfile.NamedTemporaryFile(mode='w', prefix='it-sudoers-', dir=host.path('/var/lib/initiativetracker-install')) as candidate:
        candidate.write(policy)
        candidate.flush()
        host.run('visudo', '-cf', candidate.name)
    host.write('/etc/sudoers.d/initiativetracker-update', policy, 0o440)
    host.run('visudo', '-c')
