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
        if args == ('-g', 'UUID', 'connection', 'show'):
            profiles = [p.name.removeprefix('IT-N8-').removesuffix('.nmconnection') for p in self.profiles.glob('*.nmconnection')]
            return '\n'.join(profiles)
        if args[:2] == ('connection', 'delete'):
            profile = args[3]
            path = self.profiles / ('IT-N8-' + profile + '.nmconnection')
            if not path.exists():
                raise RuntimeError('synthetic missing profile')
            path.unlink()
            if self.active == profile:
                self.active = '--'
        if args[:3] == ('-g', 'GENERAL.CON-UUID', 'device'):
            return self.active
        if 'down' in args:
            self.active = '--'
        if 'up' in args:
            target = args[args.index('uuid') + 1]
            if target in self.failed_profiles:
                self.active = ''
                raise RuntimeError('synthetic failure')
            self.active = target
        return ''

    def run_action(self, action, **kwargs):
        if action == 'rebind':
            lease = m.load().get('provisioning')
            kwargs.setdefault('lease', lease['id'] if lease else None)
            kwargs.setdefault('intent', str(uuid.uuid4()))
            kwargs.setdefault('expires', time.time()+300)
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

    def test_later_adopted_profile_is_not_deleted_by_older_cleanup(self):
        state = m.load()
        recovery = state['recovery']
        m.defer_cleanup(state, [recovery])
        m.save(state)
        m.cleanup_one(m.load())
        self.assertIn(recovery, m.load()['owned'])
        self.assertIsNone(m.load()['cleanup'])
        self.assertTrue((self.profiles / ('IT-N8-' + recovery + '.nmconnection')).exists())

    def test_destination_security_is_preserved_instead_of_downgrading_wpa3(self):
        profile = str(uuid.uuid4())
        m.stage_profile(profile, {**self.profile, 'keyManagement': 'sae'})
        self.assertIn('key-mgmt=sae', (self.profiles/('IT-N8-'+profile+'.nmconnection')).read_text())
        with self.assertRaises(ValueError):
            m.stage_profile(str(uuid.uuid4()), {**self.profile, 'keyManagement': 'wpa-eap'})

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
        self.assertFalse(any('up' in command or 'down' in command for command in self.calls[count:]))

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

    def test_disconnected_adapter_recovery_and_failed_identity_trial_restore_disconnected(self):
        self.active = '--'
        self.run_action('recover-profile')
        state = m.load()
        self.assertIsNone(state['transition']['previous'])
        self.assertFalse(state['transition']['switched'])
        m.tick()
        self.assertEqual(self.active, state['recovery'])
        state = m.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertIsNone(m.active_optional())
        self.assertEqual(m.load()['transition']['phase'], 'recovered')

    def test_unverified_automatic_hotspot_trial_returns_to_previous_profile(self):
        previous = self.active
        self.run_action('recover-profile')
        m.tick()
        self.assertEqual(self.active, m.load()['recovery'])
        state = m.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertEqual(self.active, previous)
        self.assertEqual(m.load()['transition']['phase'], 'recovered')

    def test_prepare_cannot_overlap_provisioning_and_legacy_overlap_cannot_interrupt_commit(self):
        self.run_action('open-provisioning')
        with self.assertRaises(ValueError):
            self.prepare()
        self.run_action('close-provisioning')
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        state = m.load()
        target = state['transition']['target']
        state['provisioning'] = {'previous': str(uuid.uuid4()), 'profile': str(uuid.uuid4()),
                                 'created': time.time()-1000, 'deadline': time.time()-1,
                                 'boot': m.boot(), 'mono_deadline': time.monotonic()-1}
        m.save(state)
        m.tick()
        self.assertEqual(self.active, target)
        self.assertIsNone(m.load()['provisioning'])
        self.assertIsNotNone(m.load()['restoration'])

    def test_successful_target_is_reactivated_on_boot_and_requires_new_identity_finalization(self):
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        m.tick()
        self.run_action('finalize', id=self.identity)
        target = self.active
        self.active = '--'
        with patch.object(m, 'boot', return_value='next-device-boot'):
            m.tick()
        state = m.load()
        self.assertEqual(self.active, target)
        self.assertTrue(state['transition']['automatic'])
        self.assertEqual(state['transition']['kind'], 'boot')
        self.assertEqual(state['transition']['phase'], 'committed')

    def test_same_boot_wifi_drop_retries_last_finalized_profile_before_private_hotspot(self):
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        m.tick()
        self.run_action('finalize', id=self.identity)
        target = self.active
        self.active = '--'
        self.run_action('recover-profile')
        self.assertEqual(m.load()['transition']['kind'], 'preferred')
        m.tick()
        self.assertEqual(self.active, target)
        state = m.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertIsNone(m.active_optional())
        self.run_action('recover-profile')
        self.assertEqual(m.load()['transition']['kind'], 'hotspot')
        m.tick()
        self.assertEqual(self.active, m.load()['recovery'])

    def test_failed_ap_restoration_retains_durable_retry_intent_across_restart(self):
        previous = self.active
        self.run_action('open-provisioning')
        with patch.object(m, 'up', side_effect=RuntimeError('synthetic restore failure')):
            self.run_action('close-provisioning')
        state = m.load()
        self.assertIsNone(state['provisioning'])
        self.assertEqual(state['restoration']['profile'], previous)
        self.assertIsNone(m.active_optional())
        state['restoration']['next'] = time.time()-1
        m.save(state)
        m.tick()
        self.assertEqual(self.active, previous)
        self.assertIsNone(m.load()['restoration'])

    def test_forget_does_not_remove_the_provisioning_ap_deadline(self):
        self.run_action('open-provisioning')
        lease = m.load()['provisioning']
        self.run_action('reset')
        state = m.load()
        self.assertIsNone(state['generation'])
        self.assertEqual(state['provisioning']['deadline'], lease['deadline'])
        state['provisioning']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        m.tick()
        self.assertIsNone(m.load()['provisioning'])

    def test_failed_restore_closes_the_ap_instead_of_extending_lease(self):
        previous = self.active
        self.run_action('open-provisioning')
        self.failed_profiles.add(previous)
        # Model NM rejecting restoration while leaving the AP active.
        def failing_up(profile):
            raise RuntimeError('synthetic restore failure')
        state = m.load()
        state['provisioning']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        with patch.object(m, 'up', failing_up):
            m.tick()
        self.assertIsNone(m.load()['provisioning'])
        self.assertTrue(any('down' in command for command in self.calls))

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


    def rebind_request(self, **values):
        request = {'action': 'rebind', 'generation': str(uuid.uuid4()), 'intent': str(uuid.uuid4()),
                   'expires': time.time()+60, 'recovery': self.profile, 'lease': None}
        request.update(values)
        return request

    def test_rebind_rejects_expired_authorization_before_and_after_blocking_nm(self):
        now = time.time()
        for boundary in ('before', 'stage', 'active'):
            with self.subTest(boundary=boundary), patch.object(m.time, 'time', return_value=now) as clock:
                request = self.rebind_request(expires=now+10)
                original_stage, original_nm = m.stage_profile, self.nm
                def stage(*args):
                    original_stage(*args)
                    if boundary == 'stage':
                        clock.return_value = now+11
                def nm(*args):
                    result = original_nm(*args)
                    if boundary == 'active' and args[:3] == ('-g', 'GENERAL.CON-UUID', 'device'):
                        clock.return_value = now+11
                    return result
                if boundary == 'before':
                    clock.return_value = now+11
                with patch.object(m, 'stage_profile', stage), patch.object(m, 'nm', nm), self.assertRaises(ValueError):
                    m.execute(request)
                self.assertEqual(m.load()['generation'], self.generation)

    def test_committed_rebind_retry_after_expiry_is_read_only_and_conflicting_retry_rejected(self):
        request = self.rebind_request()
        previous = self.active
        reply = m.execute(request)
        self.assertEqual(reply['binding']['intent'], request['intent'])
        snapshot = (self.root/'state.json').read_bytes()
        self.active = str(uuid.uuid4())
        with patch.object(m.time, 'time', return_value=request['expires']+1):
            self.assertEqual(m.execute(request), reply)
            with self.assertRaises(ValueError):
                m.execute({**request, 'recovery': {**self.profile, 'password': 'different-synthetic'}})
        self.assertEqual((self.root/'state.json').read_bytes(), snapshot)
        self.assertEqual(m.load()['preferred'], previous)

    def test_rebind_crash_before_commit_preserves_old_binding_and_profiles_then_retries(self):
        request = self.rebind_request()
        old_recovery = m.load()['recovery']
        original_save = m.save
        def fail_commit(state):
            if state.get('binding_change', {}).get('phase') == 'committed':
                raise OSError('synthetic loss before commit write')
            original_save(state)
        with patch.object(m, 'save', fail_commit), self.assertRaises(OSError):
            m.execute(request)
        self.assertEqual(m.load()['generation'], self.generation)
        self.assertTrue((self.profiles/('IT-N8-'+old_recovery+'.nmconnection')).exists())
        self.assertEqual(m.load()['binding_change']['phase'], 'prepared')
        self.assertTrue(m.execute(request)['ok'])
        self.assertEqual(m.load()['generation'], request['generation'])

    def test_rebind_lost_reply_after_durable_commit_is_recoverable_without_live_authorization(self):
        request = self.rebind_request()
        original_save = m.save
        def lose_reply(state):
            original_save(state)
            if state.get('binding_change', {}).get('phase') == 'committed':
                raise OSError('synthetic lost response after fsync')
        with patch.object(m, 'save', lose_reply), self.assertRaises(OSError):
            m.execute(request)
        self.assertEqual(m.load()['generation'], request['generation'])
        with patch.object(m.time, 'time', return_value=request['expires']+1):
            self.assertTrue(m.execute(request)['ok'])

    def test_cleanup_restarts_after_nm_deleted_profile_before_journal_progress(self):
        old_recovery = m.load()['recovery']
        m.execute(self.rebind_request())
        with patch.object(m, 'save', side_effect=OSError('synthetic interruption')), self.assertRaises(OSError):
            m.tick()
        self.assertFalse((self.profiles/('IT-N8-'+old_recovery+'.nmconnection')).exists())
        m.tick()
        self.assertIsNone(m.load()['cleanup'])
        self.assertNotIn(old_recovery, m.load()['owned'])

    def test_aborted_prepared_binding_cannot_be_resurrected_or_overlap_other_radio_work(self):
        request = self.rebind_request()
        with patch.object(m, 'stage_profile', side_effect=RuntimeError('synthetic load failure')), self.assertRaises(RuntimeError):
            m.execute(request)
        with self.assertRaises(ValueError):
            self.prepare()
        self.assertIsNone(self.run_action('abort-rebind', intent=request['intent'])['binding'])
        with self.assertRaises(ValueError):
            m.execute(request)
        self.assertEqual(m.load()['generation'], self.generation)

    def test_expired_owner_reset_does_not_need_old_hub_and_cleanup_is_restartable(self):
        self.prepare()
        self.commit(switchAt=time.time()-1, deadline=time.time()+99)
        with self.assertRaises(ValueError):
            self.run_action('reset')
        state = m.load()
        state['transition']['mono_deadline'] = time.monotonic()-1
        m.save(state)
        self.failed_profiles.update([state['transition']['previous'], state['recovery']])
        self.assertTrue(self.run_action('reset')['ok'])
        self.assertIsNone(m.load()['generation'])
        self.assertTrue(self.run_action('reset')['ok'])
        for _ in range(5):
            m.tick()
        self.assertIsNone(m.load()['cleanup'])
        self.assertFalse(list(self.profiles.glob('*.nmconnection')))

    def test_successful_switches_reclaim_old_managed_profiles_without_losing_boot_target(self):
        for revision in range(1, 6):
            self.identity = str(uuid.uuid4())
            self.prepare(revision=revision)
            self.commit(switchAt=time.time()-1, deadline=time.time()+99)
            m.tick()
            target = self.active
            self.run_action('finalize', id=self.identity)
            for _ in range(5):
                m.tick()
            self.assertEqual(self.active, target)
            self.assertEqual(m.load()['preferred'], target)
            self.assertLessEqual(len(m.load()['owned']), 2)


if __name__ == '__main__':
    unittest.main()
