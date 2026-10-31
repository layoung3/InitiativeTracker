import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import certbot_adapter as a

class CertbotAdapterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='it-acme-fixture-')
        cls.fixture = Path(cls.temp.name)
        def openssl(*args):
            subprocess.run(['/usr/bin/openssl', *args], cwd=cls.fixture, check=True, capture_output=True)
        openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'root.key', '-out', 'root.pem', '-days', '4', '-subj', '/CN=Fixture Root', '-addext', 'basicConstraints=critical,CA:TRUE')
        openssl('req', '-new', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'leaf.key', '-out', 'leaf.csr', '-subj', '/CN=hub.owner-domain.org')
        (cls.fixture / 'leaf.ext').write_text('subjectAltName=DNS:hub.owner-domain.org\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\n')
        openssl('x509', '-req', '-in', 'leaf.csr', '-CA', 'root.pem', '-CAkey', 'root.key', '-CAcreateserial', '-out', 'leaf.pem', '-days', '3', '-extfile', 'leaf.ext')

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def setUp(self):
        self.temp_case = tempfile.TemporaryDirectory(prefix='it-acme-case-')
        self.root = Path(self.temp_case.name)
        self.backend = self.root / 'fake-certbot'
        # A real process implements the CLI fixture, without Certbot/plugin/CA/DNS access.
        self.backend.write_text('''#!/usr/bin/python3
import json, os, pathlib, sys
args = sys.argv[1:]
if args == ['--version']:
    print('certbot 5.9.0')
    raise SystemExit(0)
root = pathlib.Path(__file__).parent
(root / 'argv.json').write_text(json.dumps(args))
challenge = root / 'fixture-challenge'
challenge.write_text('synthetic DNS challenge')
try:
    print('synthetic-secret-must-not-be-captured')
    if (root / 'fail').exists():
        raise SystemExit(3)
    host = args[args.index('--cert-name') + 1]
    config = pathlib.Path(args[args.index('--config-dir') + 1])
    archive, live, renewal = config / 'archive' / host, config / 'live' / host, config / 'renewal'
    for path in [archive, live, renewal]:
        path.mkdir(parents=True, mode=0o700, exist_ok=True)
    version = len(list(archive.glob('privkey*.pem'))) + 1
    for name, source, mode in [('fullchain', 'fixture-chain', 0o644), ('privkey', 'fixture-key', 0o600)]:
        target = archive / (name + str(version) + '.pem')
        target.write_bytes((root / source).read_bytes())
        target.chmod(mode)
        link = live / (name + '.pem')
        link.unlink(missing_ok=True)
        link.symlink_to('../../archive/' + host + '/' + target.name)
    (renewal / (host + '.conf')).write_text('fixture maintained renewal state')
finally:
    challenge.unlink(missing_ok=True)
    (root / 'cleanup-completed').write_text('fixture cleanup')
''')
        self.backend.chmod(0o700)
        (self.root / 'fixture-chain').write_bytes((self.fixture / 'leaf.pem').read_bytes())
        (self.root / 'fixture-key').write_bytes((self.fixture / 'leaf.key').read_bytes())
        (self.root / 'fixture-key').chmod(0o600)
        self.credentials = self.root / 'dns-credentials.ini'
        self.credentials.write_text('dns_provider_api_token = synthetic-secret\n')
        self.credentials.chmod(0o600)
        self.value = {'hostname': 'hub.owner-domain.org', 'renewal_timeout_seconds': 900,
                      'acme': {'state_directory': str(self.root), 'executable': str(self.backend), 'expected_version': '5.9.0',
                               'dns_plugin': 'cloudflare', 'credentials_file': str(self.credentials), 'propagation_seconds': 60,
                               'directory_url': 'https://acme.invalid/directory', 'email': 'owner@example.org',
                               'scope_reviewed': True, 'terms_approved': True}}

    def tearDown(self):
        self.temp_case.cleanup()

    def execute(self, action, **kw):
        return a.execute(self.value, action, _validator=lambda h, c, k: a.f.validate_certificate(h, c, k, _test_ca=self.fixture / 'root.pem'), **kw)

    def test_dry_run_never_invokes_backend_or_writes(self):
        before = set(self.root.iterdir())
        self.assertEqual('dry-run-no-network', self.execute('issue')['reason'])
        self.assertEqual(before, set(self.root.iterdir()))

    def test_provider_commands_are_dns_only_and_single_host(self):
        for plugin in a.PLUGINS:
            with self.subTest(plugin=plugin):
                self.value['acme']['dns_plugin'] = plugin
                args = a.arguments(self.value, 'issue')
                self.assertIn('certonly', args)
                self.assertEqual(['hub.owner-domain.org'], [args[i + 1] for i, arg in enumerate(args) if arg == '-d'])
                self.assertEqual('dns-01', args[args.index('--preferred-challenges') + 1])
                self.assertEqual('dns-' + plugin, args[args.index('--authenticator') + 1])
                self.assertIn('--dns-' + plugin + '-credentials', args)
                self.assertIn('--dns-' + plugin + '-propagation-seconds', args)
                self.assertNotIn('synthetic-secret', ' '.join(args))
                self.assertFalse(set(args) & {'--standalone', '--webroot', 'run', '--nginx', '--apache'})

    def test_initial_issue_exports_regular_private_validated_pair(self):
        self.execute('issue', apply=True)
        chain, key = a.exported_paths(self.value)
        self.assertFalse(chain.is_symlink() or key.is_symlink())
        self.assertEqual(0o600, key.stat().st_mode & 0o777)
        self.assertEqual(0o600, chain.stat().st_mode & 0o777)
        self.assertEqual(0o600, (self.root / 'active.json').stat().st_mode & 0o777)
        self.assertTrue((self.root / 'cleanup-completed').exists())
        self.assertFalse((self.root / 'fixture-challenge').exists())

    def test_renew_calls_real_fixture_backend_and_exports_success(self):
        self.execute('issue', apply=True)
        before = (self.root / 'active.json').read_bytes()
        count = len(list((self.root / 'exports').iterdir()))
        self.execute('renew', apply=True)
        self.assertEqual(count, len(list((self.root / 'exports').iterdir())))
        self.assertEqual(before, (self.root / 'active.json').read_bytes())
        args = json.loads((self.root / 'argv.json').read_text())
        self.assertIn('renew', args)
        self.assertNotIn('certonly', args)
        a.exported_paths(self.value)

    def test_renewal_failure_preserves_receipt_and_runs_fixture_cleanup(self):
        self.execute('issue', apply=True)
        before = (self.root / 'active.json').read_bytes()
        (self.root / 'fail').touch()
        with self.assertRaises(a.f.Rejected):
            self.execute('renew', apply=True)
        self.assertEqual(before, (self.root / 'active.json').read_bytes())
        self.assertFalse((self.root / 'fixture-challenge').exists())
        self.assertTrue((self.root / 'cleanup-completed').exists())

    def test_missing_initial_lineage_does_not_invoke_renew(self):
        with self.assertRaises(a.f.Rejected):
            self.execute('renew', apply=True)
        self.assertFalse((self.root / 'argv.json').exists())

    def test_no_duplicate_initial_issuance(self):
        self.execute('issue', apply=True)
        with self.assertRaises(a.f.Rejected):
            self.execute('issue', apply=True)

    def test_terms_scope_provider_and_propagation_gates(self):
        for key, value in [('terms_approved', False), ('scope_reviewed', False), ('dns_plugin', 'route53'),
                           ('dns_plugin', '--standalone'), ('propagation_seconds', 0), ('directory_url', 'http://acme.invalid/directory')]:
            old = self.value['acme'][key]
            self.value['acme'][key] = value
            try:
                with self.subTest(key=key), self.assertRaises(a.f.Rejected):
                    self.execute('issue', apply=True)
            finally:
                self.value['acme'][key] = old
        self.assertFalse((self.root / 'argv.json').exists())

    def test_credential_permissions_and_containment(self):
        self.credentials.chmod(0o644)
        with self.assertRaises(a.f.Rejected):
            self.execute('issue', apply=True)
        self.value['acme']['credentials_file'] = '/tmp/outside.ini'
        with self.assertRaises(a.f.Rejected):
            self.execute('issue', apply=True)

    def test_version_mismatch_rejected_before_issuance(self):
        self.value['acme']['expected_version'] = '0.0.0'
        with self.assertRaises(a.f.Rejected):
            self.execute('issue', apply=True)
        self.assertFalse((self.root / 'argv.json').exists())

    def test_escaped_and_mixed_archive_links_rejected(self):
        self.execute('issue', apply=True)
        live = self.root / 'config/live/hub.owner-domain.org'
        key = live / 'privkey.pem'
        key.unlink()
        key.symlink_to(self.credentials)
        with self.assertRaises(a.f.Rejected):
            a.export(self.value)
        key.unlink()
        archive = self.root / 'config/archive/hub.owner-domain.org'
        (archive / 'privkey2.pem').write_bytes((archive / 'privkey1.pem').read_bytes())
        (archive / 'privkey2.pem').chmod(0o600)
        key.symlink_to('../../archive/hub.owner-domain.org/privkey2.pem')
        with self.assertRaises(a.f.Rejected):
            a.export(self.value)

    def test_invalid_export_is_removed_and_receipt_is_unchanged(self):
        self.execute('issue', apply=True)
        before = (self.root / 'active.json').read_bytes()
        count = len(list((self.root / 'exports').iterdir()))
        (self.root / 'fixture-key').write_text('malformed key')
        with self.assertRaises(a.f.Rejected):
            self.execute('renew', apply=True)
        self.assertEqual(before, (self.root / 'active.json').read_bytes())
        self.assertEqual(count, len(list((self.root / 'exports').iterdir())))

    def test_unmanaged_renewal_hooks_are_rejected(self):
        self.execute('issue', apply=True)
        renewal = self.root / 'config/renewal/hub.owner-domain.org.conf'
        renewal.write_text('pre_hook = arbitrary-command\n')
        with self.assertRaises(a.f.Rejected):
            self.execute('renew', apply=True)

    def test_unused_noisy_output_is_discarded_and_capture_is_bounded(self):
        command = ['/usr/bin/python3', '-c', 'import sys; sys.stdout.write("x" * 2000000); sys.stderr.write("s" * 2000000)']
        self.assertEqual(b'', a.f.run(command, capture=False))
        with self.assertRaises(a.f.Rejected):
            a.f.run(command, max_output=4096)

if __name__ == '__main__':
    unittest.main()
