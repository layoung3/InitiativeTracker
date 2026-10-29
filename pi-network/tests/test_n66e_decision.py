import importlib.machinery
import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]

def load(name):
    loader = importlib.machinery.SourceFileLoader(name.replace('-', '_'), str(ROOT / name))
    spec = importlib.util.spec_from_loader(loader.name, loader)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = mod
    loader.exec_module(mod)
    return mod

m = load('initiativetracker-network-decision')
t = load('initiativetracker-network-timing')


def state(active='', down=100, now=200, pending=False, failures=0, failure_at=None):
    return {'last_active': active, 'last_observed_uptime': now, 'last_transition_pending': pending,
            'disconnected_since_uptime': down, 'failures': failures, 'last_failure_uptime': failure_at,
            'last_cache_observed_uptime': now, 'last_cached_matching': ['Home']}


def run(active='', matching=(), pending=False, recovery_present=True, uptime_seconds=230,
        state_obj=None, **kwargs):
    return m.compose(active=active, matching=matching, pending=pending,
                     recovery_present=recovery_present, uptime_seconds=uptime_seconds,
                     state=state_obj, timing=t, **kwargs)


class DecisionTests(unittest.TestCase):
    def test_hold_home(self):
        self.assertEqual(run(active='Home', state_obj=state('Home'))['decision'], 'HOLD')

    def test_hold_hotspot(self):
        self.assertEqual(run(active='InitiativeTracker-Recovery')['decision'], 'HOLD')

    def test_pending_priority(self):
        self.assertEqual(run(pending=True)['decision'], 'DEFER_TO_RECOVERY_CONTROLLER')

    def test_no_observation_unknown(self):
        self.assertEqual(run()['decision'], 'UNKNOWN')

    def test_stale_observation_unknown(self):
        self.assertEqual(run(state_obj=state(now=10))['reason'], 'stale_observation')

    def test_network_changed_unknown(self):
        self.assertEqual(run(state_obj=state('Other'))['decision'], 'UNKNOWN')

    def test_pending_change_unknown(self):
        self.assertEqual(run(state_obj=state(pending=True))['decision'], 'UNKNOWN')

    def test_prior_boot_unknown(self):
        self.assertEqual(run(state_obj=state(), state_boot_matches=False)['decision'], 'UNKNOWN')

    def test_future_observation_unknown(self):
        self.assertEqual(run(state_obj=state(now=500))['decision'], 'UNKNOWN')

    def test_recent_link_down_wait(self):
        self.assertEqual(run(state_obj=state(down=210))['reason'], 'brief_link_interruption')

    def test_offline_stale_cache_requests_refresh(self):
        result = run(state_obj=state(down=100))
        self.assertEqual(result['reason'], 'fresh_discovery_required')
        self.assertEqual(result['scan'], 'RECOMMEND_REFRESH_WHEN_SAFE')
        self.assertFalse(result['cache_fresh_scan'])

    def test_retry_backoff(self):
        self.assertEqual(run(state_obj=state(failures=1, failure_at=215))['reason'], 'retry_backoff')

    def test_retry_limit(self):
        self.assertEqual(run(state_obj=state(failures=3, failure_at=20))['reason'], 'retry_limit_reached_requires_operator_or_future_cycle')

    def test_session_hold(self):
        self.assertEqual(run(state_obj=state(), session_locked=True)['reason'], 'active_session_no_automatic_switch')

    def test_manual_override_hold(self):
        self.assertEqual(run(state_obj=state(), manual_override=True)['reason'], 'operator_override')

    def test_unknown_retry_history(self):
        s = state(); s.pop('failures')
        self.assertEqual(run(state_obj=s)['decision'], 'UNKNOWN')

    def test_invalid_down_age(self):
        self.assertEqual(run(state_obj=state(down=250))['reason'], 'invalid_disconnection_time')

    def test_boot_grace(self):
        self.assertEqual(run(uptime_seconds=60, state_obj=state(now=50, down=0))['reason'], 'startup_grace_period')

    def test_scan_cache_not_fresh(self):
        s = state(); s['last_cache_observed_uptime'] = 230
        self.assertEqual(run(matching=['Home'], state_obj=s)['decision'], 'WAIT')


if __name__ == '__main__':
    unittest.main()
