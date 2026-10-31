#!/usr/bin/env python3
"""Offline preflight and certificate lifecycle. Never changes DNS, services or app DBs."""
import argparse
import contextlib
import fcntl
import ipaddress
import json
import os
from pathlib import Path
import re
import shutil
import socket
import ssl
import stat
import subprocess
import tempfile
import time
import uuid

class Rejected(ValueError):
    pass

def hostname(value):
    # ASCII A-label FQDNs only; owner must explicitly convert/review international names.
    if not isinstance(value, str) or len(value) > 253 or value != value.lower():
        raise Rejected('Use a lower-case ASCII FQDN')
    labels = value.split('.')
    if len(labels) < 3 or any(not re.fullmatch(r'[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?', x) for x in labels):
        raise Rejected('Use a dedicated host below an owned public domain')
    if labels[-1].isdigit() or labels[-1] in {'local', 'localhost', 'test', 'invalid', 'example', 'internal', 'lan', 'home', 'arpa', 'onion'}:
        raise Rejected('A publicly issuable DNS hostname is required')
    return value

def local_address(value):
    try:
        address = ipaddress.ip_address(value)
    except ValueError:
        raise Rejected('An explicit local IP address is required') from None
    # Only RFC1918 IPv4 and ULA IPv6; no interface-scoped link-local ambiguity.
    allowed = ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', 'fc00::/7')
    if not any(address.version == (network := ipaddress.ip_network(n)).version and address in network for n in allowed):
        raise Rejected('Address must be RFC1918 IPv4 or ULA IPv6')
    return str(address)

def private_path(path, directory=False, executable=False):
    path = Path(path)
    if not path.is_absolute():
        raise Rejected('Absolute paths required')
    # Do not follow links, including ancestor links. Sticky /tmp is allowed for fixtures.
    for parent in reversed([path, *path.parents][:-1]):
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or (info.st_uid not in {0, os.geteuid()} and not (stat.S_ISDIR(info.st_mode) and info.st_mode & stat.S_ISVTX)):
            raise Rejected('Untrusted path ownership or symlink')
        if stat.S_ISDIR(info.st_mode) and info.st_mode & 0o022 and not info.st_mode & stat.S_ISVTX:
            raise Rejected('Writable ancestor directory')
    info = path.lstat()
    if directory != stat.S_ISDIR(info.st_mode) or not directory and not stat.S_ISREG(info.st_mode):
        raise Rejected('Incorrect file type')
    if info.st_mode & (0o027 if directory or executable else 0o137):
        raise Rejected('Private path permissions are too broad')
    return path

def run(args, *, data=None):
    # Never inherit tracing, OpenSSL trust overrides or user config. No command output on failure.
    env = {'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'LANG': 'C', 'HOME': '/nonexistent'}
    try:
        result = subprocess.run(args, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                check=True, timeout=60, env=env)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError):
        raise Rejected('External validation or renewal command failed') from None
    return result.stdout

def validate_certificate(host, fullchain, key, *, _test_ca=None):
    hostname(host)
    private_path(fullchain)
    private_path(key)
    # Bounded inputs; decoding happens in an isolated OpenSSL process with timeout.
    if fullchain.stat().st_size > 131072 or key.stat().st_size > 32768:
        raise Rejected('Certificate material exceeds limit')
    with tempfile.TemporaryDirectory(prefix='it-cert-') as folder:
        leaf = Path(folder) / 'leaf.pem'
        text = fullchain.read_bytes()
        blocks = re.findall(rb'-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----', text, re.S)
        if not 1 <= len(blocks) <= 8:
            raise Rejected('Invalid certificate chain')
        leaf.write_bytes(blocks[0] + b'\n')
        args = ['/usr/bin/openssl', 'verify', '-purpose', 'sslserver', '-verify_hostname', host,
                '-untrusted', str(fullchain)]
        if _test_ca:  # Unit-test injection only; not exposed by any config or CLI command.
            args += ['-CAfile', str(_test_ca), '-no-CApath', '-no-CAstore']
        args.append(str(leaf))
        run(args)
        san = run(['/usr/bin/openssl', 'x509', '-in', str(leaf), '-noout', '-ext', 'subjectAltName']).decode()
        names = re.findall(r'DNS:([^,\s]+)', san)
        if names != [host] or 'IP Address:' in san:
            raise Rejected('Use a single exact DNS SAN; no wildcard or CN fallback')
        # Require at least 24 hours remaining at activation; runtime checks actual validity.
        run(['/usr/bin/openssl', 'x509', '-in', str(leaf), '-checkend', '86400', '-noout'])
        cert_pub = run(['/usr/bin/openssl', 'x509', '-in', str(leaf), '-pubkey', '-noout'])
        key_pub = run(['/usr/bin/openssl', 'pkey', '-in', str(key), '-pubout'])
        if cert_pub != key_pub:
            raise Rejected('Certificate and private key do not match')

def config(path):
    private_path(path)
    value = json.loads(Path(path).read_text())
    hostname(value['hostname'])
    if value.get('port', 5445) != 5445:
        raise Rejected('This checkpoint reserves browser TLS port 5445; agent TLS is separate')
    root = Path(value['store'])
    if not root.is_absolute() or root == Path('/'):
        raise Rejected('Use an absolute dedicated persistent store')
    if value.get('resolution') not in {'managed-lan', 'hotspot'}:
        raise Rejected('Select a supported local resolver strategy')
    return value

def resolution(host, addresses, assigned):
    hostname(host)
    wanted = {local_address(x) for x in addresses}
    active = {str(ipaddress.ip_address(x)) for x in assigned}
    if not wanted or not wanted <= active:
        raise Rejected('Every advertised address must be explicitly assigned to this hub')
    return wanted

def resolve_preflight(host, expected, resolver=socket.getaddrinfo):
    try:
        found = {x[4][0] for x in resolver(host, None, type=socket.SOCK_STREAM)}
    except OSError:
        return {'ready': False, 'reason': 'dns-unavailable'}
    # Reject stale AAAA as well as stale A; no fallback to an unrelated interface/address.
    return {'ready': found == set(expected), 'reason': 'resolved' if found == set(expected) else 'dns-mismatch'}

def render_dns(host, addresses, assigned):
    addresses = resolution(host, addresses, assigned)
    # host-record is exact; local prevents upstream leakage of this private runtime name.
    return '\n'.join([f'local=/{host}/', f'host-record={host},' + ','.join(sorted(addresses)), ''])

def assigned_addresses():
    values = json.loads(run(['/usr/sbin/ip', '-j', 'address', 'show']).decode())
    return [a['local'] for link in values for a in link.get('addr_info', [])
            if a.get('scope') == 'global']

def read_active(root):
    path = root / 'active.json'
    if not path.exists():
        return None
    private_path(path)
    value = json.loads(path.read_text())
    if not re.fullmatch('[a-f0-9]{32}', value['generation']):
        raise Rejected('Invalid generation')
    return value

def write_active(root, value):
    # The writer owns the store; group/app can read but cannot replace any object.
    temp = root / ('.active-' + uuid.uuid4().hex)
    try:
        fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o640)
        os.fchmod(fd, 0o640)
        with os.fdopen(fd, 'w') as stream:
            json.dump(value, stream)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, root / 'active.json')
        directory_fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    finally:
        temp.unlink(missing_ok=True)

def probe(host, port, generation, *, _test_context=None):
    # Local connection + exact SNI; no DNS dependency or certificate bypass in deployment.
    context = _test_context or ssl.create_default_context()
    with socket.create_connection(('127.0.0.1', port), timeout=3) as raw:
        with context.wrap_socket(raw, server_hostname=host) as connection:
            connection.sendall(f'GET /system/https-status HTTP/1.0\r\nHost: {host}:{port}\r\nConnection: close\r\n\r\n'.encode('ascii'))
            response = b''
            while chunk := connection.recv(4096):
                response += chunk
                if len(response) > 32768:
                    raise Rejected('Invalid activation response')
    headers, body = response.split(b'\r\n\r\n', 1)
    value = json.loads(body)
    if not headers.startswith(b'HTTP/1.1 200') and not headers.startswith(b'HTTP/1.0 200'):
        raise Rejected('Activation status rejected')
    if value.get('ready') is not True or value.get('generation') != generation:
        raise Rejected('Application has not activated this generation')

def wait_for_probe(callback, host, port, generation):
    for attempt in range(8):
        try:
            callback(host, port, generation)
            return
        except (OSError, ValueError, Rejected):
            if attempt == 7:
                raise Rejected('Application TLS activation failed') from None
            time.sleep(1)

def activate(settings, fullchain, key, *, dry_run=True, _probe=probe, _test_ca=None):
    host = hostname(settings['hostname'])
    validate_certificate(host, fullchain, key, _test_ca=_test_ca)
    root = private_path(settings['store'], directory=True)
    if dry_run:
        return {'validated': True, 'changed': False}
    lock = root / '.lock'
    lock_fd = os.open(lock, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(lock_fd, 'w') as stream:
        fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        old = read_active(root)
        generation = uuid.uuid4().hex
        target = root / generation
        target.mkdir(mode=0o750)
        target.chmod(0o2750 if root.stat().st_mode & stat.S_ISGID else 0o750)
        try:
            # Empty PFX password is protected by OS permissions, never by a release secret.
            bundle = run(['/usr/bin/openssl', 'pkcs12', '-export', '-in', str(fullchain), '-inkey', str(key),
                          '-passout', 'stdin'], data=b'\n')
            fd = os.open(target / 'bundle.pfx', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o640)
            os.fchmod(fd, 0o640)
            with os.fdopen(fd, 'wb') as output:
                output.write(bundle)
                output.flush()
                os.fsync(output.fileno())
            write_active(root, {'generation': generation})
            try:
                wait_for_probe(_probe, host, settings.get('port', 5445), generation)
            except Exception:
                if old:
                    write_active(root, old)
                    try:
                        wait_for_probe(_probe, host, settings.get('port', 5445), old['generation'])
                    except Exception:
                        raise Rejected('Rollback recorded; application recovery is unconfirmed') from None
                else:
                    (root / 'active.json').unlink(missing_ok=True)
                raise Rejected('Activation failed; previous manifest restored') from None
            return {'validated': True, 'changed': True, 'generation': generation}
        except Exception:
            # Retain immutable material on uncertain reload; never remove an in-use key.
            raise

def rollback(settings, generation, *, apply=False):
    if not re.fullmatch('[a-f0-9]{32}', generation or ''):
        raise Rejected('Invalid rollback generation')
    root = private_path(settings['store'], directory=True)
    bundle = private_path(root / generation / 'bundle.pfx')
    with tempfile.TemporaryDirectory(prefix='it-rollback-') as folder:
        fullchain, key = Path(folder) / 'chain.pem', Path(folder) / 'key.pem'
        fullchain.write_bytes(run(['/usr/bin/openssl', 'pkcs12', '-in', str(bundle), '-nokeys', '-passin', 'stdin'], data=b'\n'))
        key.write_bytes(run(['/usr/bin/openssl', 'pkcs12', '-in', str(bundle), '-nocerts', '-nodes', '-passin', 'stdin'], data=b'\n'))
        fullchain.chmod(0o600)
        key.chmod(0o600)
        # Revalidates current trust/validity; expired archives never become a fallback.
        return activate(settings, fullchain, key, dry_run=not apply)


def renew(settings, *, apply=False, _runner=run):
    """Call one reviewed, credential-free argv executable. Backend owns ACME/DNS details."""
    backend = private_path(settings['renewal_adapter'], executable=True)
    if not backend.stat().st_mode & stat.S_IXUSR:
        raise Rejected('Renewal adapter is not executable')
    # Output paths are fixed in root-owned settings, never taken from subprocess output.
    if not apply:
        return {'changed': False, 'reason': 'dry-run-no-network'}
    root = private_path(settings['store'], directory=True)
    descriptor = os.open(root / '.renewal-lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'w') as locked:
        fcntl.flock(locked, fcntl.LOCK_EX | fcntl.LOCK_NB)
        _runner([str(backend)])
        return activate(settings, Path(settings['fullchain']), Path(settings['private_key']), dry_run=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['preflight', 'dns-template', 'activate', 'renew', 'rollback'])
    parser.add_argument('--config', type=Path, required=True)
    parser.add_argument('--generation')
    parser.add_argument('--address', action='append', default=[])
    parser.add_argument('--apply', action='store_true', help='Explicit certificate-store write/renewal approval; never configures network services')
    args = parser.parse_args()
    try:
        settings = config(args.config)
        if args.action in {'preflight', 'dns-template'}:
            selected = resolution(settings['hostname'], args.address, assigned_addresses())
            if args.action == 'dns-template':
                print(render_dns(settings['hostname'], selected, selected), end='')
            else:
                result = resolve_preflight(settings['hostname'], selected)
                print(json.dumps(result))
                return 0 if result['ready'] else 1
        elif args.action == 'rollback':
            print(json.dumps(rollback(settings, args.generation, apply=args.apply)))
        elif args.action == 'activate':
            print(json.dumps(activate(settings, Path(settings['fullchain']), Path(settings['private_key']), dry_run=not args.apply)))
        else:
            print(json.dumps(renew(settings, apply=args.apply)))
        return 0
    except (Rejected, OSError, ValueError, KeyError):
        # Static message: parser/provider/OpenSSL exceptions may contain credentials or file contents.
        print(json.dumps({'ready': False, 'reason': 'preflight-or-operation-rejected'}))
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
