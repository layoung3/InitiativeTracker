import importlib.machinery
import io
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
prov = importlib.machinery.SourceFileLoader('provision', str(ROOT / 'initiativetracker-wifi-provision')).load_module()
broker = importlib.machinery.SourceFileLoader('broker_n610b', str(ROOT / 'initiativetracker-network-socketd')).load_module()

class ProvisionTests(unittest.TestCase):
    def test_valid_wpa(self):
        self.assertEqual(prov.validate({'ssid':'Example', 'password':'passphrase123'}), ('Example', 'passphrase123'))
    def test_password_not_in_profile_name(self):
        rendered = io.StringIO()
        prov.create_keyfile('Example','secret1234','12345678-1234-1234-1234-123456789abc').write(rendered)
        self.assertIn('psk = secret1234', rendered.getvalue())
        self.assertNotIn('secret1234', rendered.getvalue().split('[wifi-security]')[0])
    def test_reject_invalid(self):
        for value in ({'ssid':'x', 'password':'short'}, {'ssid':'', 'password':'password00'}, {'ssid':'hey\nX', 'password':'password00'}, {'ssid':'x', 'password':'password00', 'extra':1}):
            with self.subTest(value=value):
                with self.assertRaises(ValueError): prov.validate(value)
    def test_socket_action(self):
        import json
        request=json.dumps({'action':'provision-wifi','uuid':{'ssid':'Example','password':'password00'}}).encode()
        cmd=broker.parse_request(request)
        self.assertTrue(cmd[0].endswith('initiativetracker-wifi-provision'))
    def test_socket_rejects_bad_payload(self):
        import json
        with self.assertRaises(ValueError):
            broker.parse_request(json.dumps({'action':'provision-wifi','uuid':'secret'}).encode())

if __name__=='__main__': unittest.main()
