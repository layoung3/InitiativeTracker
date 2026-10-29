import importlib.machinery
import importlib.util
import pathlib
import stat
import unittest
from unittest.mock import patch, mock_open

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / 'initiativetracker-network-socketd'
loader = importlib.machinery.SourceFileLoader('socketd_n610c', str(SCRIPT))
spec = importlib.util.spec_from_loader(loader.name, loader)
mod = importlib.util.module_from_spec(spec)
loader.exec_module(mod)

class Tests(unittest.TestCase):
    def test_qr_action_allows_no_arguments_only(self):
        self.assertEqual(mod.parse_request(b'{"action":"hotspot-join-qr","uuid":null}'), ['hotspot-join-qr'])
        for bad in (b'{"action":"hotspot-join-qr","uuid":"secret"}', b'{"action":"hotspot-join-qr","uuid":null,"x":1}'):
            with self.assertRaises(ValueError): mod.parse_request(bad)
    def test_qr_escape(self):
        self.assertEqual(mod.qr_escape('semi;slash\\comma,colon:'), 'semi\\;slash\\\\comma\\,colon\\:')
    def test_rejects_insecure_secret_permissions(self):
        fake = type('S', (), {'st_uid':0,'st_mode':stat.S_IFREG | 0o644})()
        with patch.object(mod.os,'stat',return_value=fake), patch.object(mod.os.path,'isfile',return_value=True):
            with self.assertRaises(PermissionError): mod.private_hotspot_payload()
    def test_real_qr_uses_nm_profile_ssid_and_secret(self):
        fake = type('S', (), {'st_uid':0,'st_mode':stat.S_IFREG | 0o600})()
        out = type('P', (), {'stdout':'My;Hub\n'})()
        with patch.object(mod.os,'stat',return_value=fake), patch.object(mod.os.path,'isfile',return_value=True), patch('builtins.open',mock_open(read_data='mypassword123\n')), patch.object(mod.subprocess,'run',return_value=out):
            self.assertEqual(mod.private_hotspot_payload(),'WIFI:T:WPA;S:My\\;Hub;P:mypassword123;;')

if __name__ == '__main__': unittest.main()
