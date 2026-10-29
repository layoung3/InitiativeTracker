import importlib.machinery
import importlib.util
import json
import pathlib
import tempfile
import unittest

PATH = pathlib.Path(__file__).resolve().parents[1] / 'initiativetracker-network-scan'
loader = importlib.machinery.SourceFileLoader('n66f_scan', str(PATH))
spec = importlib.util.spec_from_loader(loader.name, loader)
scan = importlib.util.module_from_spec(spec)
loader.exec_module(scan)


class GateTests(unittest.TestCase):
    def gate(self, **kw):
        vals = dict(active='', pending=False, current_uptime=200, last_attempt=None, device_state='disconnected')
        vals.update(kw)
        return scan.assess(**vals)

    def test_safe(self): self.assertEqual(self.gate(), (True, 'safe_to_request_scan'))
    def test_live_connection(self): self.assertEqual(self.gate(active='home')[1], 'connected_profile_active')
    def test_hotspot(self): self.assertEqual(self.gate(active=scan.RECOVERY)[1], 'hotspot_active')
    def test_pending(self): self.assertEqual(self.gate(pending=True)[1], 'recovery_transition_pending')
    def test_uptime(self): self.assertEqual(self.gate(current_uptime=30)[1], 'startup_grace')
    def test_cooldown(self): self.assertEqual(self.gate(last_attempt=100)[1], 'cooldown')
    def test_after_cooldown(self): self.assertTrue(self.gate(current_uptime=500, last_attempt=100)[0])
    def test_other_state(self): self.assertEqual(self.gate(device_state='other')[1], 'device_not_disconnected')
    def test_unknown_active(self): self.assertTrue(self.gate(active='--')[0])
    def test_attempting_state_cools_down(self): self.assertEqual(self.gate(last_attempt=190)[1], 'cooldown')

    def test_journal_completed(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d) / 'transition.json'
            p.write_text(json.dumps({'state': 'recovered'}))
            self.assertFalse(scan.pending_journal(pathlib.Path(d)))
            p.write_text(json.dumps({'state': 'pending'}))
            self.assertTrue(scan.pending_journal(pathlib.Path(d)))
            p.write_text('{broken')
            self.assertTrue(scan.pending_journal(pathlib.Path(d)))

    def test_state_roundtrip(self):
        with tempfile.TemporaryDirectory() as d:
            path = pathlib.Path(d) / 'scan.json'
            sample = dict(schema=1, boot_id='id', last_attempt_uptime=123., last_success_uptime=123., result='success')
            scan.save_state(path, sample)
            self.assertEqual(scan.read_state(path, 'id'), sample)
            self.assertIsNone(scan.read_state(path, 'newboot'))
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_invalid_state(self):
        with tempfile.TemporaryDirectory() as d:
            p = pathlib.Path(d) / 'scan.json'
            p.write_text('{}')
            with self.assertRaises(ValueError): scan.read_state(p, 'boot')


if __name__ == '__main__':
    unittest.main()
