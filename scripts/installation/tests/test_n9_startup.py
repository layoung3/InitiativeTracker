"""Disposable process mocks: never runs system Chromium, an updater or a Pi."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
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


if __name__ == '__main__':
    unittest.main()
