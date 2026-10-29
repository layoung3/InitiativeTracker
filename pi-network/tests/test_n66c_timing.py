import importlib.machinery
import importlib.util
from pathlib import Path
import unittest

P = Path(__file__).resolve().parents[1] / 'initiativetracker-network-timing'
l = importlib.machinery.SourceFileLoader('n66c_timing', str(P))
s = importlib.util.spec_from_loader(l.name, l)
m = importlib.util.module_from_spec(s)
l.exec_module(m)

BASE = dict(active='', recovery_present=True, transition_pending=False,
            matching=[], uptime_seconds=200, last_scan_age_seconds=5)

class TimingTests(unittest.TestCase):
    def verify(self, reason, **changes):
        self.assertEqual(m.evaluate(**(BASE | changes))[1], reason)

    def test_startup(self): self.verify('startup_grace_period', uptime_seconds=10)
    def test_boundary(self): self.verify('no_matching_saved_wifi', uptime_seconds=90)
    def test_pending(self): self.verify('transition_pending', transition_pending=True)
    def test_override(self): self.verify('operator_override', manual_override=True)
    def test_hotspot(self): self.verify('hotspot_active_no_automatic_disruption', active='InitiativeTracker-Recovery')
    def test_active(self): self.verify('working_connection_active', active='home')
    def test_session(self): self.verify('active_session_no_automatic_switch', session_locked=True)
    def test_flicker(self): self.verify('brief_link_interruption', link_down_age_seconds=8)
    def test_limit(self): self.verify('retry_limit_reached_requires_operator_or_future_cycle', retries=3)
    def test_backoff(self): self.verify('retry_backoff', last_attempt_age_seconds=50)
    def test_stale(self): self.verify('fresh_discovery_required', last_scan_age_seconds=180)
    def test_unknown(self): self.verify('fresh_discovery_required', last_scan_age_seconds=None)
    def test_match(self): self.verify('candidate=Home', matching=['Home'])
    def test_missing_hotspot(self): self.verify('missing_recovery_profile', recovery_present=False)
    def test_bad_retries(self):
        with self.assertRaises(ValueError): m.evaluate(**(BASE | dict(retries=-1)))
    def test_bad_bool(self):
        with self.assertRaises(ValueError): m.evaluate(**(BASE | dict(uptime_seconds=True)))
    def test_scan_recommend(self):
        self.assertEqual(m.evaluate(**(BASE | dict(last_scan_age_seconds=None)))[2], 'RECOMMEND_REFRESH_WHEN_SAFE')

if __name__ == '__main__': unittest.main()
