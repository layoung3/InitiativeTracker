"""Real DBus/NM keyfile acceptance; no Wi-Fi hardware, privilege escalation or host mounts."""
import importlib.machinery
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import unittest
import uuid

ISOLATED = (os.environ.get('IT_N8_REAL_NM_CONTAINER') == '1' and Path('/.dockerenv').exists()
            and {p.name for p in Path('/sys/class/net').iterdir()} == {'lo'})


@unittest.skipUnless(ISOLATED, 'Requires the network-none disposable NetworkManager container')
class RealNetworkManagerTests(unittest.TestCase):
    def setUp(self):
        source = Path(__file__).resolve().parents[1] / 'initiativetracker-satellite-network'
        loader = importlib.machinery.SourceFileLoader('real_nm_broker', str(source))
        spec = importlib.util.spec_from_loader(loader.name, loader)
        self.broker = importlib.util.module_from_spec(spec)
        loader.exec_module(self.broker)
        self.broker.PROFILES.mkdir(parents=True, exist_ok=True)
        self.created = []

    def tearDown(self):
        for identity in self.created:
            subprocess.run(['nmcli', 'connection', 'delete', 'uuid', identity], capture_output=True, check=True)

    def field(self, identity, field, secrets=False):
        args = ['nmcli', '--escape', 'no'] + (['--show-secrets'] if secrets else [])
        return subprocess.check_output(args + ['-g', field, 'connection', 'show', 'uuid', identity], text=True).rstrip('\n')

    def test_satellite_profiles_round_trip_exact_secrets_and_wpa3(self):
        for security in ('wpa-psk', 'sae'):
            identity = str(uuid.uuid4())
            value = {'ssid': 'Synthetic WiFi 名', 'password': ' synthetic\\secret ', 'keyManagement': security}
            self.broker.stage_profile(identity, value)
            self.created.append(identity)
            self.assertEqual(self.field(identity, '802-11-wireless.ssid'), value['ssid'])
            self.assertEqual(self.field(identity, '802-11-wireless-security.psk', True), value['password'])
            self.assertEqual(self.field(identity, '802-11-wireless-security.key-mgmt'), security)
            self.assertEqual(self.field(identity, 'connection.autoconnect'), 'no')
            self.assertEqual((self.broker.PROFILES / ('IT-N8-'+identity+'.nmconnection')).stat().st_mode & 0o777, 0o600)

    def test_hub_staging_loads_inactive_profile_with_real_nm(self):
        value = {'ssid': 'Synthetic Hub 名', 'password': ' synthetic\\secret '}
        source = Path(__file__).resolve().parents[2] / 'pi-network' / 'initiativetracker-wifi-provision'
        result = subprocess.run(['python3', '-I', str(source)], input=json.dumps(value), text=True, capture_output=True, check=True)
        identity = json.loads(result.stdout)['uuid']
        self.created.append(identity)
        self.assertEqual(self.field(identity, '802-11-wireless.ssid'), value['ssid'])
        self.assertEqual(self.field(identity, '802-11-wireless-security.psk', True), value['password'])
        self.assertEqual(self.field(identity, 'connection.autoconnect'), 'no')
        self.assertNotIn(value['password'], result.stdout + result.stderr)

    def test_no_wifi_device_is_reported_as_failed_activation(self):
        identity = str(uuid.uuid4())
        self.broker.stage_profile(identity, {'ssid': 'Synthetic absent', 'password': 'synthetic-only'})
        self.created.append(identity)
        with self.assertRaises(RuntimeError):
            self.broker.up(identity)
