import importlib.machinery
import importlib.util
import io
import pathlib
import tempfile
import unittest
from unittest.mock import patch

ROOT=pathlib.Path(__file__).resolve().parents[1]

def module(name, file):
    loader=importlib.machinery.SourceFileLoader(name,str(ROOT/file))
    spec=importlib.util.spec_from_loader(name,loader)
    m=importlib.util.module_from_spec(spec)
    loader.exec_module(m)
    return m

pw=module('password', 'initiativetracker-hotspot-password')
broker=module('socketd','initiativetracker-network-socketd')

class PasswordTests(unittest.TestCase):
    def test_valid(self):
        self.assertTrue(pw.valid_password('Memorable pass 2026'))
        self.assertFalse(pw.valid_password('short'))
        self.assertFalse(pw.valid_password('has\nnewline'))
        self.assertFalse(pw.valid_password('éééééééé'))

    def test_restricted_protocol(self):
        self.assertEqual(broker.parse_request(b'{"action":"rotate-hotspot-password","uuid":{"password":"TestPassword123"}}')[0],
                         '/usr/local/libexec/initiativetracker/initiativetracker-hotspot-password')
        with self.assertRaises(ValueError):
            broker.parse_request(b'{"action":"rotate-hotspot-password","uuid":{"password":"12345678","command":"rm"}}')

    def test_refuses_active_hotspot(self):
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)
            profile=path/'profile.nmconnection'
            secret=path/'password'
            profile.write_text('[wifi-security]\npsk=OldPassword123\n')
            secret.write_text('OldPassword123\n')
            config=pw.configparser.ConfigParser(interpolation=None)
            config.read(profile)
            with self.assertRaisesRegex(RuntimeError,'disable hotspot'):
                pw.rotate('NewPassword123',is_active=pw.RECOVERY,profile=profile,conf=config,secret=secret)
            self.assertEqual(profile.read_text(),'[wifi-security]\npsk=OldPassword123\n')

    def test_rotate_success_and_rollback(self):
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)
            profile=path/'profile.nmconnection'
            secret=path/'password'
            original=b'[wifi-security]\npsk=OldPassword123\n'
            profile.write_bytes(original)
            secret.write_bytes(b'OldPassword123\n')
            def conf():
                c=pw.configparser.ConfigParser(interpolation=None)
                c.read(profile)
                return c
            with patch.object(pw,'reload_nm',return_value=None):
                pw.rotate('NewPassword123',is_active='Home',profile=profile,conf=conf(),secret=secret)
            self.assertIn('NewPassword123',profile.read_text())
            self.assertEqual(secret.read_text(),'NewPassword123\n')
            with patch.object(pw,'reload_nm',side_effect=[RuntimeError('reload failed'),None]):
                with self.assertRaises(RuntimeError):
                    pw.rotate('AnotherPass123',is_active='Home',profile=profile,conf=conf(),secret=secret)
            self.assertEqual(secret.read_text(),'NewPassword123\n')
            self.assertIn('NewPassword123',profile.read_text())

if __name__=='__main__': unittest.main()
