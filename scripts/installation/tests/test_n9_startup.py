"""Disposable process mocks: never runs system Chromium, an updater or a Pi."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import signal
import time
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('n9_kiosk', ROOT / 'scripts/installation/kiosk.py')
kiosk = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kiosk)


class N9StartupTests(unittest.TestCase):
    def test_waiting_profile_is_separate_and_local_for_alternate_home(self):
        home = Path('/srv/tables/desktop user')
        wait = kiosk.waiting_command(home)
        display = kiosk.command({'role': 'satellite', 'hub_url': 'https://trusted.example'}, home)
        self.assertEqual(wait[-1], 'file:///srv/tables/desktop%20user/.local/lib/initiativetracker-satellite/waiting.html')
        self.assertNotEqual(next(x for x in wait if x.startswith('--user-data-dir=')),
                            next(x for x in display if x.startswith('--user-data-dir=')))
        for command in (wait, display):
            self.assertNotIn('--ignore-certificate-errors', command)
            self.assertNotIn('--no-sandbox', command)

    def test_no_wait_browser_when_hub_is_ready(self):
        with patch.object(kiosk, 'reachable', return_value=True), patch.object(kiosk.subprocess, 'Popen') as spawn:
            kiosk.wait_for_satellite_hub({}, Path('/home/operator'))
        spawn.assert_not_called()

    def test_offline_retries_then_closes_wait_browser_before_display(self):
        child = Mock(pid=456)
        child.poll.return_value = None
        with patch.object(kiosk, 'reachable', side_effect=[False, False, True]) as probe, \
                patch.object(kiosk.subprocess, 'Popen', return_value=child) as spawn, \
                patch.object(kiosk.time, 'sleep') as sleep, patch.object(kiosk.os, 'killpg') as kill:
            kiosk.wait_for_satellite_hub({}, Path('/home/operator'))
        self.assertEqual(probe.call_count, 3)
        self.assertEqual(sleep.call_count, 2)
        spawn.assert_called_once()
        kill.assert_called_once_with(child.pid, kiosk.signal.SIGTERM)
        child.wait.assert_called_once_with(timeout=5)

    def test_wait_browser_exit_restarts_and_interrupt_cleans_process(self):
        child = Mock(pid=456)
        child.poll.side_effect = [0, None]
        with patch.object(kiosk, 'reachable', return_value=False), \
                patch.object(kiosk.subprocess, 'Popen', return_value=child) as spawn, \
                patch.object(kiosk.time, 'sleep', side_effect=[None, SystemExit(0)]), \
                patch.object(kiosk.os, 'killpg') as kill:
            with self.assertRaises(SystemExit):
                kiosk.wait_for_satellite_hub({}, Path('/home/operator'))
        self.assertEqual(spawn.call_count, 2)
        kill.assert_called_once()

    def test_unresponsive_child_gets_bounded_stop(self):
        child = Mock(pid=456)
        child.poll.return_value = None
        child.wait.side_effect = [subprocess.TimeoutExpired('fixture', 5), 0]
        with patch.object(kiosk.os, 'killpg') as kill:
            kiosk.stop_child(child)
        self.assertEqual([c.args[1] for c in kill.call_args_list], [kiosk.signal.SIGTERM, kiosk.signal.SIGKILL])

    def test_wrong_hub_and_tls_failure_do_not_pass_readiness(self):
        settings = {'hub_url': 'https://hub.example', 'hub_id': 'expected'}
        health, identity = Mock(), Mock()
        health.__enter__ = Mock(return_value=health)
        health.__exit__ = Mock()
        identity.__enter__ = Mock(return_value=identity)
        identity.__exit__ = Mock()
        opener = Mock()
        opener.open.side_effect = [health, identity]
        with patch.object(kiosk.urllib.request, 'build_opener', return_value=opener), \
                patch.object(kiosk.json, 'load', side_effect=[{'status': 'healthy'}, {'hubId': 'wrong'}]):
            self.assertFalse(kiosk.reachable(settings))
        opener.open.side_effect = OSError('certificate rejected')
        with patch.object(kiosk.urllib.request, 'build_opener', return_value=opener):
            self.assertFalse(kiosk.reachable(settings))
        self.assertTrue(all(c.args[0].startswith('https://hub.example/') for c in opener.open.call_args_list))

    def launcher(self, failing=False, updater_exit=0):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        log = root / 'events'
        scripts = {
            'curl': '#!/bin/sh\necho health >> "$N9_EVENTS"\nexit ' + ('1' if failing else '0') + '\n',
            'sleep': '#!/bin/sh\nexit 0\n',
            'zenity': '#!/bin/sh\necho "dialog $*" >> "$N9_EVENTS"\nexit 0\n',
            'checker': '#!/bin/sh\necho updater >> "$N9_EVENTS"\nexit ' + str(updater_exit) + '\n',
            'chromium': '#!/bin/sh\necho "chromium $*" >> "$N9_EVENTS"\nexit 0\n'}
        for name, text in scripts.items():
            path = root / name
            path.write_text(text)
            path.chmod(0o755)
        text = (ROOT / 'hub-updater/runtime/initiativetracker-hub-launch').read_text()
        text = text.replace('/usr/local/bin/initiativetracker-check-update', str(root / 'checker'))
        text = text.replace('/usr/bin/chromium', str(root / 'chromium'))
        launch = root / 'launch'
        launch.write_text(text)
        environment = dict(os.environ, HOME=str(root / 'alternate-home'),
                           PATH=str(root) + ':/usr/bin:/bin', N9_EVENTS=str(log))
        result = subprocess.run(['/bin/bash', '-p', str(launch)], env=environment,
                                capture_output=True, timeout=10)
        return result, log.read_text().splitlines(), environment['HOME']

    def test_launcher_keeps_two_health_checks_updater_then_chromium_and_home(self):
        result, lines, home = self.launcher(updater_exit=7)
        self.assertEqual(result.returncode, 0, result.stderr)
        phases = [x.split()[0] for x in lines if x.split()[0] in ('health', 'updater', 'chromium')]
        self.assertEqual(phases, ['health', 'updater', 'health', 'chromium'])
        browser = next(x for x in lines if x.startswith('chromium'))
        self.assertIn('--user-data-dir=' + home + '/.config/initiativetracker-hub/chromium', browser)
        self.assertTrue(browser.endswith('http://127.0.0.1:5000/setup'))

    def test_launcher_health_failure_exits_before_updater_or_chromium(self):
        result, lines, _ = self.launcher(failing=True)
        self.assertEqual(result.returncode, 75)
        self.assertFalse(any(x.startswith(('updater', 'chromium')) for x in lines))
        self.assertEqual(lines.count('health'), 60)

    def test_runtime_manifest_matches_launcher(self):
        import hashlib
        manifest = json.loads((ROOT / 'hub-updater/runtime-manifest.json').read_text())
        self.assertEqual(manifest['sha256']['initiativetracker-hub-launch'], hashlib.sha256(
            (ROOT / 'hub-updater/runtime/initiativetracker-hub-launch').read_bytes()).hexdigest())

    def persistent_launcher(self, after_update_failure=False, slow_wait=False):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        scripts = {
            'zenity': '''#!/usr/bin/python3 -I
import os,sys,time,signal
from pathlib import Path
r=Path(os.environ['N9_FIXTURE'])
def log(value):
 with (r/'events').open('a') as f: f.write(value+'\\n')
if '--error' in sys.argv:
 log('error')
 raise SystemExit(0)
def stop(signum,frame):
 (r/'dialog-alive').unlink(missing_ok=True)
 log('dialog-exit')
 raise SystemExit(0)
signal.signal(signal.SIGTERM,stop)
(r/'dialog-alive').touch()
log('dialog-start')
while True: time.sleep(.05)
''',
            'curl': '''#!/usr/bin/python3 -I
import os,time
from pathlib import Path
r=Path(os.environ['N9_FIXTURE'])
deadline=time.monotonic()+3
while not (r/'dialog-alive').exists() and time.monotonic()<deadline: time.sleep(.01)
with (r/'events').open('a') as f: f.write('health\\n')
raise SystemExit(1 if os.environ.get('N9_SLOW_WAIT') or (os.environ.get('N9_FAIL_AFTER_UPDATE') and (r/'updated').exists()) else 0)
''',
            'checker': '''#!/usr/bin/python3 -I
import os
from pathlib import Path
r=Path(os.environ['N9_FIXTURE'])
assert not (r/'dialog-alive').exists(), 'loading dialog overlaps graphical updater'
(r/'updated').touch()
with (r/'events').open('a') as f: f.write('updater\\n')
''',
            'chromium': '''#!/usr/bin/python3 -I
import os
from pathlib import Path
r=Path(os.environ['N9_FIXTURE'])
assert not (r/'dialog-alive').exists(), 'loading dialog overlaps browser'
with (r/'events').open('a') as f: f.write('chromium\\n')
''',
            'sleep': '#!/bin/sh\nif [ -n "${N9_SLOW_WAIT:-}" ]; then /bin/sleep .05; fi\n'}
        for name, content in scripts.items():
            path = root / name
            path.write_text(content)
            path.chmod(0o755)
        text = (ROOT / 'hub-updater/runtime/initiativetracker-hub-launch').read_text()
        text = text.replace('/usr/local/bin/initiativetracker-check-update', str(root / 'checker'))
        text = text.replace('/usr/bin/chromium', str(root / 'chromium'))
        launch = root / 'launch'
        launch.write_text(text)
        environment = dict(os.environ, HOME=str(root / 'selected-desktop-home'),
                           PATH=str(root) + ':/usr/bin:/bin', N9_FIXTURE=str(root))
        if after_update_failure:
            environment['N9_FAIL_AFTER_UPDATE'] = '1'
        if slow_wait:
            environment['N9_SLOW_WAIT'] = '1'
        return root, launch, environment

    def test_persistent_dialogs_exit_before_updater_and_browser(self):
        root, launch, environment = self.persistent_launcher()
        result = subprocess.run(['/bin/bash', '-p', str(launch)], env=environment, capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((root / 'events').read_text().splitlines(),
                         ['dialog-start', 'health', 'dialog-exit', 'updater',
                          'dialog-start', 'health', 'dialog-exit', 'chromium'])
        self.assertFalse((root / 'dialog-alive').exists())

    def test_post_updater_health_failure_closes_dialog_and_never_opens_browser(self):
        root, launch, environment = self.persistent_launcher(after_update_failure=True)
        result = subprocess.run(['/bin/bash', '-p', str(launch)], env=environment, capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 75, result.stderr)
        events = (root / 'events').read_text().splitlines()
        self.assertEqual(events.count('updater'), 1)
        self.assertEqual(events.count('dialog-exit'), 2)
        self.assertEqual(events.count('health'), 61)
        self.assertNotIn('chromium', events)
        self.assertFalse((root / 'dialog-alive').exists())

    def test_signal_while_hub_is_waiting_reaps_persistent_dialog(self):
        root, launch, environment = self.persistent_launcher(slow_wait=True)
        process = subprocess.Popen(['/bin/bash', '-p', str(launch)], env=environment,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
        try:
            deadline = time.monotonic() + 4
            while not (root / 'dialog-alive').exists() and time.monotonic() < deadline:
                time.sleep(.01)
            self.assertTrue((root / 'dialog-alive').exists())
            process.send_signal(signal.SIGTERM)
            process.communicate(timeout=5)
            self.assertEqual(process.returncode, 130)
            self.assertFalse((root / 'dialog-alive').exists())
            events = (root / 'events').read_text().splitlines()
            self.assertIn('dialog-exit', events)
            self.assertNotIn('updater', events)
            self.assertNotIn('chromium', events)
        finally:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.communicate()

    def supervisor(self, shutdown_waiting=False):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        home = Path(temp.name)
        (home / '.local/state/initiativetracker-satellite').mkdir(parents=True)
        profile = home / '.local/share/initiativetracker-satellite/chromium'
        profile.mkdir(parents=True)
        (profile / 'Cookies').write_bytes(b'paired fixture unchanged')
        settings = home / 'kiosk.json'
        settings.write_text(json.dumps({'role': 'satellite', 'hub_url': 'https://hub.example', 'hub_id': 'fixture'}))
        events, handlers = [], {}
        wait = Mock(pid=101)
        wait.poll.return_value = None
        wait.wait.side_effect = lambda **kwargs: events.append('waiting-exit')
        display = Mock(pid=202)
        display.poll.return_value = None
        def display_wait(timeout=None):
            if timeout is not None:
                events.append('paired-exit')
            else:
                handlers[signal.SIGTERM](signal.SIGTERM, None)
        display.wait.side_effect = display_wait
        def spawn(args, **kwargs):
            if args[-1].startswith('file:'):
                events.append('waiting-start')
                return wait
            self.assertEqual(args[-1], 'https://hub.example/display')
            self.assertIn('--user-data-dir=' + str(profile), args)
            events.append('paired-start')
            return display
        def sleep(seconds):
            if shutdown_waiting:
                handlers[signal.SIGTERM](signal.SIGTERM, None)
        with patch.object(kiosk.Path, 'home', return_value=home), \
                patch.object(kiosk.sys, 'argv', ['kiosk.py', str(settings)]), \
                patch.object(kiosk.signal, 'signal', side_effect=lambda sig, fn: handlers.update({sig: fn})), \
                patch.object(kiosk, 'reachable', side_effect=[False, True]), \
                patch.object(kiosk.subprocess, 'Popen', side_effect=spawn), \
                patch.object(kiosk.os, 'killpg', side_effect=lambda pid, sig: events.append(f'terminate-{pid}')), \
                patch.object(kiosk.time, 'sleep', side_effect=sleep):
            with self.assertRaises(SystemExit):
                kiosk.main()
        self.assertEqual((profile / 'Cookies').read_bytes(), b'paired fixture unchanged')
        return events

    def test_supervisor_reaps_waiting_browser_before_paired_profile_and_shutdown(self):
        self.assertEqual(self.supervisor(), ['waiting-start', 'terminate-101', 'waiting-exit',
                                             'paired-start', 'terminate-202', 'paired-exit'])

    def test_supervisor_shutdown_while_waiting_reaps_browser_without_paired_launch(self):
        self.assertEqual(self.supervisor(shutdown_waiting=True),
                         ['waiting-start', 'terminate-101', 'waiting-exit'])


if __name__ == '__main__':
    unittest.main()
