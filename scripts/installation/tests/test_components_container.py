"""Fresh home ownership and existing-component replacement in disposable roots."""
import importlib.util
import json
import os
from pathlib import Path
import pwd
import shutil
import stat
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
CONTAINER = os.environ.get('IT_DISPOSABLE_CONTAINER') == '1' and Path('/.dockerenv').is_file() and os.geteuid() == 0


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


upgrade = load('component_upgrade', ROOT / 'hub-updater/upgrade-existing-components.py')
fresh = load('fresh_install', ROOT / 'scripts/installation/install.py')
provision = load('fresh_provision', ROOT / 'hub-updater/provision.py')
fixtures = load('install_test_fixtures', ROOT / 'scripts/installation/tests/test_install.py')


@unittest.skipUnless(CONTAINER, 'requires root ownership operations in the explicitly disposable container')
class ComponentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        try:
            cls.desktop = pwd.getpwnam('screenoperator')
        except KeyError:
            subprocess.run(['useradd', '--create-home', '--home-dir', '/srv/tabletop-screen', '--shell', '/bin/bash', 'screenoperator'], check=True)
            cls.desktop = pwd.getpwnam('screenoperator')

    def setUp(self):
        self.base = Path(tempfile.mkdtemp(prefix='it-components-', dir='/opt'))
        self.base.chmod(0o755)
        self.addCleanup(shutil.rmtree, self.base)
        self.root = self.base / 'system'
        self.root.mkdir()
        self.bundle = self.base / 'bundle'
        shutil.copytree(ROOT / 'hub-updater', self.bundle)
        self.manager = upgrade.Components(self.desktop, root=self.root, bundle=self.bundle)
        self.owner = (self.desktop.pw_uid, self.desktop.pw_gid)
        for name, target in self.manager.targets.items():
            path = self.manager.path(target)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((self.bundle / 'original' / name).read_bytes())
            path.chmod(0o755)
            if name == upgrade.NAMES[2]:
                os.chown(path, *self.owner)
        home = self.manager.path(self.desktop.pw_dir)
        os.chown(home, *self.owner)
        launcher = self.manager.targets[upgrade.NAMES[2]]
        self.autostart = '#!/bin/sh\n# desktop default\ntrue &\n# BEGIN InitiativeTracker Hub\n"' + launcher + '" &\n# END InitiativeTracker Hub\n'
        for target, data, mode in (
            (self.manager.guards[0], b'MANIFEST_SOURCE=/var/cache/initiativetracker-updater/incoming/approved.manifest.json\n', 0o644),
            (self.manager.guards[1], b'{"fixture":"private-configuration"}', 0o640),
            (self.manager.guards[2], self.autostart.encode(), 0o755),
            ('/var/lib/initiativetracker/database', b'fixture-database', 0o600),
            ('/var/lib/initiativetracker/identity', b'fixture-identity', 0o600),
            ('/etc/initiativetracker/network/recovery-wifi.password', b'fixture-network-secret', 0o600),
            (self.desktop.pw_dir + '/.config/initiativetracker-hub/chromium/Cookies', b'fixture-pairing', 0o600),
            ('/etc/sudoers.d/existing-policy', b'# existing policy unchanged\n', 0o440),
        ):
            path = self.manager.path(target)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            path.chmod(mode)
            if target.startswith(self.desktop.pw_dir):
                os.chown(path, *self.owner)
        current = self.manager.path('/opt/initiativetracker/current')
        current.parent.mkdir(parents=True)
        current.symlink_to('/opt/initiativetracker/releases/0.1.5-linux-arm64')
        executable = self.manager.path('/opt/initiativetracker/releases/0.1.5-linux-arm64/InitiativeTracker.Web')
        executable.parent.mkdir(parents=True)
        executable.write_text('#!/bin/sh\nexit 0\n')
        executable.chmod(0o755)

    def preserved(self):
        targets = set(self.manager.targets.values())
        return {str(path.relative_to(self.root)): (path.read_bytes(), stat.S_IMODE(path.stat().st_mode), path.stat().st_uid, path.stat().st_gid)
                for path in self.root.rglob('*') if path.is_file() and '/' + str(path.relative_to(self.root)) not in targets
                and not str(path).startswith(str(self.manager.path(upgrade.BACKUPS)))}

    def test_existing_component_upgrade_and_rollback_preserve_integration_and_all_state(self):
        before = self.preserved()
        original = {name: self.manager.read(self.manager.path(path)) for name, path in self.manager.targets.items()}
        directory = self.manager.apply()
        self.assertEqual(self.preserved(), before)
        self.assertEqual(stat.S_IMODE(directory.stat().st_mode), 0o700)
        self.assertTrue(all(stat.S_IMODE(path.stat().st_mode) == 0o600 for path in directory.iterdir()))
        self.assertEqual(self.manager.path(self.manager.guards[2]).read_text(), self.autostart)
        for name, path in self.manager.targets.items():
            data, metadata = self.manager.read(self.manager.path(path))
            self.assertEqual(data, (self.bundle / 'runtime' / name).read_bytes())
            self.assertEqual((metadata['uid'], metadata['gid'], metadata['mode']),
                             (*self.owner, 0o755) if name == upgrade.NAMES[2] else (0, 0, 0o755))
        self.assertIsNone(self.manager.apply(), 'Successful repeat should not create another backup or overwrite scripts')
        self.manager.rollback(directory)
        for name, path in self.manager.targets.items():
            self.assertEqual(self.manager.read(self.manager.path(path)), original[name])
        self.assertEqual(self.preserved(), before)

    def test_unrecognized_target_or_candidate_hash_refuses_before_backup(self):
        target = self.manager.path(self.manager.targets[upgrade.NAMES[2]])
        os.setxattr(target, 'user.it-disposable-fixture', b'preserve-metadata')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'extended attributes'):
            self.manager.apply()
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
        os.removexattr(target, 'user.it-disposable-fixture')
        target.write_text('# unexpected launcher\n')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'hash is unrecognized'):
            self.manager.apply()
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
        target.write_bytes((self.bundle / 'original' / upgrade.NAMES[2]).read_bytes())
        (self.bundle / 'runtime' / upgrade.NAMES[0]).write_text('# unexpected candidate\n')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'Candidate runtime hash mismatch'):
            self.manager.apply()

    def test_mid_replacement_failure_restores_original_scripts_without_state_changes(self):
        before = self.preserved()
        original = {name: self.manager.read(self.manager.path(path)) for name, path in self.manager.targets.items()}
        atomic = self.manager.atomic
        calls = 0
        def fail_second(*args):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError('disposable replacement failure')
            return atomic(*args)
        with patch.object(self.manager, 'atomic', side_effect=fail_second):
            with self.assertRaisesRegex(upgrade.UpgradeError, 'Private backup'):
                self.manager.apply()
        for name, path in self.manager.targets.items():
            self.assertEqual(self.manager.read(self.manager.path(path)), original[name])
        self.assertEqual(self.preserved(), before)

    def test_rollback_refuses_later_autostart_or_component_edits(self):
        directory = self.manager.apply()
        autostart = self.manager.path(self.manager.guards[2])
        autostart.write_text(self.autostart + '# later user change\n')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'Configuration/autostart changed'):
            self.manager.rollback(directory)
        autostart.write_text(self.autostart)
        target = self.manager.path(self.manager.targets[upgrade.NAMES[0]])
        target.chmod(0o750)
        with self.assertRaisesRegex(upgrade.UpgradeError, 'Installed component changed'):
            self.manager.rollback(directory)
        target.chmod(0o755)
        target.write_text('# later administrator change\n')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'Installed component changed'):
            self.manager.rollback(directory)
        self.assertEqual(target.read_text(), '# later administrator change\n')

    def test_mid_rollback_failure_restores_candidates_and_preserves_state(self):
        directory = self.manager.apply()
        before = self.preserved()
        candidates = {name: self.manager.read(self.manager.path(path)) for name, path in self.manager.targets.items()}
        atomic = self.manager.atomic
        calls = 0
        def fail_second(*args):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError('disposable rollback failure')
            return atomic(*args)
        with patch.object(self.manager, 'atomic', side_effect=fail_second):
            with self.assertRaisesRegex(upgrade.UpgradeError, 'Script rollback stopped'):
                self.manager.rollback(directory)
        for name, path in self.manager.targets.items():
            self.assertEqual(self.manager.read(self.manager.path(path)), candidates[name])
        self.assertEqual(self.preserved(), before)

    def test_inspection_and_cancel_do_not_write_or_run_any_components(self):
        before = self.preserved()
        components = {path: self.manager.path(path).read_bytes() for path in self.manager.targets.values()}
        self.manager.inspect()
        with patch.object(upgrade.sys.stdin, 'isatty', return_value=False):
            with self.assertRaisesRegex(upgrade.UpgradeError, 'Interactive approval'):
                upgrade.confirmation('REPLACE-COMPONENTS')
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
        self.assertEqual(self.preserved(), before)
        self.assertEqual(components, {path: self.manager.path(path).read_bytes() for path in self.manager.targets.values()})

    def test_exported_bundle_inspects_original_and_current_receipts_and_roundtrips_rollback(self):
        # Export real Git objects with Windows-style conversion settings, then
        # exercise the extracted inspector against this test's fake 0.1.5 root.
        archive_tests = load('archive_fixtures', ROOT / 'scripts/installation/tests/test_updater_archive.py')
        fixture = archive_tests.ArchiveTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        unpack = self.base / 'archive'
        with tarfile.open(fixture.export()) as archive:
            archive.extractall(unpack, filter='data')
        bundle = unpack / 'hub-updater'
        # Mirror documented staging modes: Git tar source modes may be 0664/
        # 0775, which the inspector correctly refuses for privileged apply.
        for path in (bundle, *bundle.rglob('*')):
            path.chmod(0o755 if path.is_dir() else 0o644)
        archived = load('exported_upgrade', bundle / 'upgrade-existing-components.py')
        manager = archived.Components(self.desktop, root=self.root, bundle=bundle)
        before = self.preserved()
        originals = {path: manager.read(manager.path(path)) for path in manager.targets.values()}
        candidates, components, _, current = manager.inspect()
        self.assertEqual(set(components), set(archived.NAMES))
        self.assertTrue(current.endswith('/0.1.5-linux-arm64'))
        self.assertFalse(manager.path(archived.BACKUPS).exists())
        self.assertEqual(self.preserved(), before)
        directory = manager.apply()
        receipt = json.loads((directory / 'receipt.json').read_text())
        for name in archived.NAMES:
            self.assertEqual(receipt['components'][name]['after_sha256'], candidates[name]['sha256'])
            self.assertEqual(receipt['components'][name]['before']['sha256'], candidates[name]['original_sha256'])
        manager.inspect()
        self.assertIsNone(manager.apply())
        manager.rollback(directory)
        self.assertEqual({path: manager.read(manager.path(path)) for path in manager.targets.values()}, originals)
        self.assertEqual(self.preserved(), before)
        for variant, error in (('original', 'Original source provenance mismatch'),
                               ('runtime', 'Candidate runtime hash mismatch')):
            path = bundle / variant / archived.NAMES[0]
            data = path.read_bytes()
            path.write_bytes(data.replace(b'\n', b'\r\n'))
            with self.assertRaisesRegex(archived.UpgradeError, error):
                manager.inspect()
            path.write_bytes(data)

    def test_fresh_hub_uses_actual_alternate_home_and_desktop_ownership(self):
        class OwnerHost(fixtures.FakeHost):
            def write(self, path, content, mode=0o644, owner=None, replace=False):
                return fresh.Host.write(self, path, content, mode, owner, replace)
        root = self.base / 'fresh-system'
        root.mkdir()
        host = OwnerHost(root)
        host.mkdir(self.desktop.pw_dir, 0o750, self.owner)
        host.mkdir('/var/lib/initiativetracker-install', 0o700)
        host.write('/usr/share/wayland-sessions/LXDE-pi-labwc.desktop', '[Desktop Entry]\nExec=labwc\n')
        artifacts = self.base / 'artifacts'
        artifacts.mkdir()
        manifest, package = fixtures.release(artifacts)
        provision.provision(host, self.desktop, manifest, package)
        fresh.desktop_install(host, self.desktop, dict(role='hub', display_output='', display_mode='', autologin=True))
        local = self.desktop.pw_dir + '/.local/lib/initiativetracker-hub'
        launcher = host.path(local + '/initiativetracker-hub-launch')
        self.assertEqual((launcher.stat().st_uid, launcher.stat().st_gid, stat.S_IMODE(launcher.stat().st_mode)), (*self.owner, 0o755))
        autostart = host.path(self.desktop.pw_dir + '/.config/labwc/autostart')
        self.assertEqual((autostart.stat().st_uid, autostart.stat().st_gid), self.owner)
        self.assertIn(local + '/initiativetracker-hub-launch', autostart.read_text())
        self.assertIn('--supervise', autostart.read_text())
        self.assertIn(self.desktop.pw_dir + '/.config/initiativetracker-hub/kiosk.json', autostart.read_text())
        self.assertTrue(autostart.read_text().rstrip().endswith('&'))
        self.assertNotIn('/home/dnd', autostart.read_text())
        for name, directory in ((upgrade.NAMES[0], '/usr/local/sbin/'), (upgrade.NAMES[1], '/usr/local/bin/')):
            installed = host.path(directory + name)
            self.assertEqual((installed.stat().st_uid, installed.stat().st_gid, stat.S_IMODE(installed.stat().st_mode)), (0, 0, 0o755))


if __name__ == '__main__':
    unittest.main()
