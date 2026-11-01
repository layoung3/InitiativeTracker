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
        self.lock = self.manager.path(upgrade.LOCK)
        self.lock.parent.mkdir(parents=True)
        self.lock.parent.chmod(0o1777)  # Debian default, fixture root only.
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

    def cli(self, *args):
        # Real main dispatch, root/mode checks, lock, and lifecycle; only the
        # selected fixture root and interactive approval transport are replaced.
        with patch.object(upgrade, 'Components', return_value=self.manager), \
                patch.object(upgrade, 'confirmation'), \
                patch.object(upgrade.sys, 'argv', ['upgrade', '--desktop-user', self.desktop.pw_name, *map(str, args)]):
            return upgrade.main()

    def test_debian_sticky_directory_reproduces_old_failure_before_any_replacement(self):
        original = {p: self.manager.read(self.manager.path(p)) for p in self.manager.targets.values()}
        # This was the exact main() gate before open/flock/apply/rollback.
        with self.assertRaisesRegex(upgrade.UpgradeError, 'Root-controlled path'):
            self.manager.no_links(self.lock, trusted=True)
        self.assertFalse(self.lock.exists())
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
        self.assertEqual({p: self.manager.read(self.manager.path(p)) for p in original}, original)

    def test_cli_inspect_apply_verify_noop_rollback_keep_same_lock_and_state(self):
        # Existing graphical updater creates 0644 under umask 022. Preserve it.
        self.lock.write_bytes(b'existing-lock-content')
        self.lock.chmod(0o644)
        inode = self.lock.stat().st_ino
        before = self.preserved()
        originals = {p: self.manager.read(self.manager.path(p)) for p in self.manager.targets.values()}
        self.assertEqual(self.cli('--inspect'), 0)
        self.assertEqual(self.cli('--apply'), 0)
        directory, = self.manager.path(upgrade.BACKUPS).iterdir()
        self.assertEqual(self.cli('--inspect'), 0)
        self.assertEqual(self.cli('--apply'), 0)
        self.assertEqual(list(self.manager.path(upgrade.BACKUPS).iterdir()), [directory])
        self.assertEqual(self.cli('--rollback', directory), 0)
        self.assertEqual({p: self.manager.read(self.manager.path(p)) for p in originals}, originals)
        self.assertEqual(self.preserved(), before)
        self.assertEqual(self.lock.stat().st_ino, inode)
        self.assertEqual(stat.S_IMODE(self.lock.parent.stat().st_mode), 0o1777)

    def test_lock_creation_preserves_inode_and_releases_after_exception(self):
        with self.assertRaisesRegex(RuntimeError, 'fixture'):
            with self.manager.updater_lock():
                inode = self.lock.stat().st_ino
                self.assertEqual((self.lock.stat().st_uid, self.lock.stat().st_gid, stat.S_IMODE(self.lock.stat().st_mode)), (0, 0, 0o600))
                raise RuntimeError('fixture')
        with self.manager.updater_lock():
            self.assertEqual(self.lock.stat().st_ino, inode)
        self.lock.parent.chmod(0o755)
        with self.manager.updater_lock():
            self.assertEqual(self.lock.stat().st_ino, inode)

    def test_unsafe_lock_entries_refused_without_modifying_target_or_components(self):
        victim = self.base / 'victim'
        victim.write_bytes(b'never truncate or chmod')
        victim.chmod(0o644)
        original = {p: self.manager.read(self.manager.path(p)) for p in self.manager.targets.values()}
        for kind in ('symlink', 'dangling', 'hardlink', 'fifo', 'directory', 'user', 'group', 'writable'):
            with self.subTest(kind=kind):
                if kind in ('symlink', 'dangling'):
                    self.lock.symlink_to(victim if kind == 'symlink' else self.base / 'missing')
                elif kind == 'hardlink':
                    os.link(victim, self.lock)
                elif kind == 'fifo':
                    os.mkfifo(self.lock)
                elif kind == 'directory':
                    self.lock.mkdir()
                else:
                    self.lock.write_bytes(b'preserve unsafe entry')
                    self.lock.chmod(0o666 if kind == 'writable' else 0o644)
                    os.chown(self.lock, self.owner[0] if kind == 'user' else 0, self.owner[1] if kind == 'group' else 0)
                entry = self.lock.lstat()
                with self.assertRaises((upgrade.UpgradeError, OSError)):
                    self.cli('--apply')
                self.assertEqual(self.lock.lstat(), entry)
                self.assertEqual(victim.read_bytes(), b'never truncate or chmod')
                self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
                self.assertEqual({p: self.manager.read(self.manager.path(p)) for p in original}, original)
                if kind == 'directory':
                    self.lock.rmdir()
                else:
                    self.lock.unlink()

    def test_unsafe_lock_ancestry_refused_and_generic_trust_checks_stay_strict(self):
        for mode, uid, gid in ((0o777, 0, 0), (0o1777, self.owner[0], 0), (0o1777, 0, self.owner[1]), (0o1775, 0, 0)):
            with self.subTest(mode=oct(mode), uid=uid, gid=gid):
                self.lock.parent.chmod(mode)
                os.chown(self.lock.parent, uid, gid)
                with self.assertRaisesRegex(upgrade.UpgradeError, 'lock directory'):
                    with self.manager.updater_lock():
                        self.fail('unsafe directory accepted')
        os.chown(self.lock.parent, 0, 0)
        self.lock.parent.chmod(0o1777)
        run = self.lock.parent.parent
        run.chmod(0o1777)
        with self.assertRaisesRegex(upgrade.UpgradeError, 'lock directory'):
            with self.manager.updater_lock():
                self.fail('sticky exception escaped /run/lock')
        run.chmod(0o755)
        real = run / 'real-lock'
        self.lock.parent.rename(real)
        self.lock.parent.symlink_to(real, target_is_directory=True)
        with self.assertRaises(OSError):
            with self.manager.updater_lock():
                self.fail('symlink directory accepted')
        self.lock.parent.unlink()
        real.rename(self.lock.parent)
        # Generic trusted paths still reject sticky writable source/config/backup ancestry.
        for directory in (self.bundle, self.manager.path('/etc/initiativetracker'), self.manager.path('/usr/local/sbin')):
            old = directory.stat().st_mode
            directory.chmod(0o1777)
            with self.assertRaisesRegex(upgrade.UpgradeError, 'Root-controlled path'):
                self.manager.apply()
            directory.chmod(stat.S_IMODE(old))
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())

    def test_unprivileged_users_cannot_replace_root_lock_in_sticky_directory(self):
        with self.manager.updater_lock():
            inode = self.lock.stat().st_ino
            result = subprocess.run(['sudo', '-u', self.desktop.pw_name, 'python3', '-I', '-c',
                'import os, sys; os.unlink(sys.argv[1])', str(self.lock)], capture_output=True, text=True, timeout=5)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('Operation not permitted', result.stderr)
            self.assertEqual(self.lock.stat().st_ino, inode)

    def test_creation_race_rejects_untrusted_winner_without_mutation(self):
        open_file = upgrade.os.open
        def attacker_wins(path, flags, mode=0o777, **kwargs):
            if path == self.lock.name and flags & os.O_EXCL:
                self.lock.write_bytes(b'untrusted race winner')
                os.chown(self.lock, *self.owner)
            return open_file(path, flags, mode, **kwargs)
        with patch.object(upgrade.os, 'open', side_effect=attacker_wins):
            with self.assertRaisesRegex(upgrade.UpgradeError, 'lock file'):
                self.cli('--apply')
        self.assertEqual(self.lock.read_bytes(), b'untrusted race winner')
        self.assertEqual(self.lock.stat().st_uid, self.owner[0])
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())

    def test_lock_identity_and_link_count_rechecked_after_flock(self):
        flock = upgrade.fcntl.flock
        for action in ('replace', 'link'):
            with self.subTest(action=action):
                def race(fd, flags):
                    flock(fd, flags)
                    if action == 'replace':
                        self.lock.unlink()  # Simulate a trusted-root race, fixture only.
                        self.lock.write_bytes(b'replacement')
                    else:
                        os.link(self.lock, self.base / 'extra-link')
                with patch.object(upgrade.fcntl, 'flock', side_effect=race):
                    with self.assertRaisesRegex(upgrade.UpgradeError, 'link identity'):
                        with self.manager.updater_lock():
                            self.fail('inode race accepted')
                self.lock.unlink()
                (self.base / 'extra-link').unlink(missing_ok=True)

    def graphical_lock_process(self, variant):
        # Execute the exact real installer's locking stanza, with only its fixed
        # pathname redirected to the disposable root. No application updater runs.
        source = (self.bundle / variant / upgrade.NAMES[0]).read_text()
        start = source.index('exec 9>"/run/lock/initiativetracker-updater.lock"')
        end = source.index('work_directory=', start)
        stanza = source[start:end].replace(upgrade.LOCK, str(self.lock))
        script = 'fail() { echo "$*" >&2; exit 73; }; umask 022\n' + stanza + 'echo held; read -r release\n'
        process = subprocess.Popen(['bash', '-c', script], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(self.stop_holder, process)
        return process

    @staticmethod
    def stop_holder(process):
        if process.poll() is None:
            process.communicate('release\n', timeout=5)

    def test_real_graphical_updater_flock_interoperability_both_directions(self):
        for variant in ('original', 'runtime'):
            with self.subTest(variant=variant):
                holder = self.graphical_lock_process(variant)
                self.assertEqual(holder.stdout.readline().strip(), 'held')
                inode = self.lock.stat().st_ino
                for action in ('--apply', '--rollback'):
                    with self.assertRaisesRegex(upgrade.UpgradeError, 'application update is running'):
                        self.cli(action, *([self.base / 'unused-backup'] if action == '--rollback' else []))
                self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
                self.stop_holder(holder)
                with self.manager.updater_lock():
                    contender = self.graphical_lock_process(variant)
                    out, err = contender.communicate(timeout=5)
                    self.assertEqual(contender.returncode, 73, out + err)
                    self.assertIn('already running', err)
                    self.assertEqual(self.lock.stat().st_ino, inode)
                holder = self.graphical_lock_process(variant)
                self.assertEqual(holder.stdout.readline().strip(), 'held')
                self.stop_holder(holder)
                self.assertEqual(self.lock.stat().st_ino, inode)

    def test_two_component_processes_contend_and_exception_releases_for_rollback(self):
        code = """import importlib.util, pathlib, pwd, sys
spec = importlib.util.spec_from_file_location('upgrade', sys.argv[1])
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
manager = m.Components(pwd.getpwnam(sys.argv[2]), root=pathlib.Path(sys.argv[3]), bundle=pathlib.Path(sys.argv[4]))
apply = manager.apply
def paused_apply():
    print('held', flush=True)
    sys.stdin.readline()
    return apply()
manager.apply = paused_apply
m.Components = lambda desktop: manager
m.confirmation = lambda token: None
sys.argv = ['upgrade', '--desktop-user', manager.desktop.pw_name, '--apply']
m.main()
"""
        holder = subprocess.Popen(['python3', '-I', '-c', code, str(ROOT / 'hub-updater/upgrade-existing-components.py'),
                                   self.desktop.pw_name, str(self.root), str(self.bundle)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.addCleanup(self.stop_holder, holder)
        self.assertIn('Pause kiosk/update activity', holder.stdout.readline())
        self.assertEqual(holder.stdout.readline().strip(), 'held')
        with self.assertRaisesRegex(upgrade.UpgradeError, 'application update is running'):
            self.cli('--apply')
        self.assertFalse(self.manager.path(upgrade.BACKUPS).exists())
        self.stop_holder(holder)  # Complete the other CLI's real replacement.
        self.assertEqual(holder.returncode, 0)
        directory, = self.manager.path(upgrade.BACKUPS).iterdir()
        self.cli('--rollback', directory)
        atomic = self.manager.atomic
        calls = 0
        lock_probes = []
        def fail_second(*args):
            nonlocal calls
            # Record, then assert outside the transaction's broad recovery catch.
            contender = subprocess.run(['flock', '-n', str(self.lock), 'true'], timeout=5)
            lock_probes.append(contender.returncode)
            calls += 1
            if calls == 2:
                raise OSError('fixture failure')
            return atomic(*args)
        with patch.object(self.manager, 'atomic', side_effect=fail_second):
            with self.assertRaisesRegex(upgrade.UpgradeError, 'original scripts restored'):
                self.cli('--apply')
        self.assertEqual(lock_probes, [1, 1, 1], 'replacement/failure/restoration lost shared lock')
        # Acquire after failed apply, then complete real apply and explicit rollback.
        self.cli('--apply')
        directories = list(self.manager.path(upgrade.BACKUPS).iterdir())
        self.cli('--rollback', directories[0])

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
