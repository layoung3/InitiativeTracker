import importlib.machinery
import importlib.util
import json
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch
import uuid

SCRIPT = Path(__file__).resolve().parents[2] / 'pi-network/initiativetracker-device-coordination'
loader = importlib.machinery.SourceFileLoader('hub_coordination', str(SCRIPT))
spec = importlib.util.spec_from_loader(loader.name, loader)
m = importlib.util.module_from_spec(spec)
loader.exec_module(m)


class HubCoordinationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.calls = []
        self.active_timer = False
        self.root_patch = patch.object(m, 'ROOT', self.root)
        self.command_patch = patch.object(m, 'command', self.command)
        self.profile_patch = patch.object(m, 'profile', return_value={'ssid': 'synthetic', 'password': 'synthetic-only'})
        for p in (self.root_patch, self.command_patch, self.profile_patch):
            p.start()
        start = int(time.time())+10
        self.request = {'id': str(uuid.uuid4()), 'kind': 'wifi', 'target': str(uuid.uuid4()), 'switchAt': start, 'deadline': start+180}

    def tearDown(self):
        for p in (self.root_patch, self.command_patch, self.profile_patch):
            p.stop()
        self.temp.cleanup()

    def command(self, *args, **kwargs):
        self.calls.append(args)
        if args[0] == '/usr/bin/systemctl':
            if args[-1].startswith('initiativetracker-n8-') and not self.active_timer:
                raise RuntimeError('not active')
            return 'active'
        if args[0] == '/usr/bin/systemd-run':
            self.active_timer = True
        return ''

    def test_duplicate_schedule_and_conflicting_replay(self):
        m.schedule(self.request)
        count = len([x for x in self.calls if x[0] == '/usr/bin/systemd-run'])
        m.schedule(self.request)
        self.assertEqual(count, len([x for x in self.calls if x[0] == '/usr/bin/systemd-run']))
        with self.assertRaises(ValueError):
            m.schedule({**self.request, 'deadline': self.request['deadline']+1})

    def test_crash_between_journal_and_systemd_is_retried_with_original_deadline(self):
        m.write(self.root/'coordination.json', {'request': self.request, 'started': False})
        m.schedule(self.request)
        self.assertTrue(self.active_timer)
        self.assertEqual(json.loads((self.root/'coordination.json').read_text())['request'], self.request)

    def test_overlapping_different_transaction_cannot_replace_armed_transition(self):
        m.schedule(self.request)
        with self.assertRaises(ValueError):
            m.schedule({**self.request, 'id': str(uuid.uuid4())})

    def test_runner_dispatches_existing_independent_controller_once(self):
        m.schedule(self.request)
        with patch.object(m.time, 'time', return_value=self.request['switchAt']):
            m.run(self.request['id'])
            m.run(self.request['id'])
        handoffs = [x for x in self.calls if x[0].endswith('initiativetracker-return-handoff')]
        self.assertEqual(len(handoffs), 1)
        self.assertIn(self.request['target'], handoffs[0])

    def test_unarmed_or_missed_window_cannot_confirm(self):
        m.schedule(self.request)
        with self.assertRaises(ValueError):
            m.confirm(self.request['id'])
        journal = {'request': {**self.request, 'deadline': int(time.time())-1}, 'started': True}
        m.write(self.root/'coordination.json', journal)
        with self.assertRaises(ValueError):
            m.confirm(self.request['id'])

    def test_disabled_timer_prevents_scheduling(self):
        with patch.object(m, 'command', side_effect=RuntimeError('timer inactive')):
            with self.assertRaises(RuntimeError):
                m.schedule(self.request)
        self.assertFalse((self.root/'coordination.json').exists())

    def test_preparation_preflight_rejects_unsafe_source_before_any_network_change(self):
        with patch.object(m, 'command', return_value='unsafe spaced ID'):
            with self.assertRaises(ValueError):
                m.preflight(self.request['target'])

    def test_preparation_preflight_checks_existing_gate_and_independent_timers(self):
        def command(*args, **kwargs):
            if 'GENERAL.CONNECTION' in args:return 'IT-External-A'
            if 'GENERAL.CON-UUID' in args:return str(uuid.uuid4())
            if args[0]=='/usr/bin/systemctl':return 'active'
            return 'RETURN_SWITCH_ENABLED=false'
        with patch.object(m, 'command', side_effect=command):
            with self.assertRaises(ValueError):
                m.preflight(self.request['target'])

    def test_deadline_validation_rejects_late_extended_or_arbitrary_destination(self):
        for change in [{'switchAt': int(time.time())-1}, {'deadline': self.request['deadline']+1000}, {'kind': 'shell'}, {'target': ';invalid'}]:
            with self.assertRaises(ValueError):
                m.validate({**self.request, **change}, time.time())


if __name__ == '__main__':
    unittest.main()
