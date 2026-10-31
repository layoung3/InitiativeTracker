"""Real short-lived broker/timer processes; fake persistent NM, no agent or hardware."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
import uuid

REPOSITORY = Path(__file__).resolve().parents[2]
ADAPTER = Path(__file__).with_name('agent_broker_fixture.py')


class CandidateLeaseProcessTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.directory = Path(self.tmp.name)
        self.generation = str(uuid.uuid4())
        self.credentials = {'ssid': 'Synthetic Candidate', 'password': 'synthetic-only'}
        self.run_broker('bootstrap', generation=self.generation, recovery=self.credentials)

    def tearDown(self):
        self.tmp.cleanup()

    def run_broker(self, action, **values):
        process = subprocess.run([sys.executable, '-I', str(ADAPTER), str(REPOSITORY), str(self.directory)],
                                 input=json.dumps({'action': action, **values}), capture_output=True, text=True, timeout=10)
        self.assertEqual(process.returncode, 0, process.stderr)
        return json.loads(process.stdout)

    def network(self):
        return json.loads((self.directory/'fake-network.json').read_text())

    def update_network(self, **values):
        network = self.network()
        network.update(values)
        (self.directory/'fake-network.json').write_text(json.dumps(network))

    def state(self):
        return json.loads((self.directory/'root/state.json').read_text())

    def join_candidate(self):
        self.assertTrue(self.run_broker('open-provisioning')['ok'])
        self.assertTrue(self.run_broker('candidate-network', profile=self.credentials)['ok'])
        candidate = self.state()['provisioning']['candidate']
        self.assertEqual(candidate, self.network()['active'])
        return candidate

    def test_no_previous_connection_timer_expiry_disconnects_unapproved_candidate(self):
        candidate = self.join_candidate()
        self.assertIsNone(self.state()['provisioning']['previous'])
        result = self.run_broker('__fixture-expire-lease')
        self.assertTrue(result['ok'])
        self.assertIsNone(result['network']['active'])
        self.assertIsNone(result['state']['provisioning'])
        self.assertTrue(any('down' in command and candidate in command for command in result['network']['commands']))
        self.assertIsNone(self.run_broker('__fixture-tick')['network']['active'])

    def test_failed_previous_restore_disconnects_candidate_and_retries_after_timer_restart(self):
        previous = str(uuid.uuid4())
        self.update_network(active=previous)
        self.join_candidate()
        self.update_network(fail_up=[previous])  # Failed up leaves the candidate active until explicit down.
        result = self.run_broker('__fixture-expire-lease')
        self.assertIsNone(result['network']['active'])
        self.assertIsNone(result['state']['provisioning'])
        self.assertEqual(result['state']['restoration']['profile'], previous)
        self.update_network(fail_up=[])
        state = self.state()
        state['restoration']['next'] = time.time()-1
        (self.directory/'root/state.json').write_text(json.dumps(state))
        restored = self.run_broker('__fixture-tick')
        self.assertEqual(restored['network']['active'], previous)
        self.assertIsNone(restored['state']['restoration'])

    def test_teardown_failure_preserves_lease_intent_for_another_timer_process(self):
        candidate = self.join_candidate()
        self.update_network(fail_down=[candidate])
        self.assertFalse(self.run_broker('__fixture-expire-lease')['ok'])
        self.assertEqual(self.network()['active'], candidate)
        self.assertEqual(self.state()['provisioning']['phase'], 'restoring')
        self.assertEqual(self.state()['provisioning']['candidate'], candidate)
        self.update_network(fail_down=[])
        retried = self.run_broker('__fixture-tick')
        self.assertIsNone(retried['network']['active'])
        self.assertIsNone(retried['state']['provisioning'])

    def test_new_lease_does_not_overwrite_approved_candidate_profile(self):
        first = self.join_candidate()
        self.assertTrue(self.run_broker('rebind', generation=str(uuid.uuid4()), recovery=self.credentials)['ok'])
        profile = self.directory/'profiles'/('IT-N8-'+first+'.nmconnection')
        original = profile.read_bytes()
        self.assertTrue(self.run_broker('open-provisioning')['ok'])
        self.assertTrue(self.run_broker('candidate-network', profile={'ssid': 'Another Synthetic', 'password': 'another-synthetic'})['ok'])
        second = self.state()['provisioning']['candidate']
        self.assertNotEqual(first, second)
        self.assertEqual(profile.read_bytes(), original)
        restored = self.run_broker('__fixture-expire-lease')
        self.assertEqual(restored['network']['active'], first)
        self.assertEqual(profile.read_bytes(), original)


if __name__ == '__main__':
    unittest.main()
