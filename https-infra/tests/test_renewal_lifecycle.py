"""Issue #13: full renewal paths, synthetic material only, no ACME/DNS access."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import certbot_adapter as a
import test_certbot_adapter as adapter_tests
import test_foundation as foundation_tests


class RenewalLifecycleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        adapter_tests.CertbotAdapterTests.setUpClass()
        foundation_tests.FoundationTests.setUpClass()

    @classmethod
    def tearDownClass(cls):
        adapter_tests.CertbotAdapterTests.tearDownClass()
        foundation_tests.FoundationTests.tearDownClass()

    def setUp(self):
        self.fixture = adapter_tests.CertbotAdapterTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.browser = tempfile.TemporaryDirectory(prefix='it-renewal-browser-')
        self.addCleanup(self.browser.cleanup)
        self.root = Path(self.browser.name)
        self.value = self.fixture.value | {'store': self.browser.name, 'port': 5445}
        self.fixture.execute('issue', apply=True)
        self.validator = a.f.validate_certificate
        self.validate = lambda h, c, k, **kw: self.validator(h, c, k, _test_ca=self.fixture.fixture / 'root.pem')
        self.probes = []
        self.probe = lambda *args: self.probes.append(args)
        self.addCleanup(patch.stopall)
        patch.object(a.f, 'validate_certificate', side_effect=lambda *args, **kw: self.validate(*args, **kw)).start()
        patch.object(a.f, 'probe', side_effect=lambda *args, **kw: self.probe(*args)).start()
        execute = a.execute
        patch.object(a, 'execute', side_effect=lambda v, action, **kw: execute(v, action, **kw, _validator=self.validate)).start()
        self.first = a.f.renew(self.value, apply=True)
        self.old = (self.root / 'active.json').read_bytes()
        self.bundle = self.root / self.first['generation'] / 'bundle.pfx'
        self.old_bundle = self.bundle.read_bytes()
        self.issuer = self.fixture.root / 'active.json'
        self.old_issuer = self.issuer.read_bytes()

    def unchanged(self):
        self.assertEqual(self.old, (self.root / 'active.json').read_bytes())
        self.assertEqual(self.old_bundle, self.bundle.read_bytes())

    def test_not_due_noop_validates_and_probes_without_manifest_or_key_writes(self):
        before = {p: (p.stat().st_ino, p.stat().st_mtime_ns, p.read_bytes())
                  for root in [self.root, self.fixture.root / 'exports']
                  for p in root.rglob('*') if p.is_file()}
        issuer_stat = self.issuer.stat()
        with patch.object(a.f, 'write_active', wraps=a.f.write_active) as writer:
            result = a.f.renew(self.value, apply=True)
        writer.assert_not_called()
        self.assertFalse(result['changed'])
        self.assertEqual('noop-verified', result['phase'])
        self.assertEqual(self.first['generation'], self.probes[-1][2])
        self.assertEqual(issuer_stat.st_mtime_ns, self.issuer.stat().st_mtime_ns)
        for p, receipt in before.items():
            self.assertEqual(receipt, (p.stat().st_ino, p.stat().st_mtime_ns, p.read_bytes()))

    def replace_fixture(self, name='next'):
        source = foundation_tests.FoundationTests.root
        (self.fixture.root / 'fixture-chain').write_bytes((source / (name + '.pem')).read_bytes())
        (self.fixture.root / 'fixture-key').write_bytes((source / (name + '.key')).read_bytes())
        # Fixture-only trust contains both independently generated test CAs.
        ca = self.fixture.root / 'combined-ca.pem'
        ca.write_bytes((source / 'ca.pem').read_bytes() + (self.fixture.fixture / 'root.pem').read_bytes())
        self.validate = lambda h, c, k, **kw: self.validator(h, c, k, _test_ca=ca)

    def test_successful_replacement_is_acknowledged_without_restart(self):
        self.replace_fixture()
        result = a.f.renew(self.value, apply=True)
        self.assertTrue(result['changed'])
        self.assertNotEqual(self.first['generation'], result['generation'])
        self.assertEqual('activation-verified', result['phase'])
        self.assertEqual(result['generation'], self.probes[-1][2])
        self.assertEqual(self.old_bundle, self.bundle.read_bytes())

    def test_cli_failure_preserves_both_receipts_and_last_known_good(self):
        (self.fixture.root / 'fail').touch()
        with self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual(('backend-invocation', 'failed'), (error.exception.phase, error.exception.code))
        self.unchanged()
        self.assertEqual(self.old_issuer, self.issuer.read_bytes())

    def test_invalid_chain_san_purpose_and_key_never_replace_last_known_good(self):
        for name in ['wrong', 'wildcard', 'client']:
            with self.subTest(name=name):
                self.replace_fixture(name)
                with self.assertRaises(a.f.PhaseRejected) as error:
                    a.f.renew(self.value, apply=True)
                self.assertEqual('export-validation', error.exception.phase)
                self.unchanged()
                self.assertEqual(self.old_issuer, self.issuer.read_bytes())
        (self.fixture.root / 'fixture-key').write_text('not a private key')
        with self.assertRaises(a.f.PhaseRejected):
            a.f.renew(self.value, apply=True)
        self.unchanged()

    def test_permissions_failure_on_unchanged_lineage_still_rejects(self):
        self.fixture.credentials.chmod(0o644)
        with self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual('adapter-preflight', error.exception.phase)
        self.unchanged()

    def test_noop_service_failure_is_not_success(self):
        self.probe = lambda *args: (_ for _ in ()).throw(OSError('private network detail'))
        with patch.object(a.f.time, 'sleep'), self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual('noop-probe', error.exception.phase)
        self.unchanged()

    def test_failed_new_peer_restores_and_confirms_previous_generation(self):
        self.replace_fixture()
        def probe(host, port, generation, fingerprint):
            self.probes.append((host, port, generation, fingerprint))
            if generation != self.first['generation']:
                raise OSError('private backend detail')
        self.probe = probe
        with patch.object(a.f.time, 'sleep'), self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual('rollback-confirmed', error.exception.code)
        self.assertEqual(self.first['generation'], self.probes[-1][2])
        self.unchanged()

    def test_directory_sync_failure_after_manifest_rename_restores_previous(self):
        self.replace_fixture()
        original = a.f.write_active
        failed = False
        def write(root, value, **kw):
            nonlocal failed
            original(root, value, **kw)
            if root == self.root and not failed:
                failed = True
                raise OSError('simulated directory fsync failure after rename')
        with patch.object(a.f, 'write_active', side_effect=write), self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual('rollback-confirmed', error.exception.code)
        self.unchanged()

    def test_uncertain_rollback_reports_phase_and_retains_all_immutable_keys(self):
        self.replace_fixture()
        self.probe = lambda *args: (_ for _ in ()).throw(OSError('private backend detail'))
        with patch.object(a.f.time, 'sleep'), self.assertRaises(a.f.PhaseRejected) as error:
            a.f.renew(self.value, apply=True)
        self.assertEqual('rollback-probe', error.exception.phase)
        self.unchanged()
        self.assertEqual(2, len(list(self.root.glob('*/bundle.pfx'))))

    def test_noop_adapter_reports_unchanged_export(self):
        result = a.execute(self.value, 'renew', apply=True)
        self.assertFalse(result['changed'])
        self.assertEqual('export-unchanged', result['phase'])


class RenewalDiagnosticsTests(unittest.TestCase):
    def test_subprocess_failure_timeout_invocation_and_output_have_bounded_codes(self):
        cases = [(['/bin/false'], {}, 'failed'),
                 (['/no/such/private-executable'], {}, 'invocation'),
                 (['/usr/bin/python3', '-c', 'import time;time.sleep(30)'], {'timeout': .01}, 'timeout'),
                 (['/usr/bin/python3', '-c', 'print("private-account-token" * 1000)'], {'max_output': 10}, 'output-limit')]
        for args, kw, code in cases:
            with self.subTest(code=code), self.assertRaises(a.f.PhaseRejected) as error:
                with a.f.operation_phase('backend-invocation'):
                    a.f.run(args, **kw)
            self.assertEqual(code, error.exception.code)
            self.assertNotIn('private', json.dumps(error.exception.status()))

    def test_cli_diagnostics_do_not_print_arbitrary_exceptions_or_tracebacks(self):
        for failure in [a.f.Rejected('token SECRET'), OSError('private-key SECRET'), ValueError('issuer-account SECRET')]:
            output = io.StringIO()
            with patch.object(sys, 'argv', ['foundation', 'renew', '--config', '/private/SECRET', '--apply']), \
                 patch.object(a.f, 'config', side_effect=failure), contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                self.assertEqual(1, a.f.main())
            self.assertEqual({'ready': False, 'reason': 'certificate-operation-rejected',
                              'phase': 'configuration', 'code': 'rejected'}, json.loads(output.getvalue()))

    def test_renewal_unit_hardening_and_timer_opt_in_are_preserved(self):
        base = Path(__file__).parents[1] / 'templates'
        unit = (base / 'initiativetracker-certificate-renew.service.example').read_text()
        for setting in ['NoNewPrivileges=true', 'PrivateTmp=true', 'ProtectSystem=strict', 'ProtectHome=true',
                        'ProtectKernelTunables=true', 'ProtectKernelModules=true', 'ProtectControlGroups=true',
                        'RestrictSUIDSGID=true', 'CapabilityBoundingSet=\n', 'KillMode=control-group', 'UMask=0077',
                        'ReadWritePaths=/var/lib/initiativetracker-browser-tls /var/lib/initiativetracker-issuer']:
            self.assertIn(setting, unit)
        for module in [a, a.f]:
            text = Path(module.__file__).read_text()
            self.assertNotIn('systemctl', text)
            self.assertNotIn('restart', text)
