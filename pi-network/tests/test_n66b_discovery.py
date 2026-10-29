import importlib.machinery
import importlib.util
from pathlib import Path
import sys
import tempfile
import json
import unittest
from unittest.mock import patch

FILE = Path(__file__).resolve().parents[1] / 'initiativetracker-network-discovery'
loader = importlib.machinery.SourceFileLoader('discovery', str(FILE))
spec = importlib.util.spec_from_loader('discovery', loader)
d = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = d
loader.exec_module(d)

class DiscoveryTests(unittest.TestCase):
    def test_escaped_colons(self):
        self.assertEqual(d.split_nmcli(r'Cafe\:WiFi:82'), ['Cafe:WiFi', '82'])
    def test_backslash(self):
        self.assertEqual(d.split_nmcli(r'Cafe\\Guest:82'), ['Cafe\\Guest', '82'])
    def test_scan_duplicates(self):
        self.assertEqual(d.scan_cached('Home:30\nHome:85\n'), {'Home': 85})
    def test_hidden(self):
        self.assertEqual(d.scan_cached(':70\n--:32\n'), {})
    def test_invalid_strength(self):
        with self.assertRaises(ValueError): d.scan_cached('Home:101')
    def test_invalid_columns(self):
        with self.assertRaises(ValueError): d.scan_cached('Home')
    def test_wifi_only(self):
        rows = 'Home Profile:abc:wifi\nRecovery:def:wifi\nOffice:ghi:ethernet\nInitiativeTracker-Recovery:jkl:wifi'
        results = d.wifi_profiles(rows, lambda uid: {'abc':'Home', 'def':'Office'}.get(uid,''))
        self.assertEqual(results, {'Home Profile':'Home', 'Recovery':'Office'})
    def test_profile_colon(self):
        self.assertEqual(d.wifi_profiles(r'Home\:Up:abc:wifi', lambda _: 'SSID'), {'Home:Up':'SSID'})
    def test_no_journal(self):
        with tempfile.TemporaryDirectory() as temp:
            self.assertFalse(d.journal_pending(Path(temp)/'absent'))
    def test_pending_journal(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Path(temp)/'journal.json'; p.write_text(json.dumps({'state':'pending'}))
            self.assertTrue(d.journal_pending(p))
    def test_recovered_journal(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Path(temp)/'journal.json'; p.write_text(json.dumps({'state':'recovered'}))
            self.assertFalse(d.journal_pending(p))
    def test_malformed_journal_defer(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Path(temp)/'journal.json'; p.write_text('{bad')
            self.assertTrue(d.journal_pending(p))
    def test_symlink_journal_defer(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Path(temp)/'link'; p.symlink_to(Path(temp)/'target')
            self.assertTrue(d.journal_pending(p))
    def test_discover_live_fixture(self):
        values = {
            ('-g','GENERAL.CONNECTION','device','show','wlan0'):'netplan-wlan0-Logan\n',
            ('-t','-f','NAME,UUID,TYPE','connection','show'):'netplan-wlan0-Logan:uuid1:wifi\nInitiativeTracker-Recovery:uuid2:wifi\nlo:uuid3:loopback\n',
            ('-g','802-11-wireless.ssid','connection','show','uuid','uuid1'):'Logan\n',
            ('-t','-f','SSID,SIGNAL','device','wifi','list','ifname','wlan0','--rescan','no'):'Logan:75\nGuest:40\n',
        }
        with patch.object(d,'nmcli',side_effect=lambda *args: values[args]), patch.object(d,'journal_pending',return_value=False):
            active, profiles, cached, matching, pending, choice, reason = d.discover()
        self.assertEqual(active,'netplan-wlan0-Logan')
        self.assertEqual(matching,('netplan-wlan0-Logan',))
        self.assertEqual(choice,'HOLD')
    def test_transition_defers(self):
        values = {
            ('-g','GENERAL.CONNECTION','device','show','wlan0'):'--\n',
            ('-t','-f','NAME,UUID,TYPE','connection','show'):'InitiativeTracker-Recovery:uuid2:wifi\n',
            ('-t','-f','SSID,SIGNAL','device','wifi','list','ifname','wlan0','--rescan','no'):'',
        }
        with patch.object(d,'nmcli',side_effect=lambda *args: values[args]), patch.object(d,'journal_pending',return_value=True):
            result=d.discover()
        self.assertEqual(result[-2],'DEFER_TO_RECOVERY_CONTROLLER')

if __name__ == '__main__': unittest.main()
