import importlib.util
import json
import os
from pathlib import Path
import ssl
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('foundation', Path(__file__).parents[1] / 'foundation.py')
f = importlib.util.module_from_spec(spec)
spec.loader.exec_module(f)

class FoundationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='it-https-tests-')
        cls.root = Path(cls.temp.name)
        cls.host = 'hub.owner-domain.org'
        cls.openssl('req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', 'ca.key', '-out', 'ca.pem',
                    '-subj', '/CN=Disposable Test Root', '-days', '3', '-addext', 'basicConstraints=critical,CA:TRUE')
        for name, san, eku in [('good', cls.host, 'serverAuth'), ('wrong', 'other.owner-domain.org', 'serverAuth'),
                               ('wildcard', '*.owner-domain.org', 'serverAuth'), ('client', cls.host, 'clientAuth'), ('next', cls.host, 'serverAuth')]:
            cls.openssl('req', '-new', '-newkey', 'rsa:2048', '-nodes', '-keyout', name + '.key',
                        '-out', name + '.csr', '-subj', '/CN=' + cls.host)
            (cls.root / (name + '.ext')).write_text(f'subjectAltName=DNS:{san}\nextendedKeyUsage={eku}\nbasicConstraints=critical,CA:FALSE\n')
            cls.openssl('x509', '-req', '-in', name + '.csr', '-CA', 'ca.pem', '-CAkey', 'ca.key', '-CAcreateserial',
                        '-out', name + '.pem', '-days', '2', '-extfile', name + '.ext')
        for path in cls.root.iterdir():
            path.chmod(0o600)

    @classmethod
    def openssl(cls, *args):
        subprocess.run(['/usr/bin/openssl', *args], cwd=cls.root, check=True, capture_output=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def settings(self, folder):
        return {'hostname': self.host, 'store': str(folder), 'port': 5445}

    def activate(self, folder, name="good", **kwargs):
        return f.activate(self.settings(folder), self.root / (name + ".pem"), self.root / (name + ".key"), _test_ca=self.root / 'ca.pem', **kwargs)

    def test_valid_hostname(self):
        for host in ['hub.owner-domain.org', 'h-2.sub.owner-domain.org', 'hub.xn--p1ai.ru']:
            self.assertEqual(host, f.hostname(host))

    def test_invalid_hostnames(self):
        for host in ['localhost', 'hub.local', 'hub.localhost', '*.owner-domain.org', 'https://hub.domain.org',
                     'hub.domain.org:5445', 'hub.domain.org.', 'HUB.domain.org', '192.168.4.1',
                     'a..domain.org', '-hub.domain.org', 'hub.domain.org/path', 'hub.internal', 'hub.home.arpa', 'hub.域名.org']:
            with self.subTest(host=host), self.assertRaises(f.Rejected):
                f.hostname(host)

    def test_private_addresses(self):
        for address in ['10.2.3.4', '172.20.1.1', '192.168.4.1', 'fd12::2']:
            self.assertEqual(address, f.local_address(address))

    def test_rejects_nonlocal_or_ambiguous_addresses(self):
        for address in ['127.0.0.1', '0.0.0.0', '1.1.1.1', '169.254.1.1', '224.0.0.1', 'fe80::1%wlan0', '::', '::1', 'example.org', '::ffff:192.168.1.2']:
            with self.subTest(address=address), self.assertRaises(f.Rejected):
                f.local_address(address)

    def test_stale_or_unassigned_address_rejected(self):
        with self.assertRaises(f.Rejected):
            f.resolution(self.host, ['192.168.4.2'], ['192.168.4.1'])
        with self.assertRaises(f.Rejected):
            f.resolution(self.host, [], ['192.168.4.1'])

    def test_exact_dns_template_no_public_update(self):
        text = f.render_dns(self.host, ['192.168.4.1', 'fd12::2'], ['192.168.4.1', 'fd12::2'])
        self.assertEqual(text, f'local=/{self.host}/\nhost-record={self.host},192.168.4.1,fd12::2\n')

    def test_offline_dns(self):
        def offline(*a, **kw):
            raise OSError('no DNS')
        self.assertEqual('dns-unavailable', f.resolve_preflight(self.host, ['192.168.4.1'], offline)['reason'])

    def test_stale_aaaa_rejected(self):
        resolver = lambda *a, **kw: [(None, None, None, None, ('192.168.4.1', 0)), (None, None, None, None, ('fd12::5', 0))]
        self.assertFalse(f.resolve_preflight(self.host, ['192.168.4.1'], resolver)['ready'])

    def test_local_resolution_without_internet(self):
        resolver = lambda *a, **kw: [(None, None, None, None, ('192.168.4.1', 0))]
        self.assertTrue(f.resolve_preflight(self.host, ['192.168.4.1'], resolver)['ready'])

    def test_public_roots_do_not_accept_synthetic_ca(self):
        with self.assertRaises(f.Rejected):
            f.validate_certificate(self.host, self.root / 'good.pem', self.root / 'good.key')

    def test_certificate_chain_san_and_key(self):
        f.validate_certificate(self.host, self.root / 'good.pem', self.root / 'good.key', _test_ca=self.root / 'ca.pem')

    def test_invalid_certificate_identity_and_purpose(self):
        for name in ['wrong', 'wildcard', 'client']:
            with self.subTest(name=name), self.assertRaises(f.Rejected):
                f.validate_certificate(self.host, self.root / (name + '.pem'), self.root / (name + '.key'), _test_ca=self.root / 'ca.pem')

    def test_mismatched_key_rejected(self):
        with self.assertRaises(f.Rejected):
            f.validate_certificate(self.host, self.root / 'good.pem', self.root / 'wrong.key', _test_ca=self.root / 'ca.pem')

    def test_malformed_certificate(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / 'bad.pem'
            bad.write_text('not a certificate')
            bad.chmod(0o600)
            with self.assertRaises(f.Rejected):
                f.validate_certificate(self.host, bad, self.root / 'good.key')

    def test_old_tls_peer_cannot_acknowledge_new_generation(self):
        from unittest.mock import MagicMock
        raw = MagicMock()
        connection = MagicMock()
        connection.getpeercert.return_value = b'old-leaf-der'
        connection.recv.side_effect = [b'HTTP/1.0 200 OK\r\n\r\n{"ready":true,"generation":"new"}', b'']
        context = MagicMock()
        context.wrap_socket.return_value.__enter__.return_value = connection
        expected = f.hashlib.sha256(b'new-leaf-der').hexdigest()
        with patch.object(f.socket, 'create_connection', return_value=raw):
            with self.assertRaisesRegex(f.Rejected, 'leaf does not match'):
                f.probe(self.host, 5445, 'new', expected, _test_context=context)
        connection.sendall.assert_not_called()

    def test_no_secret_in_error(self):
        with self.assertRaises(f.Rejected) as error:
            f.run(['/usr/bin/openssl', 'not-a-command-super-secret'])
        self.assertNotIn('super-secret', str(error.exception))

    def test_timed_out_backend_is_terminated_without_output_leakage(self):
        with self.assertRaisesRegex(f.Rejected, 'timed out') as error:
            f.run(['/usr/bin/python3', '-c', 'import time; print("synthetic-secret", flush=True); time.sleep(30)'], timeout=.05)
        self.assertNotIn('synthetic-secret', str(error.exception))

    def test_world_readable_private_file_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'key'
            path.write_text('secret')
            path.chmod(0o644)
            with self.assertRaises(f.Rejected):
                f.private_path(path)

    def test_symlink_and_writable_ancestor_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'link'
            path.symlink_to(self.root / 'good.key')
            with self.assertRaises(f.Rejected):
                f.private_path(path)
            path.unlink()
            path.write_text('secret')
            path.chmod(0o600)
            Path(tmp).chmod(0o770)
            with self.assertRaises(f.Rejected):
                f.private_path(path)

    def test_dry_run_does_not_activate(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            self.assertFalse(self.activate(folder)['changed'])
            self.assertEqual([], list(folder.iterdir()))

    def test_atomic_promotion_and_permissions(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            def acknowledge(host, port, generation, fingerprint):
                self.assertEqual(self.host, host)
                self.assertEqual(generation, f.read_active(folder)['generation'])
                self.assertTrue((folder / generation / 'bundle.pfx').is_file())
            result = self.activate(folder, dry_run=False, _probe=acknowledge)
            self.assertTrue(result['changed'])
            self.assertEqual(0o640, (folder / result['generation'] / 'bundle.pfx').stat().st_mode & 0o777)
            self.assertEqual(0o640, (folder / 'active.json').stat().st_mode & 0o777)

    def test_not_due_renewal_confirms_service_without_new_generation(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            old = self.activate(folder, dry_run=False, _probe=lambda *a: None)['generation']
            before = set(folder.iterdir())
            acknowledgements = []
            result = self.activate(folder, dry_run=False, _probe=lambda h, p, g, fingerprint: acknowledgements.append(g))
            self.assertFalse(result['changed'])
            self.assertEqual(old, result['generation'])
            self.assertEqual([old], acknowledgements)
            self.assertEqual(before, set(folder.iterdir()))

    def test_rollback_last_known_good(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(f.time, 'sleep'):
            folder = Path(tmp)
            old = self.activate(folder, dry_run=False, _probe=lambda *a: None)['generation']
            def reject_new(host, port, generation, fingerprint):
                if generation != old:
                    raise f.Rejected('fixture reload failure')
            with self.assertRaisesRegex(f.Rejected, 'previous manifest restored'):
                self.activate(folder, name="next", dry_run=False, _probe=reject_new)
            self.assertEqual(old, f.read_active(folder)['generation'])

    def test_uncertain_rollback_is_reported(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(f.time, 'sleep'):
            folder = Path(tmp)
            self.activate(folder, dry_run=False, _probe=lambda *a: None)
            with self.assertRaisesRegex(f.Rejected, 'recovery is unconfirmed'):
                self.activate(folder, name="next", dry_run=False, _probe=lambda *a: (_ for _ in ()).throw(OSError('offline')))

    def test_first_activation_failure_requests_disable(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(f.time, 'sleep'):
            folder = Path(tmp)
            with self.assertRaises(f.Rejected):
                self.activate(folder, name="next", dry_run=False, _probe=lambda *a: (_ for _ in ()).throw(OSError('offline')))
            self.assertIsNone(f.read_active(folder))
            self.assertEqual({"disabled": True}, json.loads((folder / "active.json").read_text()))

    def test_renewal_failure_preserves_active(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            old = self.activate(folder, dry_run=False, _probe=lambda *a: None)['generation']
            adapter = folder / 'adapter'
            adapter.write_text('#!/bin/sh\nexit 1\n')
            adapter.chmod(0o700)
            settings = self.settings(folder) | {'renewal_adapter': str(adapter)}
            self.assertEqual('dry-run-no-network', f.renew(settings)['reason'])
            with self.assertRaises(f.Rejected):
                f.renew(settings, apply=True)
            self.assertEqual(old, f.read_active(folder)['generation'])

    def test_resolution_ignores_unselected_global_interface(self):
        self.assertEqual({'192.168.4.1'}, f.resolution(self.host, ['192.168.4.1'], ['192.168.4.1', '2001:db8::1']))

    def test_group_writable_adapter_and_concurrent_renewal_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            adapter = folder / 'adapter'
            adapter.write_text('#!/bin/sh\nexit 0\n')
            adapter.chmod(0o770)
            settings = self.settings(folder) | {'renewal_adapter': str(adapter)}
            with self.assertRaises(f.Rejected):
                f.renew(settings, apply=True)
            adapter.chmod(0o700)
            lock = os.open(folder / '.renewal-lock', os.O_RDWR | os.O_CREAT, 0o600)
            try:
                f.fcntl.flock(lock, f.fcntl.LOCK_EX | f.fcntl.LOCK_NB)
                with self.assertRaises(BlockingIOError):
                    f.renew(settings, apply=True)
            finally:
                os.close(lock)

    def test_rollback_revalidates_archive(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            generation = self.activate(folder, dry_run=False, _probe=lambda *a: None)['generation']
            validate = f.validate_certificate
            with patch.object(f, 'validate_certificate', side_effect=lambda h, c, k, **kwargs: validate(h, c, k, _test_ca=self.root / 'ca.pem')):
                result = f.rollback(self.settings(folder), generation)
            self.assertFalse(result['changed'])
            self.assertEqual(generation, f.read_active(folder)['generation'])
            with self.assertRaises(f.Rejected):
                f.rollback(self.settings(folder), '../../etc')

    def test_path_traversal_manifest_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            f.write_active(folder, {'generation': '../../secret'})
            with self.assertRaises(f.Rejected):
                f.read_active(folder)

if __name__ == '__main__':
    unittest.main()
