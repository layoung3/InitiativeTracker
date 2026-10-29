import importlib.machinery
import importlib.util
import json
import tempfile
from pathlib import Path
import unittest
import io
from contextlib import redirect_stdout

FILE = Path(__file__).resolve().parents[1] / 'initiativetracker-network-observer'
loader = importlib.machinery.SourceFileLoader('n66d_observer', str(FILE))
spec = importlib.util.spec_from_loader(loader.name, loader)
m = importlib.util.module_from_spec(spec)
loader.exec_module(m)

class ObserverTests(unittest.TestCase):
    def make(self, old=None, **overrides):
        data = dict(boot='bootA', up=100, active='netplan-wlan0-Logan', matching=['netplan-wlan0-Logan'], pending=False, timestamp='2026-10-09T00:00:00Z')
        data.update(overrides)
        return m.update(old, **data)

    def test_connected_not_down(self): self.assertIsNone(self.make()['disconnected_since_uptime'])
    def test_down_starts_at_observation(self): self.assertEqual(self.make(active='')['disconnected_since_uptime'], 100)
    def test_down_duration_persists(self): self.assertEqual(self.make(self.make(active=''), active='', up=145)['disconnected_since_uptime'], 100)
    def test_reconnection_clears_downtime(self): self.assertIsNone(self.make(self.make(active=''), up=145)['disconnected_since_uptime'])
    def test_stale_uptime_resets(self): self.assertEqual(self.make(self.make(active='', up=200), active='', up=50)['disconnected_since_uptime'], 50)
    def test_cached_matching_sorted(self): self.assertEqual(self.make(matching=['b','a','b'])['last_cached_matching'], ['a','b'])
    def test_pending_preserved(self): self.assertTrue(self.make(pending=True)['last_transition_pending'])
    def test_failure_history_is_not_invented(self): self.assertEqual(self.make()['failures'], 0)
    def test_failure_history_persists(self):
        old = self.make(); old['failures']=2; old['last_failure_uptime']=90
        self.assertEqual(self.make(old, up=110)['failures'], 2)
    def test_boot_change_invalidates(self): self.assertIsNone(m.validate(self.make(), 'bootB'))
    def test_malformed_state_rejected(self):
        with self.assertRaises(ValueError): m.validate({'schema':1,'boot_id':'bootA'}, 'bootA')
    def test_atomic_write_and_read(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'observer.json'; m.atomic_save(path, self.make())
            self.assertEqual(m.read_state(path, 'bootA')['last_active'],'netplan-wlan0-Logan')
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)
    def test_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'observer.json'; target=Path(d)/'target'; target.write_text('{}')
            path.symlink_to(target)
            with self.assertRaises(ValueError): m.read_state(path, 'bootA')
    def test_output_does_not_claim_scan(self):
        buffer = io.StringIO()
        with redirect_stdout(buffer): m.output(self.make(), 100, 'status')
        self.assertIn('CACHE_IS_FRESH_SCAN=false', buffer.getvalue())
        self.assertIn('NETWORK_WRITES=DISABLED', buffer.getvalue())

if __name__ == '__main__': unittest.main()
