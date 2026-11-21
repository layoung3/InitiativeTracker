#!/usr/bin/python3 -I
"""Labwc-owned kiosk supervisor. Preserves hub updater-before-browser ordering."""
import fcntl
import ipaddress
import json
import os
import re
import socket
from pathlib import Path
import signal
import subprocess
import sys
import time
import urllib.request
from urllib.parse import urlsplit


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


def local_hub_reachable():
    """Hub kiosk uses the fixed loopback HTTP health endpoint, not satellite config."""
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open('http://127.0.0.1:5000/system/health', timeout=4) as response:
            result = json.load(response)
            return result.get('status') == 'healthy' and result.get('product') == 'InitiativeTracker'
    except (OSError, ValueError, KeyError):
        return False



def verified_hub_https(settings):
    """Optional secure kiosk URL; no private DNS, insecure TLS, or HTTP authority.

    The local operator must separately provision trusted HTTPS, loopback-only
    hub DNS, and this explicit kiosk setting. A valid remote DNS answer must
    never be treated as physical proof merely because TLS succeeds.
    """
    origin = settings.get('hub_https_origin')
    if not isinstance(origin, str) or len(origin) > 265:
        return None
    try:
        parsed = urlsplit(origin)
        host = parsed.hostname
        if (parsed.scheme != 'https' or parsed.username or parsed.password or
            parsed.path or parsed.query or parsed.fragment or parsed.port not in (None, 443) or
            not host or origin != 'https://' + host or
            host != host.lower() or
            not re.fullmatch(r'[a-z0-9](?:[a-z0-9.-]{0,251}[a-z0-9])?', host) or
            len(host.split('.')) < 3 or host.split('.')[-1] in
            {'local', 'localhost', 'test', 'invalid', 'example', 'internal', 'lan', 'home', 'arpa', 'onion'} or
            host.split('.')[-1].isdigit()):
            return None
        answers = socket.getaddrinfo(host, 443, type=socket.SOCK_STREAM)
        if not answers or any(not ipaddress.ip_address(item[4][0]).is_loopback for item in answers):
            return None
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(origin + '/system/health', timeout=4) as response:
            result = json.load(response)
            if result.get('status') != 'healthy' or result.get('product') != 'InitiativeTracker':
                return None
        return origin + '/setup'
    except (OSError, ValueError, KeyError, TypeError):
        return None


def hub_target(settings):
    return verified_hub_https(settings) or 'http://127.0.0.1:5000/setup'


def command(settings, home):
    if settings['role'] == 'hub':
        return [str(home / '.local/lib/initiativetracker-hub/initiativetracker-hub-launch')]
    return ['/usr/bin/chromium', '--kiosk', '--no-first-run', '--no-default-browser-check',
            '--password-store=basic', '--disable-session-crashed-bubble',
            '--user-data-dir=' + str(home / '.local/share/initiativetracker-satellite/chromium'),
            settings['hub_url'] + '/display']


def hub_resume_command(home, settings=None):
    """Reopen the existing hub profile after a service outage, no updater check.

    The ordinary launcher is still used for first startup. Recovery must not
    present an update prompt or change the installed application on its own.
    """
    return ['/usr/bin/chromium', '--kiosk', '--start-maximized', '--noerrdialogs',
            '--disable-infobars', '--no-first-run', '--disable-session-crashed-bubble',
            '--disable-features=TranslateUI', '--disable-pinch',
            '--overscroll-history-navigation=0', '--password-store=basic',
            '--user-data-dir=' + str(home / '.config/initiativetracker-hub/chromium'),
            hub_target(settings or {})]


def watch_hub_child(child, settings):
    """Return True only after a sustained hub outage and healthy recovery.

    Short failures cannot disrupt a live game. This runs outside the browser,
    so a Chromium network-error page cannot strand the physical touchscreen.
    """
    offline = 0
    needs_recovery = False
    while child.poll() is None:
        time.sleep(5)
        if child.poll() is not None:
            return False
        if local_hub_reachable():
            if needs_recovery:
                return True
            offline = 0
        else:
            offline += 1
            if offline >= 3:
                needs_recovery = True
    return False


def waiting_command(home):
    """Local, inert page; separate profile cannot change pairing cookies."""
    local = home / '.local/lib/initiativetracker-satellite/waiting.html'
    return ['/usr/bin/chromium', '--kiosk', '--no-first-run', '--no-default-browser-check',
            '--password-store=basic', '--disable-session-crashed-bubble',
            '--user-data-dir=' + str(home / '.local/state/initiativetracker-satellite/waiting-chromium'),
            local.as_uri()]


def stop_child(child):
    if child and child.poll() is None:
        try:
            os.killpg(child.pid, signal.SIGTERM)
            child.wait(timeout=5)
        except ProcessLookupError:
            pass
        except subprocess.TimeoutExpired:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait()


def wait_for_satellite_hub(settings, home):
    """Probe outside Chromium; no cert bypass, cached success or HTTP fallback."""
    waiting = None
    try:
        while not reachable(settings):
            if waiting is None or waiting.poll() is not None:
                waiting = subprocess.Popen(waiting_command(home), start_new_session=True,
                                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            time.sleep(5)
    finally:
        stop_child(waiting)


def main():
    os.umask(0o077)
    settings = json.loads(Path(sys.argv[1]).read_text())
    home = Path.home()
    state = home / '.local/state' / ('initiativetracker-' + settings['role'])
    with (state / 'kiosk.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return 0
        return supervise(settings, home)


def supervise(settings, home):
    child = None

    def stop(signum, frame):
        stop_child(child)
        raise SystemExit(0)

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    if settings.get('display_output'):
        subprocess.run(['/usr/bin/wlr-randr', '--output', settings['display_output'],
                        '--mode', settings['display_mode']], check=False,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    recovering = False
    while True:
        if settings['role'] == 'satellite':
            wait_for_satellite_hub(settings, home)
        # Child output is intentionally not accumulated into an unbounded log.
        # The root updater's diagnostic journal remains available locally.
        # The first launch retains updater-before-Chromium ordering. A recovery
        # launch skips the updater and uses the identical persistent hub profile.
        args = hub_resume_command(home, settings) if settings['role'] == 'hub' and recovering else command(settings, home)
        child = subprocess.Popen(args, start_new_session=True,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if settings['role'] == 'hub':
            restored = watch_hub_child(child, settings)
            if restored:
                stop_child(child)
                recovering = True
            else:
                child.wait()
        else:
            child.wait()
        time.sleep(5)


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--hub-target':
        try:
            chosen = json.loads(Path(sys.argv[2]).read_text())
            if chosen.get('role') != 'hub':
                raise ValueError('Not a hub configuration')
            print(hub_target(chosen))
        except (OSError, ValueError, KeyError, TypeError):
            print('http://127.0.0.1:5000/setup')
    else:
        sys.exit(main())
