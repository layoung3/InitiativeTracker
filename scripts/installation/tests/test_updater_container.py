"""Real bash/tar/sudo, fake systemd/health, synthetic data in a disposable container.

Never runs on the development host. Production scripts have no test bypasses;
only the temporary instrumented script has path and command substitutions.
"""
import importlib.util
import io
import json
import os
import pwd
from pathlib import Path
import shutil
import stat
import subprocess
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
CONTAINER = os.environ.get('IT_DISPOSABLE_CONTAINER') == '1' and Path('/.dockerenv').is_file() and os.geteuid() == 0


@unittest.skipUnless(CONTAINER, 'requires the explicitly disposable Debian test container')
class UpdaterLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.base = Path(tempfile.mkdtemp(prefix='it-updater-', dir='/opt'))
        self.base.chmod(0o755)
        self.addCleanup(shutil.rmtree, self.base)
        self.incoming = self.base / 'cache/incoming'
        self.incoming.mkdir(parents=True)
        self.incoming.chmod(0o755)
        self.app = self.base / 'app'
        self.old = self.app / 'releases/0.1.5-linux-arm64'
        self.old.mkdir(parents=True)
        (self.old / 'InitiativeTracker.Web').write_text('#!/bin/sh\nexit 0\n')
        (self.old / 'InitiativeTracker.Web').chmod(0o755)
        (self.app / 'current').symlink_to(self.old)
        self.data = self.base / 'data'
        self.data.mkdir(mode=0o750)
        for name, content in [('database', 'old-database'), ('DataProtection-Keys', 'fixture-key'), ('identity', 'fixture-guid')]:
            (self.data / name).write_text(content)
        (self.data / 'secure-access').mkdir(mode=0o700)
        (self.data / 'secure-access/state.bin').write_bytes(b'backup-era-secure-grant')
        (self.data / 'media').mkdir()
        (self.data / 'media/portrait.png').write_bytes(b'private-media-fixture')
        self.manifest = self.incoming / 'new.manifest.json'
        package = self.incoming / 'InitiativeTracker-0.1.6-linux-arm64.tar.gz'
        with tarfile.open(package, 'w:gz') as archive:
            for name, payload in [('InitiativeTracker.Web', b'fake executable'), ('wwwroot/asset.txt', b'public')]:
                info = tarfile.TarInfo(name)
                info.size = len(payload)
                archive.addfile(info, io.BytesIO(payload))
        import hashlib
        self.manifest.write_text(json.dumps({'formatVersion': 1, 'product': 'InitiativeTracker', 'platform': 'linux-arm64',
            'version': '0.1.6', 'healthPath': '/system/health', 'package': {'fileName': package.name,
            'sizeBytes': package.stat().st_size, 'sha256': hashlib.sha256(package.read_bytes()).hexdigest()}}))
        self.config = self.base / 'etc/updater.conf'
        self.config.parent.mkdir()
        self.config.write_text('MANIFEST_SOURCE=' + str(self.manifest) + '\n')
        self.config.chmod(0o644)
        (self.config.parent / 'appsettings.Production.json').write_text('{"fixture":"no-secrets"}')
        self.mode = self.base / 'mode'
        self.mode.write_text('success')
        self.bin = self.base / 'bin'
        self.bin.mkdir()
        self.stub('uname', '#!/bin/sh\necho aarch64\n')
        self.stub('sleep', '#!/bin/sh\nexit 0\n')
        self.stub('logger', '#!/bin/sh\nexit 0\n')
        self.stub('systemctl', '''#!/usr/local/bin/python3 -I
from pathlib import Path
import sys
base=Path(__file__).resolve().parents[1]
mode=(base/'mode').read_text()
active=(base/'app/current').resolve().name
operation=sys.argv[1]
with (base/'operations').open('a') as stream: stream.write(operation+' '+active+'\\n')
if operation=='stop' and (mode=='stopfail-initial' or (mode=='stopfail-rollback' and active.startswith('0.1.6'))):
    sys.exit(1)
if operation=='start' and active.startswith('0.1.5') and mode in ('healthfail-new','startfail-new'):
    if not (base/'data/secure-access/mutation.pending').is_file(): sys.exit(98)
    with (base/'operations').open('a') as stream: stream.write('restored-secure-access-fenced\\n')
if operation=='start' and active.startswith('0.1.6'):
    (base/'data/database').write_text('migrated-database')
    (base/'data/secure-access/state.bin').write_bytes(b'new-release-revoked-grant')
    if mode=='startfail-new': sys.exit(1)
sys.exit(0)
''')
        self.stub('curl', '''#!/usr/local/bin/python3 -I
from pathlib import Path
import json,sys
base=Path(__file__).resolve().parents[1]
active=(base/'app/current').resolve().name
mode=(base/'mode').read_text()
if active.startswith('0.1.6') and mode in ('healthfail-new','stopfail-rollback'):
    sys.exit(22)
version=active.removesuffix('-linux-arm64')
Path(sys.argv[sys.argv.index('--output')+1]).write_text(json.dumps({'status':'healthy','version':version}))
''')
        text = (ROOT / 'hub-updater/runtime/initiativetracker-install-update').read_text()
        replacements = {
            '/etc/initiativetracker/updater.conf': str(self.config),
            '/opt/initiativetracker': str(self.app),
            '/var/lib/initiativetracker': str(self.data),
            '/var/backups/initiativetracker': str(self.base / 'backups'),
            '/var/cache/initiativetracker-updater': str(self.base / 'cache'),
            '/etc/initiativetracker/appsettings.Production.json': str(self.config.parent / 'appsettings.Production.json'),
            '/run/lock/initiativetracker-updater.lock': str(self.base / 'update.lock'),
            'PATH=/usr/sbin:/usr/bin:/sbin:/bin': 'PATH=' + str(self.bin) + ':/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin',
        }
        for before, after in replacements.items():
            text = text.replace(before, after)
        self.script = self.base / 'instrumented-installer'
        self.script.write_text(text)
        self.script.chmod(0o755)

    def stub(self, name, content):
        path = self.bin / name
        path.write_text(content)
        path.chmod(0o755)

    def execute(self, mode='success'):
        self.mode.write_text(mode)
        return subprocess.run(['bash', str(self.script)], capture_output=True, text=True, timeout=20)

    def test_success_retains_private_backup_and_service_readable_release(self):
        result = self.execute()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.app / 'current').resolve().name, '0.1.6-linux-arm64')
        self.assertEqual((self.data / 'database').read_text(), 'migrated-database')
        backup = next((self.base / 'backups').glob('*.tar.gz'))
        self.assertEqual(stat.S_IMODE(backup.stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(backup.parent.stat().st_mode), 0o700)
        with tarfile.open(backup) as archive:
            self.assertEqual(archive.extractfile('./database').read(), b'old-database')
            self.assertEqual(archive.extractfile('./DataProtection-Keys').read(), b'fixture-key')
        result = subprocess.run(['sudo', '-u', 'initiativetracker', 'test', '-x', str(self.app / 'current/InitiativeTracker.Web')])
        self.assertEqual(result.returncode, 0)
        self.assertEqual(stat.S_IMODE((self.app / 'current').resolve().stat().st_mode), 0o755)

    def test_health_and_service_start_failures_restore_release_and_whole_data(self):
        for mode in ('healthfail-new', 'startfail-new'):
            with self.subTest(mode=mode):
                result = self.execute(mode)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual((self.app / 'current').resolve(), self.old, result.stdout + result.stderr)
                self.assertEqual((self.data / 'database').read_text(), 'old-database')
                self.assertEqual((self.data / 'identity').read_text(), 'fixture-guid')
                self.assertEqual((self.data / 'DataProtection-Keys').read_text(), 'fixture-key')
                self.assertEqual((self.data / 'media/portrait.png').read_bytes(), b'private-media-fixture')
                self.assertEqual((self.data / 'secure-access/state.bin').read_bytes(), b'backup-era-secure-grant')
                self.assertTrue((self.data / 'secure-access/mutation.pending').is_file())
                self.assertEqual(stat.S_IMODE((self.data / 'secure-access/mutation.pending').stat().st_mode), 0o600)
                self.assertIn('restored-secure-access-fenced', (self.base / 'operations').read_text())
                failed = next(self.base.glob('data.failed-*'))
                self.assertEqual((failed / 'database').read_text(), 'migrated-database')
                shutil.rmtree(self.app / 'releases/0.1.6-linux-arm64')
                shutil.rmtree(failed)
                for backup in (self.base / 'backups').glob('*'):
                    backup.unlink()

    def test_unsafe_restored_secure_directory_prevents_rollback_restart(self):
        secure = self.data / 'secure-access'
        shutil.rmtree(secure)
        outside = self.base / 'outside-secure'
        outside.mkdir()
        (outside / 'sentinel').write_bytes(b'unchanged')
        secure.symlink_to(outside, target_is_directory=True)
        result = self.execute('healthfail-new')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve().name, '0.1.6-linux-arm64')
        self.assertNotIn('start 0.1.5', (self.base / 'operations').read_text())
        self.assertEqual((outside / 'sentinel').read_bytes(), b'unchanged')
        self.assertFalse((outside / 'mutation.pending').exists())
        self.assertIn('recovery incomplete', result.stdout)

    def test_stop_failure_during_rollback_preserves_live_data_and_link(self):
        result = self.execute('stopfail-rollback')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve().name, '0.1.6-linux-arm64')
        self.assertEqual((self.data / 'database').read_text(), 'migrated-database')
        self.assertEqual(list(self.base.glob('data.failed-*')), [])
        self.assertIn('recovery incomplete', result.stdout)

    def test_initial_stop_failure_never_backs_up_or_moves_data(self):
        result = self.execute('stopfail-initial')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve(), self.old)
        self.assertEqual((self.data / 'database').read_text(), 'old-database')
        self.assertEqual(list((self.base / 'backups').glob('*.tar.gz')), [])

    def test_backup_failure_restarts_old_service_without_data_moves(self):
        self.stub('tar', '#!/bin/sh\ncase " $* " in *" -czf "*) exit 1;; esac\nexec /usr/bin/tar "$@"\n')
        result = self.execute()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve(), self.old)
        self.assertEqual((self.data / 'database').read_text(), 'old-database')
        self.assertIn('start 0.1.5', (self.base / 'operations').read_text())

    def test_pending_link_failure_restarts_old_service(self):
        (self.app / 'current.new').symlink_to(self.old)
        result = self.execute()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve(), self.old)
        self.assertIn('start 0.1.5', (self.base / 'operations').read_text())

    def test_untrusted_or_traversing_manifest_refused_before_service_stop(self):
        self.config.write_text('MANIFEST_SOURCE=' + str(self.incoming / '../outside.manifest.json') + '\n')
        result = self.execute()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.base / 'operations').exists())

    def sudo_from_hostile_directory(self, hostile_environment=False, vulnerable_control=False):
        spec = importlib.util.spec_from_file_location('provision', ROOT / 'hub-updater/provision.py')
        provision = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(provision)
        operator = pwd.getpwnam('operator')
        hostile = Path(tempfile.mkdtemp(prefix='it-hostile-', dir='/home/operator'))
        os.chown(hostile, operator.pw_uid, operator.pw_gid)
        self.addCleanup(shutil.rmtree, hostile)
        marker = hostile / 'import-executed'
        # Only a harmless UID marker inside the disposable directory. Never
        # executes outside the explicitly marked container or touches real data.
        payload = 'import os\nwith open(' + repr(str(marker)) + ', "a") as f: f.write(str(os.geteuid()) + "\\n")\nraise RuntimeError("hostile import control")\n'
        for name in ('pathlib', 'json', 'tarfile', 'sitecustomize', 'usercustomize'):
            module = hostile / (name + '.py')
            module.write_text(payload)
            os.chown(module, operator.pw_uid, operator.pw_gid)
        bash_env = hostile / 'bash-env.sh'
        bash_env.write_text('/usr/bin/id -u >> ' + str(marker) + '\n')
        os.chown(bash_env, operator.pw_uid, operator.pw_gid)
        binary = Path('/usr/local/sbin/initiativetracker-install-update')
        policy = Path('/etc/sudoers.d/initiativetracker-import-fixture')
        self.addCleanup(binary.unlink, missing_ok=True)
        self.addCleanup(policy.unlink, missing_ok=True)
        policy.write_text(provision.sudo_policy('operator') +
            'Defaults!/usr/local/sbin/initiativetracker-install-update env_keep += "PYTHONPATH PYTHONHOME PYTHONUSERBASE BASH_ENV"\n')
        policy.chmod(0o440)
        subprocess.run(['visudo', '-c'], check=True, capture_output=True)
        text = self.script.read_text()
        env_receipt = self.base / 'inherited-python-environment'
        text = text.replace('set -Eeuo pipefail\n', 'set -Eeuo pipefail\nprintf "%s\\n" "${PYTHONPATH:-}" "${PYTHONHOME:-}" "${PYTHONUSERBASE:-}" "${BASH_ENV:-}" > ' + str(env_receipt) + '\n', 1)
        command = ['sudo', '-u', 'operator', 'env']
        if hostile_environment:
            command += ['PYTHONPATH=' + str(hostile), 'PYTHONHOME=/nonexistent-hostile-python-home', 'PYTHONUSERBASE=' + str(hostile), 'BASH_ENV=' + str(bash_env)]
        command += ['sudo', '-n', str(binary)]
        if vulnerable_control:
            # Prove this test detects the reported root import, rather than
            # merely relying on sudo's normal environment cleanup.
            binary.write_text(text.replace('/usr/bin/python3 -I - "$CONFIG_FILE"', '/usr/bin/python3 - "$CONFIG_FILE"', 1))
            binary.chmod(0o755)
            result = subprocess.run(command, cwd=hostile, capture_output=True, text=True, timeout=20)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('0', marker.read_text().splitlines())
            marker.unlink()
        binary.write_text(text)
        binary.chmod(0o755)
        result = subprocess.run(command, cwd=hostile, capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse(marker.exists(), 'Caller-controlled module was imported')
        self.assertEqual((self.app / 'current').resolve().name, '0.1.6-linux-arm64')
        if hostile_environment:
            self.assertEqual(env_receipt.read_text().splitlines(), [str(hostile), '/nonexistent-hostile-python-home', str(hostile), str(bash_env)])

    def test_real_sudo_hostile_cwd_modules_cannot_execute_as_root(self):
        self.sudo_from_hostile_directory(vulnerable_control=True)

    def test_real_sudo_preserved_python_environment_is_ignored(self):
        self.sudo_from_hostile_directory(hostile_environment=True)

    def checker_fixture(self):
        # Retain the actual checker and actual hardened root lifecycle, changing
        # only fixed paths and substituting a recording sudo transport. Real
        # sudo authorization is tested independently below.
        curl = (self.bin / 'curl').read_text()
        curl = curl.replace("Path(sys.argv[sys.argv.index('--output')+1]).write_text", "if '--output' not in sys.argv:\n    print(json.dumps({'version':'0.1.5'}))\n    sys.exit(0)\nPath(sys.argv[sys.argv.index('--output')+1]).write_text")
        self.stub('curl', curl)
        self.stub('sudo', '#!/bin/sh\nprintf "%s\\n" "$#" > ' + str(self.base / 'sudo-argc') + '\n[ "$#" -eq 2 ] && [ "$1" = "-n" ] || exit 99\nexec "$2"\n')
        self.stub('zenity', '#!/bin/sh\nprintf "%s\\n" "$1" >> ' + str(self.base / 'dialogs') + '\nexit 0\n')
        checker = self.base / 'checker'
        text = (ROOT / 'hub-updater/runtime/initiativetracker-check-update').read_text()
        text = text.replace('/etc/initiativetracker/updater.conf', str(self.config))
        text = text.replace('/usr/local/sbin/initiativetracker-install-update', str(self.script))
        text = text.replace('/opt/initiativetracker/current', str(self.app / 'current'))
        checker.write_text(text)
        checker.chmod(0o755)
        return checker

    def test_checker_prompt_uses_zero_argument_installer_and_installs_versioned_release(self):
        checker = self.checker_fixture()
        result = subprocess.run(['bash', str(checker)], env={**os.environ, 'PATH': str(self.bin) + ':' + os.environ['PATH']}, capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.base / 'sudo-argc').read_text().strip(), '2')
        self.assertEqual((self.app / 'current').resolve().name, '0.1.6-linux-arm64')
        self.assertEqual((self.base / 'dialogs').read_text().splitlines(), ['--question', '--info'])

    def test_checker_reports_failed_install_after_attempted_rollback(self):
        checker = self.checker_fixture()
        self.mode.write_text('healthfail-new')
        result = subprocess.run(['bash', str(checker)], env={**os.environ, 'PATH': str(self.bin) + ':' + os.environ['PATH']}, capture_output=True, text=True, timeout=20)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((self.app / 'current').resolve(), self.old)
        self.assertIn('--error', (self.base / 'dialogs').read_text())

    def test_hub_launcher_keeps_health_checker_health_chromium_order(self):
        events = self.base / 'launch-events'
        self.stub('curl', '#!/bin/sh\necho health >> ' + str(events) + '\nexit 0\n')
        checker = self.base / 'checker'
        checker.write_text('#!/bin/sh\necho checker >> ' + str(events) + '\nexit 1\n')
        checker.chmod(0o755)
        self.stub('chromium', '#!/bin/sh\necho chromium >> ' + str(events) + '\nprintf "%s\\n" "$@" > ' + str(self.base / 'chromium-args') + '\n')
        launcher = self.base / 'launcher'
        text = (ROOT / 'hub-updater/runtime/initiativetracker-hub-launch').read_text()
        text = text.replace('/usr/local/bin/initiativetracker-check-update', str(checker)).replace('/usr/bin/chromium', str(self.bin / 'chromium'))
        launcher.write_text(text)
        result = subprocess.run(['bash', str(launcher)], env={**os.environ, 'HOME': str(self.base / 'home'), 'PATH': str(self.bin) + ':' + os.environ['PATH']}, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(events.read_text().splitlines(), ['health', 'checker', 'health', 'chromium'])
        args = (self.base / 'chromium-args').read_text()
        self.assertIn('http://127.0.0.1:5000/setup', args)
        self.assertIn('/.config/initiativetracker-hub/chromium', args)
        self.stub('python-entry', '#!/bin/sh\nprintf "%s\\n" "$@" > ' + str(self.base / 'supervisor-args') + '\n')
        launcher.write_text(text.replace('/usr/bin/python3', str(self.bin / 'python-entry')))
        result = subprocess.run(['bash', str(launcher), '--supervise', str(self.base / 'kiosk.json')],
                                env={**os.environ, 'HOME': str(self.base / 'home')}, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.base / 'supervisor-args').read_text().splitlines(),
                         ['-I', str(self.base / 'home/.local/lib/initiativetracker-hub/kiosk.py'), str(self.base / 'kiosk.json')])
        self.assertEqual(events.read_text().splitlines(), ['health', 'checker', 'health', 'chromium'])


@unittest.skipUnless(CONTAINER, 'requires real sudo in explicitly disposable Debian container')
class NarrowSudoTests(unittest.TestCase):
    def test_zero_argument_entry_only_and_service_account_has_no_grant(self):
        spec = importlib.util.spec_from_file_location('provision', ROOT / 'hub-updater/provision.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        binary = Path('/usr/local/sbin/initiativetracker-install-update')
        binary.write_text('#!/bin/sh\ntest "$#" -eq 0 || exit 9\necho disposable-fixture-only\n')
        binary.chmod(0o755)
        policy = Path('/etc/sudoers.d/initiativetracker-fixture')
        policy.write_text(module.sudo_policy('operator'))
        policy.chmod(0o440)
        self.addCleanup(policy.unlink)
        self.addCleanup(binary.unlink)
        subprocess.run(['visudo', '-c'], check=True, capture_output=True)
        def call(user, *command):
            return subprocess.run(['sudo', '-u', user, 'sudo', '-n', *command], capture_output=True, text=True)
        self.assertEqual(call('operator', str(binary)).stdout.strip(), 'disposable-fixture-only')
        self.assertNotEqual(call('operator', str(binary), '/tmp/evil.manifest.json').returncode, 0)
        self.assertNotEqual(call('operator', '/bin/sh', '-c', 'true').returncode, 0)
        self.assertNotEqual(call('initiativetracker', str(binary)).returncode, 0)


if __name__ == '__main__':
    unittest.main()
