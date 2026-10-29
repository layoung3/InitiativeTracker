import importlib.machinery
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).resolve().parents[1] / 'initiativetracker-network-verified-decision'
loader = importlib.machinery.SourceFileLoader('n66g_verified_decision', str(PATH))
spec = importlib.util.spec_from_loader(loader.name, loader)
mod = importlib.util.module_from_spec(spec)
loader.exec_module(mod)

BASE = {'decision': 'WAIT', 'reason': 'fresh_discovery_required'}


def case(scan=None, active='', pending=False, matching=(), base=BASE):
    return mod.evaluate(base=base, scan=scan, boot_id='abc', uptime=500,
                        matching=matching, active=active, pending=pending)


def record(result='success', completed=480, boot='abc'):
    return {'boot_id': boot, 'result': result, 'last_success_uptime': completed}


class VerifiedDecisionTests(unittest.TestCase):
    def test_missing(self):
        self.assertEqual(case()['scan_verification'], 'missing_scan_record')

    def test_failed(self):
        self.assertEqual(case(record('failed'))['decision'], 'WAIT')

    def test_attempting(self):
        self.assertEqual(case(record('attempting'))['scan_verification'], 'scan_in_progress_or_interrupted')

    def test_prior_boot(self):
        self.assertEqual(case(record(boot='old'))['scan_verification'], 'different_boot')

    def test_stale(self):
        self.assertEqual(case(record(completed=10))['scan_verification'], 'stale_scan_request')

    def test_future_time(self):
        self.assertEqual(case(record(completed=600))['scan_verification'], 'invalid_scan_timestamp')

    def test_success_with_match(self):
        val = case(record(), matching=['Home'])
        self.assertEqual(val['decision'], 'CANDIDATE_SAVED_WIFI')

    def test_success_without_match_remains_unknown(self):
        self.assertEqual(case(record())['decision'], 'UNKNOWN')

    def test_connected_home_holds(self):
        val = case(record(), active='Home', base={'decision': 'HOLD', 'reason': 'working_connection_active'})
        self.assertEqual(val['decision'], 'HOLD')

    def test_hotspot_holds(self):
        val = case(record(), active='InitiativeTracker-Recovery', base={'decision': 'HOLD', 'reason': 'hotspot_active'})
        self.assertEqual(val['decision'], 'HOLD')

    def test_pending_defers(self):
        val = case(record(), pending=True, base={'decision': 'DEFER_TO_RECOVERY_CONTROLLER', 'reason': 'transition_pending'})
        self.assertEqual(val['decision'], 'DEFER_TO_RECOVERY_CONTROLLER')

    def test_invalid_up(self):
        with self.assertRaises(ValueError):
            mod.evaluate(base=BASE, scan=None, boot_id='abc', uptime=float('nan'), matching=[], active='', pending=False)

    def test_startup_grace_not_overridden(self):
        value = case(record(), matching=['Home'], base={'decision': 'WAIT', 'reason': 'startup_grace_period'})
        self.assertEqual(value['reason'], 'startup_grace_period')

    def test_unknown_observation_not_overridden(self):
        value = case(record(), matching=['Home'], base={'decision': 'UNKNOWN', 'reason': 'missing_observation'})
        self.assertEqual(value['decision'], 'UNKNOWN')

    def test_retry_backoff_not_overridden(self):
        value = case(record(), matching=['Home'], base={'decision': 'WAIT', 'reason': 'retry_backoff'})
        self.assertEqual(value['reason'], 'retry_backoff')

    def test_unknown_scan_result(self):
        self.assertEqual(case(record('garbage'))['scan_verification'], 'unknown_scan_result')

    def test_negative_completed(self):
        self.assertEqual(case(record(completed=-2))['scan_verification'], 'invalid_scan_timestamp')

    def test_matching_deduped(self):
        self.assertEqual(case(record(), matching=['X', 'X'])['matching'], ['X'])


if __name__ == '__main__':
    unittest.main()
