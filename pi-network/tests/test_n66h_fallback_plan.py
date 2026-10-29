import importlib.machinery
import importlib.util
from pathlib import Path
import sys
import unittest

P = Path(__file__).resolve().parents[1] / 'initiativetracker-network-fallback-plan'
loader = importlib.machinery.SourceFileLoader('n66h', str(P))
spec = importlib.util.spec_from_loader(loader.name, loader)
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
loader.exec_module(m)

DEFAULT = dict(active='', pending=False, recovery_present=True, uptime=500, downtime=200,
               scan_verification='fresh_scan_request', matches=[], failures=0,
               last_failure_age=None, scan_confirmations=[], evidence_live=False)
class FallbackTests(unittest.TestCase):
    def run_case(self, expected, reason=None, **changes):
        inp = dict(DEFAULT, **changes)
        res = m.assess(**inp)
        self.assertEqual(res['decision'], expected)
        if reason: self.assertEqual(res['reason'], reason)
        self.assertFalse(res['execution_allowed'])
        self.assertEqual(res['network_action'], 'NONE')
    def test_pending(self): self.run_case('DEFER', pending=True)
    def test_connected(self): self.run_case('HOLD', active='netplan-wlan0-Logan')
    def test_hotspot(self): self.run_case('HOLD', active='InitiativeTracker-Recovery')
    def test_session(self): self.run_case('HOLD', session_locked=True)
    def test_manual(self): self.run_case('HOLD', manual_override=True)
    def test_no_recovery(self): self.run_case('BLOCKED', recovery_present=False)
    def test_boot(self): self.run_case('WAIT', uptime=40)
    def test_missing_downtime(self): self.run_case('UNKNOWN', downtime=None)
    def test_brief(self): self.run_case('WAIT', downtime=12)
    def test_unknown_retries(self): self.run_case('UNKNOWN', failures=None)
    def test_unknown_failure_time(self): self.run_case('UNKNOWN', failures=1)
    def test_cooldown(self): self.run_case('WAIT', failures=1, last_failure_age=10)
    def test_saved_candidate(self): self.run_case('CANDIDATE_SAVED_WIFI', matches=['netplan-wlan0-Logan'])
    def test_saved_stale(self): self.run_case('UNKNOWN', matches=['Home'], scan_verification='stale_scan_request')
    def test_max_retry(self): self.run_case('WAIT', matches=['Home'], failures=3, last_failure_age=150)
    def test_missing_scan(self): self.run_case('UNKNOWN', scan_verification='missing_scan_record')
    def test_one_scan(self): self.run_case('UNKNOWN', scan_confirmations=[490])
    def test_two_too_close(self): self.run_case('UNKNOWN', scan_confirmations=[310, 400])
    def test_two_scans_untrusted(self): self.run_case('UNKNOWN', scan_confirmations=[250, 490])
    def test_two_trusted_simulation(self): self.run_case('HOTSPOT_ELIGIBLE', scan_confirmations=[250,490], evidence_live=True)
    def test_stale_evidence(self): self.run_case('UNKNOWN', uptime=900, scan_confirmations=[250,490], evidence_live=True)
    def test_invalid_nan(self):
        with self.assertRaises(ValueError): m.assess(**dict(DEFAULT, uptime=float('nan')))
    def test_invalid_bool(self):
        with self.assertRaises(ValueError): m.assess(**dict(DEFAULT, failures=True))
    def test_invalid_scan(self):
        with self.assertRaises(ValueError): m.assess(**dict(DEFAULT, scan_verification='fake'))

if __name__ == '__main__': unittest.main()
