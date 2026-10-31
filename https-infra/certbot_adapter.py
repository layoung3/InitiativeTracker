#!/usr/bin/env python3
"""Reviewed Certbot DNS-01 adapter. No package installation or network in dry-run."""
import argparse
import fcntl
import json
import os
from pathlib import Path
import re
import pwd
import stat
import uuid
from urllib.parse import urlsplit

import foundation as f

# File-credential DNS plugins distributed by the Certbot project. No provider is selected.
PLUGINS = frozenset('cloudflare digitalocean dnsimple dnsmadeeasy gehirn google linode luadns nsone ovh rfc2136 sakuracloud'.split())

def control_path(path, *, directory=False, private=False, executable=False):
    path = Path(path)
    if not path.is_absolute():
        raise f.Rejected('Absolute backend paths required')
    for parent in [path, *path.parents][:-1]:
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or (info.st_uid not in {0, os.geteuid()} and not (str(parent) == '/tmp' and info.st_mode & stat.S_ISVTX)):
            raise f.Rejected('Untrusted backend path')
        if info.st_mode & 0o022 and not (str(parent) in {'/tmp', '/var/tmp'} and info.st_mode & stat.S_ISVTX):
            raise f.Rejected('Writable backend path')
    info = path.lstat()
    if directory and not stat.S_ISDIR(info.st_mode) or not directory and not stat.S_ISREG(info.st_mode):
        raise f.Rejected('Incorrect backend path type')
    if private and info.st_mode & 0o077:
        raise f.Rejected('Issuer state and credentials must be private')
    if executable and not info.st_mode & stat.S_IXUSR:
        raise f.Rejected('Backend executable is not executable')
    return path

def settings(value):
    host = f.hostname(value['hostname'])
    acme = value['acme']
    root = Path(acme['state_directory'])
    if not root.is_absolute() or root == Path('/'):
        raise f.Rejected('Dedicated absolute issuer state required')
    plugin = acme['dns_plugin']
    if plugin not in PLUGINS:
        raise f.Rejected('Select a reviewed file-credential Certbot DNS plugin')
    propagation = acme['propagation_seconds']
    if type(propagation) is not int or not 10 <= propagation <= 1800:
        raise f.Rejected('DNS propagation delay must be between 10 and 1800 seconds')
    timeout = value.get('renewal_timeout_seconds', 900)
    if type(timeout) is not int or not propagation + 60 <= timeout <= 3600:
        raise f.Rejected('Backend timeout must include propagation and validation time')
    if acme.get('scope_reviewed') is not True:
        raise f.Rejected('Credential scope and challenge delegation need owner review')
    url = urlsplit(acme['directory_url'])
    if url.scheme != 'https' or not url.hostname or url.username or url.password or url.query or url.fragment or url.port not in {None, 443}:
        raise f.Rejected('A reviewed HTTPS ACME directory is required')
    email = acme['email']
    if not isinstance(email, str) or not re.fullmatch(r'[^\s@\x00-\x1f]+@[^\s@\x00-\x1f]+\.[^\s@\x00-\x1f]+', email):
        raise f.Rejected('An owner-selected ACME account contact is required')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', acme['expected_version']):
        raise f.Rejected('An explicitly reviewed Certbot version is required')
    credential = Path(acme['credentials_file'])
    if not credential.is_absolute() or not credential.is_relative_to(root):
        raise f.Rejected('Keep DNS credentials inside the private issuer directory')
    return host, acme, root, plugin, timeout

def arguments(value, action):
    host, acme, root, plugin, _ = settings(value)
    common = [str(acme['executable']), '--config', str(root / 'adapter.ini'), '--config-dir', str(root / 'config'),
              '--work-dir', str(root / 'work'), '--logs-dir', str(root / 'logs'), '--cert-name', host,
              '--non-interactive', '--no-directory-hooks', '--preferred-challenges', 'dns-01',
              '--authenticator', 'dns-' + plugin, '--dns-' + plugin + '-credentials', str(acme['credentials_file']),
              '--dns-' + plugin + '-propagation-seconds', str(acme['propagation_seconds']),
              '--server', acme['directory_url'], '--quiet']
    if action == 'issue':
        if acme.get('terms_approved') is not True:
            raise f.Rejected('Initial issuance requires explicit CA terms approval')
        return common + ['certonly', '--agree-tos', '--email', acme['email'], '--key-type', 'ecdsa', '--elliptic-curve', 'secp256r1', '-d', host]
    if action == 'renew':
        return common + ['renew']
    raise f.Rejected('Unsupported ACME action')

def read_archive(root, host, name):
    live = root / 'config' / 'live' / host / (name + '.pem')
    control_path(live.parent, directory=True)
    if not live.is_symlink():
        raise f.Rejected('Expected a Certbot managed lineage link')
    target = live.resolve(strict=True)
    archive = root / 'config' / 'archive' / host
    if target.parent != archive or not re.fullmatch(name + r'[1-9][0-9]*\.pem', target.name):
        raise f.Rejected('Certbot lineage escaped its selected archive')
    control_path(target, private=name == 'privkey')
    descriptor = os.open(target, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, 'rb') as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_size > (32768 if name == 'privkey' else 131072):
            raise f.Rejected('Invalid Certbot archive input')
        material = stream.read(131073)
        if len(material) > (32768 if name == 'privkey' else 131072):
            raise f.Rejected('Certbot archive grew beyond limit')
    return material, re.search(r'([1-9][0-9]*)\.pem$', target.name)[1]

def export(value, *, _validator=f.validate_certificate):
    host, _, root, _, _ = settings(value)
    chain, chain_version = read_archive(root, host, 'fullchain')
    key, key_version = read_archive(root, host, 'privkey')
    if chain_version != key_version:
        raise f.Rejected('Certificate archive generation changed during export')
    output = root / 'exports'
    output.mkdir(mode=0o700, exist_ok=True)
    control_path(output, directory=True, private=True)
    # The backend can report success without renewal. Reuse a validated immutable pair.
    if (root / 'active.json').exists():
        old_chain, old_key = exported_paths(value)
        if f.chain_identity(old_chain.read_bytes()) == f.chain_identity(chain) and old_key.read_bytes() == key:
            _validator(host, old_chain, old_key)
            return old_chain.parent
    generation = output / uuid.uuid4().hex
    generation.mkdir(mode=0o700)
    try:
        for name, material in [('fullchain.pem', chain), ('privkey.pem', key)]:
            fd = os.open(generation / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, 'wb') as stream:
                stream.write(material)
                stream.flush()
                os.fsync(stream.fileno())
        _validator(host, generation / 'fullchain.pem', generation / 'privkey.pem')
        for directory in [generation, output]:
            descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
        # Exports are immutable snapshots. The orchestrator gets both paths from one receipt.
        return generation
    except Exception:
        for path in generation.iterdir():
            path.unlink()
        generation.rmdir()
        raise

def ambient_config_paths():
    # Exact Linux defaults for pinned Certbot 5.9.0. The subprocess environment has
    # neither HOME nor XDG_CONFIG_HOME, so expanduser uses the effective user's pwd home.
    home = Path(pwd.getpwuid(os.geteuid()).pw_dir)
    return [Path('/etc/letsencrypt/cli.ini'), home / '.config' / 'letsencrypt' / 'cli.ini']

def reject_ambient_config():
    # ConfigArgParse reads defaults in addition to explicit --config. Reject any
    # ambient file/link before even invoking --version; never delete or alter it.
    for path in ambient_config_paths():
        if os.path.lexists(path):
            raise f.Rejected('Ambient Certbot configuration requires an isolated issuer environment')


def execute(value, action, *, apply=False, _runner=f.run, _validator=f.validate_certificate):
    argv = arguments(value, action)
    host, acme, root, _, timeout = settings(value)
    if not apply:
        return {'changed': False, 'reason': 'dry-run-no-network', 'action': action}
    reject_ambient_config()
    control_path(root, directory=True, private=True)
    control_path(acme['credentials_file'], private=True)
    control_path(acme['executable'], executable=True)
    lock_fd = os.open(root / '.adapter-lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(lock_fd, 'w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        # Ambient defaults were rejected separately: explicit --config alone is not isolation.
        # Private state and directory hooks remain separately constrained.
        fd = os.open(root / 'adapter.ini', os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
        os.close(fd)
        reject_ambient_config()
        version = _runner([str(acme['executable']), '--version'], max_output=4096).decode().strip()
        if version != 'certbot ' + acme['expected_version']:
            raise f.Rejected('Certbot version differs from the reviewed version')
        renewal = root / 'config' / 'renewal' / (host + '.conf')
        if action == 'issue' and renewal.exists():
            raise f.Rejected('A lineage already exists; use renewal')
        if action == 'renew' and not renewal.is_file():
            raise f.Rejected('Initial issuance is required before renewal')
        if renewal.exists():
            control_path(renewal)
            if renewal.stat().st_size > 65536:
                raise f.Rejected('Renewal configuration exceeds limit')
            if re.search(r'(?im)^\s*(?:pre_hook|post_hook|deploy_hook)\s*=\s*[^\s#]', renewal.read_text()):
                raise f.Rejected('Renewal hooks require a separate adapter review')
        reject_ambient_config()
        _runner(argv, timeout=timeout, capture=False)
        snapshot = export(value, _validator=_validator)
        receipt = {'snapshot': snapshot.name, 'hostname': host}
        f.write_active(root, receipt)  # Fixed private receipt; contains no key or provider secret.
        os.chmod(root / 'active.json', 0o600)
        return {'changed': True, 'action': action, 'snapshot': snapshot.name}

def exported_paths(value):
    host, _, root, _, _ = settings(value)
    control_path(root / 'active.json', private=True)
    if (root / 'active.json').stat().st_size > 1024:
        raise f.Rejected('Issuer receipt exceeds limit')
    receipt = json.loads((root / 'active.json').read_text())
    if receipt['hostname'] != host or not re.fullmatch('[a-f0-9]{32}', receipt['snapshot']):
        raise f.Rejected('Invalid issuer export receipt')
    snapshot = root / 'exports' / receipt['snapshot']
    chain, key = f.private_path(snapshot / 'fullchain.pem'), f.private_path(snapshot / 'privkey.pem')
    if chain.stat().st_size > 131072 or key.stat().st_size > 32768:
        raise f.Rejected('Issuer snapshot exceeds limit')
    return chain, key

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['issue', 'renew'])
    parser.add_argument('--config', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    try:
        f.private_path(args.config)
        value = json.loads(args.config.read_text())
        print(json.dumps(execute(value, args.action, apply=args.apply)))
        return 0
    except (f.Rejected, OSError, ValueError, KeyError):
        print(json.dumps({'changed': False, 'reason': 'certbot-operation-rejected'}))
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
