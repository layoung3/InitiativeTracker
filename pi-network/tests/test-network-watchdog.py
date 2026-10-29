#!/usr/bin/env python3
"""Tests the watchdog's dry-run decision engine without NetworkManager."""
import importlib.machinery
import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

PATH = Path(__file__).resolve().parents[1] / 'initiativetracker-network-watchdog'
loader = importlib.machinery.SourceFileLoader('watchdog', str(PATH))
spec = importlib.util.spec_from_loader(loader.name, loader)
mod = importlib.util.module_from_spec(spec)
loader.exec_module(mod)
NOW = datetime(2026, 10, 9, tzinfo=timezone.utc)

class Tests(unittest.TestCase):
    def test_missing_profile_blocks(self):
        self.assertEqual(mod.decision({'active': 'home', 'recovery_present': False}, now=NOW), 'BLOCKED_RECOVERY_PROFILE_MISSING')
    def test_idle_existing_is_preserved(self):
        self.assertEqual(mod.decision({'active': 'home', 'recovery_present': True}, now=NOW), 'KEEP_EXISTING')
    def test_idle_hotspot_is_preserved(self):
        self.assertEqual(mod.decision({'active': mod.RECOVERY, 'recovery_present': True}, now=NOW), 'KEEP_RECOVERY')
    def test_no_active_network_review(self):
        self.assertEqual(mod.decision({'active': '--', 'recovery_present': True}, now=NOW), 'NO_ACTIVE_WIFI_REVIEW')
    def test_pending_transition(self):
        t={'previous_profile':'home','target_profile':'other','expires_at':NOW+timedelta(seconds=60)}
        self.assertEqual(mod.decision({'active':'home','recovery_present':True},t,NOW),'TRANSITION_PENDING')
    def test_target_needs_confirmation(self):
        t={'previous_profile':'home','target_profile':'other','expires_at':NOW+timedelta(seconds=60)}
        self.assertEqual(mod.decision({'active':'other','recovery_present':True},t,NOW),'TARGET_SEEN_AWAIT_EXPLICIT_VALIDATION')
    def test_timeout_plan_not_execution(self):
        t={'previous_profile':'home','target_profile':'other','expires_at':NOW-timedelta(seconds=1)}
        self.assertEqual(mod.decision({'active':'other','recovery_present':True},t,NOW),'WOULD_RESTORE_PREVIOUS_THEN_HOTSPOT')
    def test_expired_previous_recovered(self):
        t={'previous_profile':'home','target_profile':'other','expires_at':NOW-timedelta(seconds=1)}
        self.assertEqual(mod.decision({'active':'home','recovery_present':True},t,NOW),'RECOVERED_PREVIOUS')
    def test_invalid_scenario_fails(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'scenario.json'; p.write_text(json.dumps({'previous_profile':'home','target_profile':'$(bad)','expires_at_utc':'2026-10-09T00:00:00Z'}))
            with self.assertRaises(ValueError): mod.parse_transition(p)

if __name__=='__main__': unittest.main(verbosity=2)
