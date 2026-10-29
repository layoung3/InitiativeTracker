import importlib.machinery
import importlib.util
from pathlib import Path
import unittest
import uuid

p = Path(__file__).resolve().parents[1] / 'initiativetracker-stage-recovery-hotspot'
loader = importlib.machinery.SourceFileLoader('recovery', str(p))
spec = importlib.util.spec_from_loader(loader.name, loader)
mod = importlib.util.module_from_spec(spec)
loader.exec_module(mod)

class Tests(unittest.TestCase):
    def test_profile_inactive(self):
        text = mod.make_profile('wlan0','InitiativeTracker-84eaee7c','a'*24,str(uuid.uuid4()))
        self.assertIn('autoconnect=false', text)
        self.assertIn('mode=ap', text)
        self.assertIn('method=shared', text)
        self.assertIn('address1=10.84.0.1/24', text)
        self.assertIn('key-mgmt=wpa-psk', text)
    def test_reject_bad_ssid(self):
        with self.assertRaises(ValueError):
            mod.make_profile('wlan0','abc\n[connection]','a'*24,str(uuid.uuid4()))
    def test_reject_bad_device(self):
        with self.assertRaises(ValueError):
            mod.make_profile('wlan0; echo bad','safe','a'*24,str(uuid.uuid4()))
    def test_reject_bad_secret(self):
        with self.assertRaises(ValueError):
            mod.make_profile('wlan0','safe','short',str(uuid.uuid4()))
    def test_reject_bad_uuid(self):
        with self.assertRaises(ValueError):
            mod.make_profile('wlan0','safe','a'*24,'nope')
if __name__ == '__main__':
    unittest.main()
