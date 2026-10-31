"""Disposable root-boundary tests. Fake NM; real private files, atomic replacement and journals."""
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch
import uuid

SCRIPT = Path(__file__).resolve().parents[1] / 'initiativetracker-satellite-network'
loader = importlib.machinery.SourceFileLoader('satellite_network', str(SCRIPT))
spec = importlib.util.spec_from_loader(loader.name, loader)
m = importlib.util.module_from_spec(spec)
loader.exec_module(m)


class SatelliteNetworkTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.profiles = self.root / 'profiles'
        self.profiles.mkdir()
        self.active = str(uuid.uuid4())
        self.calls = []
        self.failed_profiles = set()
        self.patches = [patch.object(m, 'ROOT', self.root), patch.object(m, 'PROFILES', self.profiles), patch.object(m, 'nm', self.nm)]
        for p in self.patches:
            p.start()
        self.generation = str(uuid.uuid4())
        self.identity = str(uuid.uuid4())
        self.profile = {'ssid': 'Synthetic; WiFi\\名', 'password': 'synthetic-secret\\ '}  # No real credentials.
        self.run_action('bootstrap', generation=self.generation, recovery=self.profile)

    def tearDown(self):
        for p in self.patches:
            p.stop()
        self.tmp.cleanup()

    def nm(self, *args):
        self.calls.append(args)
        if args[:3] == ('-g', 'GENERAL.CON-UUID', 'device'):
            return self.active
        if 'up' in args:
            target = args[args.index('uuid') + 1]
            if target in self.failed_profiles:
                self.active = ''
                raise RuntimeError('synthetic failure')
            self.active = target
        return ''

    def run_action(self, action, **kwargs):
        return m.execute({'action': action, **kwargs})

    def prepare(self, **kwargs):
        request = dict(generation=self.generation, id=self.identity, revision=1, expires=time.time()+90, target=self.profile)
        request.update(kwargs)
        self.run_action('prepare', **request)
        return request

    def commit(self, **kwargs):
        now = time.time()
        request = dict(id=self.identity, switchAt=now+1, deadline=now+100)
        request.update(kwargs)
        self.run_action('commit', **request)
        return request

    def test_profile_staging_uses_private_escaped_keyfile_and_no_password_argv(self):
        content = next(self.profiles.glob('*.nmconnection')).read_text()
        self.assertIn('autoconnect=false', content)
        self.assertIn('ssid=83;121;', content)
        self.assertIn('psk=synthetic-secret\\\\\\s', content)
        self.assertEqual(next(self.profiles.glob('*')).stat().st_mode & 0o777, 0o600)
        self.assertNotIn(self.profile['password'], str(self.calls))

    def test_no_activation_before_commit_and_duplicate_prepare(self):
        request = self.prepare()
        self.run_action('prepare', **request)
        self.assertFalse(any('up' in c for c in self.calls))
        with self.assertRaises(ValueError):
            self.run_action('prepare', **{**request, 'target': {'ssid': 'changed', 'password': 'synthetic-other'}})

    def test_commit_duplicate_does_not_extend_deadline_and_replay_rejected(self):
        self.prepare()
        request = self.commit()
        self.run_action('commit', **request)
        with self.assertRaises(ValueError):
            self.run_action('commit', **{**request, 'deadline': request['deadline']+1})
        self.assertEqual(m.load()['transition']['deadline'], request['deadline'])
        with self.assertRaises(ValueError):
            self.run_action('abandon', id=self.identity)

    def test_restart_and_independent_deadline_rollback(self):
        previous = self.active
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        m.tick()
        self.assertNotEqual(self.active, previous)
        state = m.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertEqual(self.active, previous)
        self.assertEqual(m.load()['transition']['phase'], 'recovered')
        count = len(self.calls)
        m.tick()
        self.assertEqual(len(self.calls), count)

    def test_reboot_and_clock_rollback_force_recovery(self):
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        m.tick()
        state = m.load()
        state['transition']['boot'] = 'previous-boot'
        m.save(state)
        m.tick()
        self.assertEqual(m.load()['transition']['phase'], 'recovered')

    def test_target_and_previous_failures_recover_to_paired_hotspot(self):
        previous = self.active
        self.prepare()
        self.failed_profiles.update([previous, m.load()['transition']['target']])
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        m.tick()
        self.assertEqual(self.active, m.load()['recovery'])
        self.assertEqual(m.load()['transition']['phase'], 'recovered')

    def test_failed_rollback_is_retried_without_losing_intent(self):
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        state = m.load()
        self.failed_profiles.update([state['transition']['previous'], state['recovery']])
        state['transition']['mono_deadline'] = time.monotonic()-1
        self.active = state['transition']['target']
        m.save(state)
        with self.assertRaises(RuntimeError):
            m.tick()
        self.assertEqual(m.load()['transition']['phase'], 'recovering')
        self.failed_profiles.clear()
        m.tick()
        self.assertEqual(m.load()['transition']['phase'], 'recovered')

    def test_rebinding_requires_separate_operation_and_invalidates_old_revision(self):
        with self.assertRaises(ValueError):
            self.run_action('bootstrap', generation=str(uuid.uuid4()), recovery=self.profile)
        self.prepare()
        self.run_action('abandon', id=self.identity)
        new = str(uuid.uuid4())
        self.run_action('rebind', generation=new, recovery=self.profile)
        self.assertEqual(m.load()['generation'], new)
        self.assertIsNone(m.load()['transition'])
        with self.assertRaises(ValueError):
            self.prepare()

    def test_bounded_unique_provisioning_lease_does_not_extend_and_preserves_binding(self):
        previous = self.active
        self.run_action('open-provisioning')
        initial = m.load()['provisioning']
        self.run_action('open-provisioning')
        self.assertEqual(initial, m.load()['provisioning'])
        self.assertEqual(m.load()['generation'], self.generation)
        access = self.run_action('provisioning-info')['access']
        self.assertGreaterEqual(len(access['password']), 24)
        self.assertNotIn(access['password'], str(self.calls))
        state = m.load()
        state['provisioning']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertEqual(self.active, previous)
        self.assertIsNone(m.load()['provisioning'])
        self.assertEqual(m.load()['generation'], self.generation)

    def test_candidate_network_timeout_preserves_old_hub(self):
        previous = self.active
        self.run_action('open-provisioning')
        self.run_action('candidate-network', profile=self.profile)
        state = m.load()
        state['provisioning']['boot'] = 'previous-boot'
        m.save(state)
        m.tick()
        self.assertEqual(self.active, previous)
        self.assertEqual(m.load()['generation'], self.generation)

    def test_unknown_actions_profiles_generations_and_malformed_input_rejected(self):
        for request in [{'action': 'shell', 'command': 'id'}, {'action': 'activate', 'profile': str(uuid.uuid4())},
                        {'action': 'commit', 'id': self.identity, 'switchAt': time.time(), 'deadline': time.time()+100}]:
            with self.assertRaises(ValueError):
                m.execute(request)
        with self.assertRaises(ValueError):
            self.prepare(generation=str(uuid.uuid4()))
        with self.assertRaises(ValueError):
            self.prepare(target={'ssid': 'bad\n[wifi]', 'password': 'synthetic-pass'})

    def test_truncated_journal_and_symlink_fail_closed(self):
        (self.root/'state.json').write_text('{')
        with self.assertRaises(ValueError):
            m.load()
        (self.root/'state.json').unlink()
        (self.root/'state.json').symlink_to(self.root/'outside')
        with self.assertRaises(ValueError):
            m.load()

    def test_atomic_write_failure_preserves_old_journal(self):
        before = (self.root/'state.json').read_bytes()
        with patch.object(m.os, 'replace', side_effect=OSError('interrupted')):
            with self.assertRaises(OSError):
                self.prepare()
        self.assertEqual(before, (self.root/'state.json').read_bytes())


if __name__ == '__main__':
    unittest.main()
