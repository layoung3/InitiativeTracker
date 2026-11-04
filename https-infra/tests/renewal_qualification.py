#!/usr/bin/env python3
"""Debian 13 / Certbot 4 renewal fixture. Never contacts a CA or DNS provider.

No-op uses the actual Certbot executable and its real lineage/due-date logic.
Replacement uses the real CLI/plugin selection/storage with an in-process fake
ACME client; only obtain_certificate is synthetic. Trust/probe injections exist
only in this fixture, never in runtime configuration.
"""
import argparse
import errno
from pathlib import Path
import socket
import sys
import tempfile
from types import SimpleNamespace
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import certbot_adapter as a
from certbot._internal import cli, main, storage
from certbot._internal.plugins import disco


def backend(argv):
    # CLI subprocess with a fake ACME boundary; all real Certbot renewal/storage
    # code still runs. The fixture deliberately contains no issuer account.
    root = Path(argv[argv.index('--config-dir') + 1]).parent
    def obtain(domains, new_key):
        assert domains == ['hub.owner-domain.org']
        return ((root / 'next.pem').read_bytes(), (root / 'ca.pem').read_bytes(),
                SimpleNamespace(pem=(root / 'next.key').read_bytes()), None)
    client = Mock(obtain_certificate=Mock(side_effect=obtain))
    with patch.object(main, '_init_le_client', return_value=client), \
         patch.object(socket.socket, 'connect', side_effect=AssertionError('Network forbidden in fixture')):
        result = main.main(argv)
    assert client.obtain_certificate.call_count == 1
    return result or 0


def qualify(issuer=None, store=None):
    with tempfile.TemporaryDirectory(prefix='it-debian-renewal-') as directory:
        scratch = Path(directory)
        root = Path(issuer) if issuer else scratch / 'issuer'
        browser = Path(store) if store else scratch / 'browser'
        root.mkdir(mode=0o700, exist_ok=True)
        browser.mkdir(mode=0o750, exist_ok=True)
        root.chmod(0o700)
        def openssl(*argv):
            a.f.run(['/usr/bin/openssl', *argv], capture=False)
        openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', str(root / 'ca.key'),
                '-out', str(root / 'ca.pem'), '-days', '90', '-subj', '/CN=Disposable Renewal Fixture',
                '-addext', 'basicConstraints=critical,CA:TRUE')
        ext = root / 'leaf.ext'
        ext.write_text('subjectAltName=DNS:hub.owner-domain.org\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\n')
        for name in ['leaf', 'next']:
            openssl('req', '-new', '-newkey', 'rsa:2048', '-nodes', '-keyout', str(root / (name + '.key')),
                    '-out', str(root / (name + '.csr')), '-subj', '/CN=hub.owner-domain.org')
            openssl('x509', '-req', '-in', str(root / (name + '.csr')), '-CA', str(root / 'ca.pem'),
                    '-CAkey', str(root / 'ca.key'), '-CAcreateserial', '-out', str(root / (name + '.pem')),
                    '-days', '89', '-extfile', str(ext))
        for path in root.iterdir():
            path.chmod(0o600)
        (root / 'adapter.ini').write_text('')
        credentials = root / 'cloudflare.ini'
        credentials.write_text('dns_cloudflare_api_token = synthetic-fixture-only\n')
        credentials.chmod(0o600)
        value = {'hostname': 'hub.owner-domain.org', 'store': str(browser), 'port': 5445,
                 'acme': {'backend_profile': a.DEBIAN_PROFILE, 'expected_version': '4.0.0',
                          'executable': '/usr/bin/certbot', 'state_directory': str(root),
                          'dns_plugin': 'cloudflare', 'credentials_file': str(credentials),
                          'propagation_seconds': 60, 'directory_url': a.LE_PRODUCTION,
                          'email': 'fixture@example.org', 'scope_reviewed': True, 'terms_approved': True}}
        a.qualify_debian_packages(value)
        parsed = cli.prepare_and_parse_args(disco.PluginsRegistry.find_all(), a.arguments(value, 'issue')[1:])
        lineage = storage.RenewableCert.new_lineage(value['hostname'], (root / 'leaf.pem').read_bytes(),
                  (root / 'leaf.key').read_bytes(), (root / 'ca.pem').read_bytes(), parsed)
        assert not lineage.should_autorenew()
        original_validate, original_execute = a.f.validate_certificate, a.execute
        validate = lambda h, c, k, **kw: original_validate(h, c, k, _test_ca=root / 'ca.pem')
        replacement = False
        def runner(argv, **kwargs):
            if replacement and argv[0] == '/usr/bin/certbot' and 'renew' in argv:
                argv = ['/usr/bin/python3', '-I', str(Path(__file__).resolve()), 'backend', *argv[1:]]
            return a.f.run(argv, **kwargs)
        with patch.object(a.f, 'validate_certificate', side_effect=validate), \
             patch.object(a.f, 'probe', return_value=None), \
             patch.object(a, 'execute', side_effect=lambda v, action, **kw:
                          original_execute(v, action, **kw, _runner=runner, _validator=validate)):
            first = a.f.renew(value, apply=True)
            assert first['changed']
            before = (browser / 'active.json').read_bytes()
            issuer_before = (root / 'active.json').read_bytes()
            result = a.f.renew(value, apply=True)
            assert not result['changed'] and result['phase'] == 'noop-verified'
            assert before == (browser / 'active.json').read_bytes()
            assert issuer_before == (root / 'active.json').read_bytes()
            # Force the real due-date policy without a CA request or sleep. The
            # production argv explicitly disables the redundant Certbot delay.
            renewal = root / 'config' / 'renewal' / (value['hostname'] + '.conf')
            renewal.write_text('renew_before_expiry = 100 days\n' + renewal.read_text())
            replacement = True
            result = a.f.renew(value, apply=True)
            assert result['changed'] and result['phase'] == 'activation-verified'
            assert result['generation'] != first['generation']
            assert (browser / first['generation'] / 'bundle.pfx').is_file()
        print('PASS: Debian Certbot 4 real not-due CLI, unchanged export/manifest, fake-ACME due replacement, validated activation and retained previous bundle')


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == 'backend':
        raise SystemExit(backend(sys.argv[2:]))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--issuer')
    parser.add_argument('--store')
    parser.add_argument('--check-sandbox', action='store_true')
    args = parser.parse_args()
    if args.check_sandbox:
        status = Path('/proc/self/status').read_text()
        assert 'NoNewPrivs:\t1' in status
        assert 'CapEff:\t0000000000000000' in status
        for forbidden in ['/etc/it-renewal-must-not-write', '/repo/it-renewal-must-not-write']:
            try:
                Path(forbidden).write_text('forbidden')
            except OSError as error:
                assert error.errno in {errno.EROFS, errno.EACCES}
            else:
                raise AssertionError('ProtectSystem did not confine writes')
    qualify(args.issuer, args.store)
