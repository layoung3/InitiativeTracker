import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


m = module('install', ROOT / 'scripts/installation/install.py')
kiosk = module('kiosk', ROOT / 'scripts/installation/kiosk.py')
provision = module('provision', ROOT / 'hub-updater/provision.py')
HUB_ID = '01234567-89ab-cdef-0123-456789abcdef'
PASSWORD = 'disposable-test-secret'


class FakeHost(m.Host):
    def __init__(self, root):
        super().__init__(root)
        self.commands = []
        self.fail = None
        self.arch = 'arm64'

    def write(self, path, content, mode=0o644, owner=None, replace=False):
        # Fixture ownership maps root to the current test user. Real ownership
        # and sudo authorization are checked separately in a disposable container.
        if owner:
            owner = (os.getuid(), os.getgid())
        return super().write(path, content, mode, owner, replace)

    def run(self, *args, **kwargs):
        self.commands.append((args, kwargs))
        if self.fail and self.fail in args:
            raise m.InstallError('simulated command failure')
        if args[0] == 'dpkg' and '--print-architecture' in args:
            return self.arch
        if args[0] == 'uname':
            return 'aarch64'
        if args[0] == 'nmcli' and 'GENERAL.TYPE' in args:
            return 'wifi'
        if args[0] == 'iw':
            return 'wiphy 0' if 'dev' in args else 'Supported interface modes:\n * AP\n'
        if args[0] == 'curl' and args[-1].endswith('/system/hub-info'):
            return json.dumps({'hubId': HUB_ID})
        if args[0] == 'curl' and args[-1].endswith('/system/health'):
            return json.dumps({'status': 'healthy', 'version': '0.1.5'})
        if args[0] == 'dotnet':
            return 'Microsoft.NETCore.App 10.0.1\nMicrosoft.AspNetCore.App 10.0.1'
        return ''


def release(directory, entries=None):
    package = directory / 'InitiativeTracker-0.1.5-linux-arm64.tar.gz'
    metadata = json.dumps({'runtimeOptions': {'tfm': 'net10.0', 'includedFrameworks': [
        {'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}]}}).encode()
    if entries is None:
        entries = [('InitiativeTracker.Web', b'fake apphost'),
                   ('InitiativeTracker.Web.runtimeconfig.json', metadata),
                   ('appsettings.Production.json', b'{}'), ('wwwroot/test.txt', b'public')]
    with tarfile.open(package, 'w:gz') as archive:
        for name, data in entries:
            entry = tarfile.TarInfo(name)
            entry.size = len(data)
            archive.addfile(entry, io.BytesIO(data))
    manifest = directory / 'release-manifest.json'
    manifest.write_text(json.dumps({'formatVersion': 1, 'product': 'InitiativeTracker',
        'version': '0.1.5', 'platform': 'linux-arm64', 'healthPath': '/system/health',
        'package': {'fileName': package.name, 'sizeBytes': package.stat().st_size, 'sha256': m.digest(package)}}))
    return manifest, package


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.host = FakeHost(self.base / 'system')
        self.host.root.mkdir()
        self.desktop = SimpleNamespace(pw_name='operator', pw_uid=os.getuid(), pw_gid=os.getgid(), pw_dir='/home/operator')
        self.app = SimpleNamespace(pw_name='initiativetracker', pw_uid=os.getuid(), pw_gid=os.getgid(), pw_dir=m.DATA)
        for path, text in (('/etc/os-release', 'ID=raspbian\nVERSION_ID="13"\n'),
                           ('/etc/hostname', 'old-host\n'), ('/etc/hosts', '127.0.0.1 localhost\n'),
                           ('/usr/share/wayland-sessions/LXDE-pi-labwc.desktop', '[Desktop Entry]\nExec=labwc\n'),
                           ('/home/operator/.config/labwc/autostart', '# existing desktop\ntrue &\n')):
            self.host.write(path, text)
        for path in ('/usr/bin/labwc', '/usr/sbin/lightdm', '/proc/device-tree/model', '/sys/class/net/wlan0', '/run/systemd/system'):
            self.host.path(path).parent.mkdir(parents=True, exist_ok=True)
            self.host.path(path).write_text('Raspberry Pi 5')
        self.host.owned.clear()
        self.artifacts = self.base / 'artifacts'
        self.artifacts.mkdir()
        self.manifest, self.package = release(self.artifacts)
        self.settings = dict(role='satellite', desktop_user='operator', hostname='sat-test',
                             external_wifi={'ssid': 'FixtureLAN', 'password': PASSWORD},
                             hub_wifi={'ssid': 'FixtureHub', 'password': PASSWORD},
                             autologin=True, display_output='', display_mode='',
                             system_runtime=False, hub_url='http://hub.local:5000', hub_id=HUB_ID,
                             hotspot_ssid='', hotspot_password=None)
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.output = io.StringIO()
        self.stack.enter_context(contextlib.redirect_stdout(self.output))
        self.stack.enter_context(contextlib.redirect_stderr(self.output))
        self.stack.enter_context(patch.object(m.pwd, 'getpwnam', side_effect=lambda n: self.app if n == 'initiativetracker' else self.desktop))

    def commission(self, role):
        self.settings['role'] = role
        return m.commission(self.host, role, self.desktop, '13', self.settings, self.manifest, self.package)

    def test_satellite_complete_persistent_profile_and_repeat_no_changes(self):
        self.assertEqual(self.commission('satellite'), 0)
        profile = self.host.path('/home/operator/.local/share/initiativetracker-satellite/chromium')
        (profile / 'Cookies').write_bytes(b'pairing-fixture')
        before = {str(x): x.read_bytes() for x in self.host.root.rglob('*') if x.is_file()}
        commands = len(self.host.commands)
        self.assertEqual(self.commission('satellite'), 0)
        self.assertEqual(len(self.host.commands), commands)
        self.assertEqual(before, {str(x): x.read_bytes() for x in self.host.root.rglob('*') if x.is_file()})
        self.assertIn('LXDE-pi-labwc', self.host.path('/etc/lightdm/lightdm.conf.d/90-initiativetracker.conf').read_text())

    def test_hub_complete_preserves_update_layout_and_private_modes(self):
        self.assertEqual(self.commission('hub'), 0)
        self.assertEqual(str(self.host.path(m.APP + '/current').readlink()), m.APP + '/releases/0.1.5-linux-arm64')
        self.assertEqual(stat.S_IMODE(self.host.path(m.APP + '/releases/0.1.5-linux-arm64').stat().st_mode), 0o755)
        self.assertEqual(stat.S_IMODE(self.host.path(m.DATA).stat().st_mode), 0o750)
        self.assertEqual(stat.S_IMODE(self.host.path('/var/backups/initiativetracker').stat().st_mode), 0o700)
        self.assertIn('.manifest.json', self.host.path(m.ETC + '/updater.conf').read_text())
        commands = [x[0] for x in self.host.commands]
        self.assertIn(('systemctl', 'enable', 'initiativetracker-boot-fallback.timer'), commands)
        self.assertNotIn(('systemctl', 'enable', '--now', 'initiativetracker-boot-fallback.timer'), commands)
        self.assertTrue(self.host.path(m.NETWORK + '/initiativetracker-network-discovery').is_file())
        self.assertFalse(any('disabled-example' in str(x) for x in self.host.root.rglob('*')))
        self.assertFalse(any('aspnetcore-runtime-10.0' in x for x in commands))

    def test_no_activation_or_networkmanager_restart_and_no_password_argv_output(self):
        self.commission('satellite')
        for args, kwargs in self.host.commands:
            self.assertNotIn(PASSWORD, str(args) + str(kwargs))
            if args[0] == 'nmcli':
                self.assertEqual(args[1:3], ('connection', 'load'))
            self.assertFalse('NetworkManager.service' in args and any(x in args for x in ('restart', 'stop', 'disable')))
        self.assertNotIn(PASSWORD, self.output.getvalue())
        for target in self.host.path('/etc/NetworkManager/system-connections').glob('*'):
            self.assertEqual(stat.S_IMODE(target.stat().st_mode), 0o600)
        external = self.host.path('/etc/NetworkManager/system-connections/IT-External-1.nmconnection').read_text()
        self.assertIn('autoconnect-priority = 100', external)

    def test_hub_hotspot_is_random_private_and_never_autoconnects(self):
        self.commission('hub')
        secret = self.host.path(m.ETC + '/network/recovery-wifi.password')
        self.assertRegex(secret.read_text(), r'^[a-f0-9]{24}\n$')
        self.assertEqual(stat.S_IMODE(secret.stat().st_mode), 0o600)
        profile = self.host.path('/etc/NetworkManager/system-connections/InitiativeTracker-Recovery.nmconnection').read_text()
        self.assertIn('autoconnect=false', profile)
        self.assertIn('InitiativeTracker-01234567', profile)
        self.assertNotIn(secret.read_text().strip(), str(self.host.commands) + self.output.getvalue())

    def test_keyfile_password_backslashes_and_spaces_are_escaped_without_argv(self):
        credentials = {'ssid': ' Test LAN ', 'password': ' test\\secret '}
        m.stage_wifi(self.host, credentials, 'IT-External-1', 100)
        text = self.host.path('/etc/NetworkManager/system-connections/IT-External-1.nmconnection').read_text()
        self.assertIn(r'ssid = \sTest\sLAN\s', text)
        self.assertIn(r'psk = \stest\\secret\s', text)
        self.assertNotIn(credentials['password'], str(self.host.commands))

    def test_existing_data_and_dangling_current_are_refused(self):
        self.host.mkdir(m.DATA)
        database = self.host.path(m.DATA + '/database')
        database.write_bytes(b'do-not-erase')
        with self.assertRaises(m.InstallError):
            m.existing(self.host, 'hub', self.desktop)
        self.assertEqual(database.read_bytes(), b'do-not-erase')
        database.unlink()
        self.host.path(m.DATA).rmdir()
        self.host.mkdir(m.APP)
        self.host.path(m.APP + '/current').symlink_to('/nonexistent')
        with self.assertRaises(m.InstallError):
            m.existing(self.host, 'hub', self.desktop)

    def test_interrupted_apply_preserves_receipt_and_fails_closed_on_repeat(self):
        self.host.fail = 'install'
        with self.assertRaises(m.InstallError):
            self.commission('satellite')
        receipt = self.host.path(m.STATE + '/receipt.json')
        self.assertEqual(json.loads(receipt.read_text())['status'], 'in-progress')
        before = receipt.read_bytes()
        with self.assertRaises(m.InstallError):
            self.commission('satellite')
        self.assertEqual(receipt.read_bytes(), before)

    def test_backup_precedes_autostart_change_and_preserves_defaults(self):
        self.commission('satellite')
        receipt = json.loads(self.host.path(m.STATE + '/receipt.json').read_text())
        backup = self.host.path(receipt['backups'] + '/home/operator/.config/labwc/autostart')
        self.assertEqual(backup.read_text(), '# existing desktop\ntrue &\n')
        self.assertEqual(stat.S_IMODE(backup.stat().st_mode), 0o600)
        self.assertIn('true &', self.host.path('/home/operator/.config/labwc/autostart').read_text())

    def test_lightdm_main_cannot_override_new_dropin_and_is_backed_up(self):
        original = '[LightDM]\nlog-directory=/var/log/lightdm\n[Seat:*]\nautologin-user=imager-user\nuser-session=old-session\ngreeter-session=existing\n'
        self.host.write('/etc/lightdm/lightdm.conf', original)
        self.commission('satellite')
        receipt = json.loads(self.host.path(m.STATE + '/receipt.json').read_text())
        self.assertEqual(self.host.path(receipt['backups'] + '/etc/lightdm/lightdm.conf').read_text(), original)
        after = self.host.path('/etc/lightdm/lightdm.conf').read_text()
        self.assertNotRegex(after, r'(?m)^autologin-user=')
        self.assertIn('greeter-session=existing', after)
        self.assertIn('log-directory=/var/log/lightdm', after)

    def test_custom_lightdm_seat_requires_review(self):
        with self.assertRaises(m.InstallError):
            m.lightdm_main('[Seat:seat1]\nautologin-user=another\n')

    def test_dry_run_never_creates_host_or_runs_commands_or_reads_passwords(self):
        with patch.object(m, 'Host', side_effect=AssertionError('host created')), patch.object(m, 'read_password', side_effect=AssertionError('password read')):
            self.assertEqual(m.main(['satellite', '--dry-run']), 0)
            self.assertEqual(m.main(['hub', '--dry-run', '--manifest', str(self.manifest), '--package', str(self.package)]), 0)

    def test_preflight_rejects_x64_and_unsupported_os_before_mutation(self):
        self.host.arch = 'amd64'
        with self.assertRaisesRegex(m.InstallError, 'ARM64'):
            m.preflight(self.host, 'hub')
        self.host.path('/etc/os-release').write_text('VERSION_ID=11\n')
        with self.assertRaises(m.InstallError):
            m.preflight(self.host, 'hub')

    def test_malicious_manifests_archives_and_wrong_runtime_rejected(self):
        for entries in ([('../escape', b'x')], [('/absolute', b'x')], [('InitiativeTracker.Web', b'x')], [('duplicate', b'x'), ('duplicate', b'x')]):
            manifest, package = release(self.artifacts, entries)
            with self.assertRaises(m.InstallError):
                m.validate_release(manifest, package)
        manifest, package = release(self.artifacts)
        package.write_bytes(package.read_bytes() + b'altered')
        with self.assertRaises(m.InstallError):
            m.validate_release(manifest, package)

    def test_secret_getpass_never_falls_back_to_echo(self):
        with patch.object(m.sys.stdin, 'isatty', return_value=False), patch.object(m.getpass, 'getpass') as password:
            with self.assertRaises(m.InstallError):
                m.read_password('test')
            password.assert_not_called()

    def test_symlink_managed_paths_cannot_escape_fixture(self):
        self.host.path('/etc/lightdm').mkdir()
        self.host.path('/etc/lightdm/lightdm.conf.d').symlink_to(self.artifacts)
        with self.assertRaises(m.InstallError):
            m.existing(self.host, 'satellite', self.desktop)

    def test_cancel_confirmation_has_no_mutations(self):
        with patch('builtins.input', return_value='no'):
            with self.assertRaises(m.InstallError):
                m.confirm('Network change', 'INSTALL-FRESH')
        self.assertEqual(self.host.commands, [])

    def test_original_source_hashes_and_byte_sizes(self):
        m.verify_updater_sources()
        record = json.loads((ROOT / 'hub-updater/provenance.json').read_text())
        for entry in record['files']:
            path = ROOT / 'hub-updater/original' / entry['name']
            self.assertEqual(path.stat().st_size, entry['bytes'])
            self.assertEqual(m.digest(path), entry['sha256'])

    def test_wrong_running_release_never_passes_first_install_health(self):
        with patch.object(m.time, 'sleep'):
            with self.assertRaises(m.InstallError):
                m.wait_health(self.host, 'http://127.0.0.1:5000', attempts=1, expected_version='0.1.6')

    def test_hub_origin_rejects_embedded_secrets_and_pairing_url(self):
        for value in ('http://user:secret@hub/', 'http://hub/display?token=secret', 'file:///etc/passwd', 'http://hub/#token'):
            with self.assertRaises(m.InstallError):
                m.validate_url(value)

    def test_kiosk_commands_keep_persistent_origin_and_original_hub_launcher(self):
        satellite = kiosk.command(self.settings, Path('/home/operator'))
        self.assertEqual(satellite[-1], 'http://hub.local:5000/display')
        self.assertIn('--user-data-dir=/home/operator/.local/share/initiativetracker-satellite/chromium', satellite)
        self.settings['role'] = 'hub'
        self.assertEqual(kiosk.command(self.settings, Path('/home/operator')), ['/home/operator/.local/lib/initiativetracker-hub/initiativetracker-hub-launch'])


if __name__ == '__main__':
    unittest.main()
