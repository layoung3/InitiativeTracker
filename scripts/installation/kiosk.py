#!/usr/bin/python3 -I
"""Labwc-owned kiosk supervisor. Preserves hub updater-before-browser ordering."""
import fcntl
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import urllib.request


def reachable(settings):
    try:
        base = settings['hub_url']
        # A local/LAN hub must not require an environment HTTP proxy.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(base + '/system/health', timeout=5) as response:
            if json.load(response).get('status') != 'healthy':
                return False
        with opener.open(base + '/system/hub-info', timeout=5) as response:
            return json.load(response).get('hubId', '').lower() == settings['hub_id']
    except (OSError, ValueError, KeyError):
        return False


def command(settings, home):
    if settings['role'] == 'hub':
        return [str(home / '.local/lib/initiativetracker-hub/initiativetracker-hub-launch')]
    return ['/usr/bin/chromium', '--kiosk', '--no-first-run', '--no-default-browser-check',
            '--password-store=basic', '--disable-session-crashed-bubble',
            '--user-data-dir=' + str(home / '.local/share/initiativetracker-satellite/chromium'),
            settings['hub_url'] + '/display']


def main():
    os.umask(0o077)
    settings = json.loads(Path(sys.argv[1]).read_text())
    home = Path.home()
    state = home / '.local/state' / ('initiativetracker-' + settings['role'])
    lock = (state / 'kiosk.lock').open('a')
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        return 0
    child = None

    def stop(signum, frame):
        if child and child.poll() is None:
            os.killpg(child.pid, signal.SIGTERM)
        raise SystemExit(0)

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    if settings.get('display_output'):
        subprocess.run(['/usr/bin/wlr-randr', '--output', settings['display_output'],
                        '--mode', settings['display_mode']], check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    while True:
        if settings['role'] == 'satellite' and not reachable(settings):
            time.sleep(5)
            continue
        # Child output is intentionally not accumulated into an unbounded log.
        # The root updater's diagnostic journal remains available locally.
        child = subprocess.Popen(command(settings, home), start_new_session=True,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        child.wait()
        time.sleep(5)


if __name__ == '__main__':
    sys.exit(main())
