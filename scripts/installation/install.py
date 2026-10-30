#!/usr/bin/python3 -I
"""Fresh-device commissioning. No live-device repair, update, or network switch.

The CLI intentionally has no alternate-root, noninteractive apply, or force option.
Tests inject a disposable Host and never execute operating-system commands.
"""
import argparse
import fcntl
import getpass
import hashlib
import importlib.machinery
import importlib.util
import io
import json
import os
from pathlib import Path
import pwd
import re
import secrets
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.parse
import uuid

REPO = Path(__file__).resolve().parents[2]
STATE = '/var/lib/initiativetracker-install'
NETWORK = '/usr/local/libexec/initiativetracker'
APP = '/opt/initiativetracker'
DATA = '/var/lib/initiativetracker'
ETC = '/etc/initiativetracker'
ORIGINAL_HASHES = {
    'initiativetracker-install-update': '17bfda06c20f3d8b932d7f7f1bf341ed17107e32c602586f9680e463d9542c56',
    'initiativetracker-check-update': '16e57846ace608c5e81d394107612d7d34100e82c06ef23e39b3aa409d607d4d',
    'initiativetracker-hub-launch': '4bd743792f68b6041fb80e5c83cb377231bce743d5194115db146bbcb8b2c44a',
}
PACKAGES = ['curl', 'ca-certificates', 'python3', 'avahi-daemon', 'avahi-utils',
            'libnss-mdns', 'network-manager', 'chromium', 'util-linux']
NATIVE = {'12': ['libicu72', 'libssl3'], '13': ['libicu76', 'libssl3t64']}


class InstallError(Exception):
    pass


def digest(path):
    with path.open('rb') as stream:
        result = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(chunk)
        return result.hexdigest()


def load_helper(name):
    loader = importlib.machinery.SourceFileLoader(name, str(REPO / 'pi-network' / name))
    spec = importlib.util.spec_from_loader(name, loader)
    module = importlib.util.module_from_spec(spec)
    loader.exec_module(module)
    return module


def validate_url(value):
    url = urllib.parse.urlsplit(value)
    if (url.scheme not in ('http', 'https') or not url.hostname or
            url.username is not None or url.password is not None or
            url.query or url.fragment or url.path not in ('', '/') or
            any(ord(c) < 33 for c in value)):
        raise InstallError('Hub address must be an HTTP(S) origin without credentials, path, token, or query')
    try:
        url.port
    except ValueError as exc:
        raise InstallError('Invalid hub port') from exc
    return value.rstrip('/')


def confirm(message, token):
    print(message)
    if input(f'Type {token} to continue: ').strip() != token:
        raise InstallError('Cancelled; requested operation was not performed')


def ask(label, default=''):
    answer = input(f'{label}' + (f' [{default}]' if default else '') + ': ').strip()
    return answer or default


def read_password(label):
    # getpass otherwise falls back to echoed stdin on a pipe. Never permit that.
    if not sys.stdin.isatty():
        raise InstallError('A terminal is required for secure password entry')
    first = getpass.getpass(label + ': ')
    if first != getpass.getpass('Repeat password: '):
        raise InstallError('Passwords did not match')
    return first


class Host:
    def __init__(self, root=Path('/')):
        self.root = root
        self.backup = None
        self.owned = []

    def path(self, path):
        if not str(path).startswith('/') or '..' in Path(path).parts:
            raise InstallError('Expected an absolute system path')
        return self.root / str(path).lstrip('/')

    def safe(self, path):
        target = self.path(path)
        for parent in (target, *target.parents):
            if parent == self.root.parent:
                break
            if parent.is_symlink():
                raise InstallError(f'Refusing symlink in managed path: {path}')
        return target

    def run(self, *args, input=None, env=None):
        # Do not echo command output: nmcli and external tools may disclose local secrets.
        try:
            result = subprocess.run(args, input=input, text=True, capture_output=True,
                                    timeout=900, env=env, check=False)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise InstallError(f'{Path(args[0]).name} could not complete; inspect locally') from exc
        if result.returncode:
            raise InstallError(f'{Path(args[0]).name} failed (exit {result.returncode}); inspect locally')
        return result.stdout.strip()

    def mkdir(self, path, mode=0o755, owner=None):
        target = self.safe(path)
        if target.exists():
            if not target.is_dir():
                raise InstallError(f'Expected directory: {path}')
            return
        if not target.parent.exists():
            self.mkdir('/' + str(target.parent.relative_to(self.root)), mode, owner)
        target.mkdir(mode=mode)
        target.chmod(mode)
        if owner:
            os.chown(target, *owner)

    def write(self, path, content, mode=0o644, owner=None, replace=False):
        target = self.safe(path)
        if target.exists():
            if not replace:
                raise InstallError(f'Existing configuration preserved: {path}')
            if not target.is_file() or self.backup is None:
                raise InstallError(f'Cannot back up existing file: {path}')
            backup = self.safe(self.backup + path)
            backup.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
            shutil.copy2(target, backup)
            backup.chmod(0o600)
        target.parent.mkdir(parents=True, exist_ok=True)
        # A private temporary followed by atomic rename prevents partially written secrets.
        fd, temporary = tempfile.mkstemp(prefix='.it-install-', dir=target.parent)
        try:
            with os.fdopen(fd, 'wb') as stream:
                stream.write(content.encode() if isinstance(content, str) else content)
                stream.flush()
                os.fsync(stream.fileno())
                os.fchmod(stream.fileno(), mode)
                if owner:
                    os.fchown(stream.fileno(), *owner)
            if not replace and target.exists():
                raise InstallError(f'File appeared during installation: {path}')
            os.replace(temporary, target)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
        self.owned.append(path)

    def copy(self, source, target, mode=0o644, owner=None):
        self.write(target, source.read_bytes(), mode, owner)


def validate_release(manifest, package):
    try:
        data = json.loads(manifest.read_text(encoding='utf-8-sig'))
        p = data['package']
        if (data['formatVersion'] != 1 or data['product'] != 'InitiativeTracker' or
                data['platform'] != 'linux-arm64' or data['healthPath'] != '/system/health' or
                not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?', data['version']) or
                not re.fullmatch(r'[A-Za-z0-9_.+-]+\.tar\.gz', p['fileName']) or
                '..' in p['fileName'] or package.name != p['fileName'] or
                package.stat().st_size != p['sizeBytes'] or
                digest(package) != p['sha256'].lower()):
            raise ValueError()
        if not hasattr(tarfile, 'data_filter'):
            raise InstallError('Python tarfile.data_filter support is required (install OS security updates)')
        names = set()
        with tarfile.open(package, 'r:gz') as archive:
            for member in archive:
                name = member.name.removeprefix('./').rstrip('/')
                if name in ('', '.'):
                    continue
                if (name.startswith('/') or '..' in Path(name).parts or ':' in name or
                        not (member.isfile() or member.isdir()) or name in names):
                    raise InstallError('Unsafe or duplicate archive member')
                names.add(name)
            if 'InitiativeTracker.Web' not in names:
                raise InstallError('Missing application executable at archive root')
            metadata = next((m for m in archive.getmembers() if m.name.removeprefix('./') == 'InitiativeTracker.Web.runtimeconfig.json'), None)
            if metadata is None or metadata.size > 65536:
                raise InstallError('Missing/oversized publish runtime metadata')
            runtime = json.load(archive.extractfile(metadata))['runtimeOptions']
            included = runtime.get('includedFrameworks', [])
            if runtime.get('tfm') != 'net10.0' or not any(f.get('name') == 'Microsoft.NETCore.App' and f.get('version', '').startswith('10.') for f in included):
                raise InstallError('Expected established self-contained .NET 10 ARM64 release; review other publish formats separately')
        return data
    except (OSError, ValueError, KeyError, TypeError, tarfile.TarError) as exc:
        raise InstallError('Invalid release: inspect trusted manifest, size, SHA-256, platform, and archive') from exc


def verify_updater_sources():
    for name, expected in ORIGINAL_HASHES.items():
        path = REPO / 'hub-updater' / 'original' / name
        if not path.is_file() or path.is_symlink() or digest(path) != expected:
            raise InstallError('Approved updater sources unavailable or mismatched; recover the verified source handoff before hub installation')
    recipe = REPO / 'hub-updater' / 'provision.py'
    if not recipe.is_file():
        raise InstallError('Approved updater provisioning/hardening is not yet available; hub apply is blocked')


def preflight(host, role, desktop=None):
    os_release = dict(line.split('=', 1) for line in host.path('/etc/os-release').read_text().splitlines()
                      if '=' in line and not line.startswith('#'))
    version = os_release.get('VERSION_ID', '').strip('"')
    model = host.path('/proc/device-tree/model')
    if (version not in ('12', '13') or os_release.get('ID', '').strip('"') not in ('debian', 'raspbian') or
            not model.exists() or 'Raspberry Pi' not in model.read_text()):
        raise InstallError('Requires Raspberry Pi OS desktop 64-bit on Debian 12 or 13')
    if host.run('dpkg', '--print-architecture') != 'arm64' or host.run('uname', '-m') != 'aarch64':
        raise InstallError('Requires ARM64 (aarch64); no host installation was attempted')
    for path in ('/usr/bin/labwc', '/usr/sbin/lightdm', '/run/systemd/system', '/sys/class/net/wlan0'):
        if not host.path(path).exists():
            raise InstallError(f'Missing desktop/OS prerequisite: {path}; use Raspberry Pi OS desktop with Labwc')
    host.run('systemctl', 'is-active', 'NetworkManager.service')
    reserved = {'IT-External-1'} | ({'InitiativeTracker-Recovery'} if role == 'hub' else {'IT-Hub-Private'})
    if reserved.intersection(host.run('nmcli', '-g', 'NAME', 'connection', 'show').splitlines()):
        raise InstallError('A reserved InitiativeTracker network profile name already exists; preserved without changes')
    if role == 'hub':
        active_profile = host.run('nmcli', '--escape', 'no', '-g', 'GENERAL.CONNECTION', 'device', 'show', 'wlan0')
        if active_profile not in ('', '--') and not re.fullmatch(r'[A-Za-z0-9_.:-]{1,80}', active_profile):
            raise InstallError('Active wlan0 profile ID is incompatible with supervised DM switches (requires [A-Za-z0-9_.:-]{1,80}). Connection preserved; review its UUID and explicitly rename the profile to a unique simple ID such as IT-Imager-External during local commissioning, then rerun preflight. No profile was renamed or switched.')
    if host.run('nmcli', '-g', 'GENERAL.TYPE', 'device', 'show', 'wlan0') != 'wifi':
        raise InstallError('The existing hub controllers require the wlan0 Wi-Fi interface')
    if role == 'hub':
        phy = host.run('iw', 'dev', 'wlan0', 'info')
        match = re.search(r'wiphy\s+(\d+)', phy)
        if not match or not re.search(r'^\s*\* AP\s*$', host.run('iw', 'phy', 'phy' + match[1], 'info'), re.M):
            raise InstallError('wlan0 must report AP support')
    if desktop:
        if desktop.pw_uid < 1000 or desktop.pw_dir == '/root' or not Path(desktop.pw_dir).is_absolute():
            raise InstallError('Select an existing ordinary desktop account, not root or the app account')
        home = host.safe(desktop.pw_dir)
        if (not home.is_dir() or home.stat().st_uid != desktop.pw_uid or
                getattr(desktop, 'pw_shell', '/bin/bash') in ('/usr/sbin/nologin', '/sbin/nologin', '/bin/false')):
            raise InstallError('Desktop account must have its own existing home and a login shell')
    sessions = list(host.path('/usr/share/wayland-sessions').glob('*labwc*.desktop'))
    if len(sessions) != 1:
        raise InstallError('Requires exactly one installed Labwc .desktop session; review OS desktop configuration locally')
    for managed in (STATE, ETC, APP, DATA, NETWORK, '/var/backups/initiativetracker',
                    '/var/cache/initiativetracker-updater', '/etc/NetworkManager/system-connections',
                    '/etc/systemd/system', '/etc/sudoers.d', '/etc/lightdm/lightdm.conf'):
        for parent in (host.safe(managed), *host.safe(managed).parents):
            if parent == host.root.parent:
                break
            if parent.exists():
                info = parent.stat()
                if info.st_uid != 0 or info.st_mode & 0o022:
                    raise InstallError('System installation directories must be root-owned and not group/world writable')
    if shutil.disk_usage(host.path('/')).free < 2 * 1024**3:
        raise InstallError('At least 2 GiB free storage is required for staging and rollback')
    return version


def existing(host, role, desktop):
    receipt = host.safe(STATE + '/receipt.json')
    if receipt.exists():
        data = json.loads(receipt.read_text())
        if data.get('role') != role or data.get('desktop_user') != desktop.pw_name:
            raise InstallError('Existing installation has another role/account; preserved without changes')
        if data.get('status') != 'complete':
            raise InstallError('Interrupted installation preserved. Review receipt and backups locally; do not reinstall over its data')
        print('Already commissioned; configuration, releases, database and browser profile preserved. Use the graphical updater for hub releases.')
        return True
    targets = [ETC + '/installation.json', '/etc/lightdm/lightdm.conf.d/90-initiativetracker.conf',
               '/etc/NetworkManager/system-connections/IT-External-1.nmconnection']
    if role == 'hub':
        targets += [APP, DATA, ETC + '/updater.conf', ETC + '/appsettings.Production.json',
                    desktop.pw_dir + '/.config/initiativetracker-hub',
                    '/etc/NetworkManager/system-connections/InitiativeTracker-Recovery.nmconnection',
                    '/etc/initiativetracker/network/recovery-wifi.password',
                    '/etc/systemd/system/initiativetracker.service',
                    '/etc/sudoers.d/initiativetracker-update',
                    '/usr/local/sbin/initiativetracker-install-update',
                    '/usr/local/bin/initiativetracker-check-update', NETWORK]
    else:
        targets += [desktop.pw_dir + '/.config/initiativetracker-satellite',
                    desktop.pw_dir + '/.local/share/initiativetracker-satellite',
                    '/etc/NetworkManager/system-connections/IT-Hub-Private.nmconnection']
    targets += [desktop.pw_dir + '/.local/lib/initiativetracker-' + role,
                desktop.pw_dir + '/.local/state/initiativetracker-' + role]
    if role == 'hub':
        targets += [str('/etc/systemd/system/' + x.name) for x in (REPO / 'pi-network/systemd').iterdir()
                    if x.suffix in ('.service', '.timer', '.socket')]
        targets += [ETC + '/network', '/var/lib/initiativetracker-network']
    autostart = host.safe(desktop.pw_dir + '/.config/labwc/autostart')
    if autostart.exists() and 'initiativetracker-' in autostart.read_text():
        raise InstallError('Existing InitiativeTracker desktop autostart preserved')
    for target in targets:
        path = host.safe(target)
        if path.exists():
            raise InstallError(f'Existing installation/configuration preserved: {target}. Use documented maintenance instead')
    return False


def wifi_request(label):
    if ask(label + '? (yes/no)', 'no') != 'yes':
        return None
    result = {'ssid': ask('Wi-Fi SSID'), 'password': read_password('Wi-Fi WPA password')}
    try:
        load_helper('initiativetracker-wifi-provision').validate(result)
    except ValueError as exc:
        raise InstallError('SSID or WPA password format is invalid; no credentials saved') from exc
    return result


def wizard(role, desktop, args):
    settings = {'role': role, 'desktop_user': desktop.pw_name}
    settings['hostname'] = ask('Commissioning hostname (hub later uses its persisted friendly hostname)', args.hostname or '')
    if not re.fullmatch(r'[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?', settings['hostname']):
        raise InstallError('Hostname must be a single lowercase DNS label')
    settings['external_wifi'] = wifi_request('Save an additional external Wi-Fi profile for autoconnect on a disconnected radio')
    settings['autologin'] = ask('Enable LightDM Labwc desktop autologin? (yes/no)', 'yes') == 'yes'
    settings['system_runtime'] = False
    settings['display_output'] = ask('Optional wlr-randr output name (blank preserves desktop display settings)')
    settings['display_mode'] = ''
    if settings['display_output']:
        if not re.fullmatch(r'[A-Za-z0-9_.-]{1,64}', settings['display_output']):
            raise InstallError('Invalid display output')
        settings['display_mode'] = ask('Display mode', '1280x800')
        if not re.fullmatch(r'\d{3,5}x\d{3,5}(?:@\d{1,3}(?:\.\d+)?Hz)?', settings['display_mode']):
            raise InstallError('Invalid display mode')
    if role == 'hub':
        print('The established release bundles .NET 10. Native OS libraries are installed; no SDK is needed.')
        settings['system_runtime'] = ask('Also install optional system ASP.NET Core 10 ARM64 runtime? (yes/no)', 'no') == 'yes'
        settings['hotspot_ssid'] = ask('Private hotspot SSID (blank derives from new hub identity)')
        if settings['hotspot_ssid'] and not re.fullmatch(r'[A-Za-z0-9_.-]{1,32}', settings['hotspot_ssid']):
            raise InstallError('Hotspot SSID requires 1–32 ASCII letters, numbers, _, . or -')
        print('A unique cryptographically random hotspot password will be stored root-only; view it privately on local Setup.')
        settings['hotspot_password'] = None
        if ask('Choose initial hotspot password instead? (yes/no)', 'no') == 'yes':
            password = read_password('Initial hotspot WPA password')
            if not re.fullmatch(r'[A-Za-z0-9]{8,63}', password):
                raise InstallError('Initial hotspot password must contain 8–63 ASCII letters/numbers')
            settings['hotspot_password'] = password
        settings['network_gates'] = True
        confirm('Network recovery gates allow supervised DM switches and disconnected-boot hotspot fallback after at least 60 seconds plus discovery/attempts. This can disconnect an operator on a previously disconnected radio. Keep Ethernet or local console recovery.', 'ENABLE-RECOVERY')
    else:
        settings['hub_url'] = validate_url(ask('Hub base URL', args.hub_url or ''))
        try:
            settings['hub_id'] = str(uuid.UUID(ask('Expected hub GUID from /system/hub-info', args.hub_id or '')))
        except ValueError as exc:
            raise InstallError('Expected hub identity must be a GUID') from exc
        settings['hub_wifi'] = wifi_request("Save hub's private Wi-Fi credentials for autoconnect on a disconnected radio")
        print('Satellite Wi-Fi following is not implemented. Choose the hub network locally when it changes.')
    return settings


def stage_wifi(host, credentials, profile_id, priority):
    if not credentials:
        return
    uid = str(uuid.uuid4())
    keyfile = load_helper('initiativetracker-wifi-provision').create_keyfile(credentials['ssid'], credentials['password'], uid)
    # GLib keyfiles interpret backslash escapes and trim unescaped leading
    # spaces. Encode those characters so the actual credential round-trips.
    for section, field, value in (('wifi', 'ssid', credentials['ssid']),
                                   ('wifi-security', 'psk', credentials['password'])):
        keyfile[section][field] = value.replace('\\', '\\\\').replace(' ', '\\s')
    keyfile['connection'].update({'id': profile_id, 'interface-name': 'wlan0',
                                 'autoconnect': 'true', 'autoconnect-priority': str(priority)})
    text = io.StringIO()
    keyfile.write(text)
    target = '/etc/NetworkManager/system-connections/' + profile_id + '.nmconnection'
    host.write(target, text.getvalue(), 0o600)
    # Load only; never up/reload/restart NetworkManager. Autoconnect can act on a
    # disconnected radio, but never preempts an already active Wi-Fi connection.
    host.run('nmcli', 'connection', 'load', target)


def packages(host, role, version, system_runtime=False):
    print('Installing distribution prerequisites; package maintainer scripts may restart services.')
    host.run('apt-get', 'update')
    requested = PACKAGES + ['wlr-randr']
    if role == 'hub':
        requested += ['dnsmasq-base', 'nftables', 'iw', 'sqlite3', 'zenity', 'sudo',
                      'libc6', 'libgcc-s1', 'libstdc++6', 'zlib1g', 'libgssapi-krb5-2', *NATIVE[version]]
    host.run('apt-get', 'install', '-y', *requested, env={**os.environ, 'DEBIAN_FRONTEND': 'noninteractive', 'NEEDRESTART_MODE': 'l'})
    if role == 'hub' and system_runtime:
        deb = STATE + '/packages-microsoft-prod.deb'
        if not host.path('/etc/apt/sources.list.d/microsoft-prod.list').exists():
            host.run('curl', '--fail', '--location', '--proto', '=https', '--tlsv1.2',
                     f'https://packages.microsoft.com/config/debian/{version}/packages-microsoft-prod.deb', '--output', deb)
            host.run('dpkg', '-i', deb)
        host.run('apt-get', 'update')
        host.run('apt-get', 'install', '-y', 'aspnetcore-runtime-10.0')
        runtimes = host.run('dotnet', '--list-runtimes')
        if not all(name + ' 10.' in runtimes for name in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')):
            raise InstallError('.NET 10 ASP.NET Core runtime verification failed')


def install_network(host, ssid, password):
    host.mkdir(NETWORK)
    host.mkdir('/var/lib/initiativetracker-network', 0o700)
    host.mkdir(ETC + '/network', 0o700)
    for source in sorted((REPO / 'pi-network').glob('initiativetracker-*')):
        host.copy(source, NETWORK + '/' + source.name, 0o755)
    for source in sorted((REPO / 'pi-network/systemd').iterdir()):
        if source.suffix in ('.service', '.timer', '.socket'):
            host.copy(source, '/etc/systemd/system/' + source.name)
    helper = load_helper('initiativetracker-stage-recovery-hotspot')
    generated = secrets.token_hex(12)
    profile = helper.make_profile('wlan0', ssid, generated, str(uuid.uuid4()))
    if password:
        profile = profile.replace('psk=' + generated, 'psk=' + password)
    else:
        password = generated
    host.write(ETC + '/network/recovery-wifi.password', password + '\n', 0o600)
    target = '/etc/NetworkManager/system-connections/InitiativeTracker-Recovery.nmconnection'
    host.write(target, profile, 0o600)
    host.run('nmcli', 'connection', 'load', target)
    host.write('/etc/systemd/system/initiativetracker.service.d/network-commands.conf',
               (REPO / 'scripts/installation/templates/network-commands.conf').read_text())
    host.run('systemctl', 'daemon-reload')
    # Independent recovery must be active before authorization gates are created.
    for unit in ('network-recovery.timer', 'network-return.timer', 'network-observer.timer', 'network-bridge.socket'):
        host.run('systemctl', 'enable', '--now', 'initiativetracker-' + unit)
        host.run('systemctl', 'is-active', 'initiativetracker-' + unit)
    for name, value in (('live-switch', 'ENABLE_N64C_LIVE'), ('return-switch', 'ENABLE_RETURN_SWITCH'), ('boot-fallback', 'ENABLE_BOOT_FALLBACK')):
        host.write(ETC + '/network/' + name + '.enabled', value + '=yes\n', 0o600)
    host.run('systemctl', 'enable', 'initiativetracker-prepare-startup.service')
    # Defer boot fallback timer until next boot: commissioning never changes Wi-Fi.
    host.run('systemctl', 'enable', 'initiativetracker-boot-fallback.timer')


def wait_health(host, url, hub_id=None, attempts=60, expected_version=None):
    for _ in range(attempts):
        try:
            health = json.loads(host.run('curl', '--fail', '--silent', '--connect-timeout', '2', '--max-time', '4', url + '/system/health'))
            if health.get('status') != 'healthy':
                raise InstallError('Hub reports unhealthy status')
            if expected_version and health.get('version') != expected_version:
                raise InstallError('Unexpected running application version')
            info = json.loads(host.run('curl', '--fail', '--silent', '--connect-timeout', '2', '--max-time', '4', url + '/system/hub-info'))
            if hub_id and str(uuid.UUID(info['hubId'])) != hub_id:
                raise InstallError('Reachable hub has a different identity')
            return info
        except (InstallError, ValueError, KeyError):
            time.sleep(2)
    raise InstallError('Health/identity deadline exceeded; installation retained for inspection')


def first_release(host, manifest, package):
    data = validate_release(manifest, package)
    host.run('useradd', '--system', '--user-group', '--home', DATA, '--shell', '/usr/sbin/nologin', 'initiativetracker')
    account = pwd.getpwnam('initiativetracker')
    host.mkdir(DATA, 0o750, (account.pw_uid, account.pw_gid))
    release = APP + '/releases/' + data['version'] + '-linux-arm64'
    host.mkdir(APP)
    host.mkdir(APP + '/releases')
    host.mkdir(release)
    with tarfile.open(package, 'r:gz') as archive:
        archive.extractall(host.path(release), filter='data')
    host.path(release).chmod(0o755)
    # Avoid a global umask 077: root-only release directories prevent the service
    # user from traversing them. Private data/backups receive explicit modes.
    for target in host.path(release).rglob('*'):
        target.chmod(0o755 if target.is_dir() or target.stat().st_mode & 0o111 else 0o644)
    host.path(release + '/InitiativeTracker.Web').chmod(0o755)
    host.copy(REPO / 'hub-bootstrap/appsettings.Production.json', ETC + '/appsettings.Production.json', 0o640,
              (0, account.pw_gid))
    # Release contains its default appsettings; replace only inside the newly
    # created, inactive release. Nothing under an existing current is overwritten.
    target_config = host.path(release + '/appsettings.Production.json')
    if target_config.exists():
        target_config.unlink()
    host.copy(REPO / 'hub-bootstrap/appsettings.Production.json', release + '/appsettings.Production.json')
    for source in ('initiativetracker.service', 'initiativetracker-hub-hostname.service'):
        host.copy(REPO / 'hub-bootstrap' / source, '/etc/systemd/system/' + source)
    host.copy(REPO / 'hub-bootstrap/initiativetracker-sync-hostname', '/usr/local/sbin/initiativetracker-sync-hostname', 0o755)
    host.safe(APP + '/current').symlink_to(release)
    host.run('systemctl', 'daemon-reload')
    host.run('systemctl', 'enable', '--now', 'initiativetracker.service')
    info = wait_health(host, 'http://127.0.0.1:5000', expected_version=data['version'])
    return info


def lightdm_main(text):
    """Let our drop-in take effect, preserving all other main-file settings.

    LightDM reads its main file after conf.d; an old Imager autologin assignment
    would otherwise silently override our new drop-in. Back up before writing.
    """
    section = ''
    result = []
    for line in text.splitlines(keepends=True):
        match = re.match(r'\s*\[([^]]+)\]', line)
        if match:
            section = match[1]
        setting = re.match(r'\s*(autologin-user|autologin-user-timeout|autologin-session|user-session)\s*=', line)
        if setting and section.startswith('Seat:'):
            if section != 'Seat:*':
                raise InstallError('Custom per-seat LightDM settings require local review before automation')
            line = '# InitiativeTracker setting moved to drop-in: ' + line
        result.append(line)
    return ''.join(result)


def desktop_install(host, desktop, settings):
    home = desktop.pw_dir
    owner = (desktop.pw_uid, desktop.pw_gid)
    role = settings['role']
    config = home + '/.config/initiativetracker-' + role
    local = home + '/.local/lib/initiativetracker-' + role
    state = home + '/.local/state/initiativetracker-' + role
    # Created parents must belong to the desktop user too.
    for directory in (home + '/.config', home + '/.local', home + '/.local/lib', home + '/.local/state', home + '/.local/share'):
        host.mkdir(directory, 0o700, owner)
    for directory in (config, local, state, home + '/.config/labwc'):
        host.mkdir(directory, 0o700, owner)
    settings_file = {k: settings[k] for k in ('role', 'display_output', 'display_mode')}
    if role == 'satellite':
        settings_file.update({k: settings[k] for k in ('hub_url', 'hub_id')})
        host.mkdir(home + '/.local/share/initiativetracker-satellite/chromium', 0o700, owner)
    host.write(config + '/kiosk.json', json.dumps(settings_file) + '\n', 0o600, owner)
    host.copy(REPO / 'scripts/installation/kiosk.py', local + '/kiosk.py', 0o755, owner)
    autostart = home + '/.config/labwc/autostart'
    target = host.safe(autostart)
    text = target.read_text() if target.exists() else ''
    if not target.exists() and host.path('/etc/xdg/labwc/autostart').exists():
        text = host.path('/etc/xdg/labwc/autostart').read_text()
    if not text:
        text = '#!/usr/bin/env bash\n'
    if 'initiativetracker-' in text:
        raise InstallError('Existing InitiativeTracker desktop startup preserved; inspect locally')
    line = f'/usr/bin/python3 -I {shlex.quote(local + "/kiosk.py")} {shlex.quote(config + "/kiosk.json")} &'
    host.write(autostart, text + '\n# InitiativeTracker commissioned kiosk\n' + line + '\n', 0o700, owner, replace=target.exists())
    if settings['autologin']:
        session = next(host.path('/usr/share/wayland-sessions').glob('*labwc*.desktop')).stem
        main = host.safe('/etc/lightdm/lightdm.conf')
        if main.exists():
            before = main.read_text()
            after = lightdm_main(before)
            if after != before:
                host.write('/etc/lightdm/lightdm.conf', after, replace=True)
        template = (REPO / 'scripts/installation/templates/lightdm.conf.in').read_text()
        host.write('/etc/lightdm/lightdm.conf.d/90-initiativetracker.conf',
                   template.format(desktop_user=desktop.pw_name, session=session))
        host.run('systemctl', 'set-default', 'graphical.target')
        host.run('systemctl', 'enable', 'lightdm.service')


def plan(role, inspection=True):
    print(f'Role: {role}. ' + ('Inspection only: no packages, files, secrets, service changes, or network operations.' if inspection else 'Proposed fresh-device installation; changes require the interactive confirmations below.'))
    print('Apply requires: Raspberry Pi OS desktop ARM64 Debian 12/13, Labwc + LightDM, active NetworkManager/wlan0, terminal, existing desktop user, local/Ethernet recovery.')
    print('Packages: ' + ', '.join(PACKAGES + ['wlr-randr'] + (['native .NET dependencies for OS version', 'dnsmasq-base', 'nftables', 'iw', 'sqlite3', 'zenity', 'sudo'] if role == 'hub' else [])))
    if role == 'hub':
        print('Release bundles .NET 10 ARM64; system ASP.NET Core 10 runtime is an optional wizard choice, no SDK.')
    print('Interactive: hostname, external WPA Wi-Fi, LightDM autologin, optional display mode. Backups: ' + STATE + '/backups. No SSH changes or automatic reboot.')
    if role == 'hub':
        print('Hub: validate trusted manifest/package; versioned first release + persistent data; approved graphical updater; restricted network socket + independent recovery; inactive random-password AP; fallback timer starts next boot.')
    else:
        print('Satellite: verified hub origin/GUID; private persistent Chromium profile; Labwc kiosk startup; optional hub WPA Wi-Fi saved with lower preference. Manual pairing approval. No automatic Wi-Fi following or remote satellite updater.')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('role', choices=('hub', 'satellite'), nargs='?')
    parser.add_argument('--dry-run', '--inspect', action='store_true', dest='dry_run')
    parser.add_argument('--desktop-user')
    parser.add_argument('--hostname')
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--hub-url')
    parser.add_argument('--hub-id')
    args = parser.parse_args(argv)
    role = args.role
    if role is None:
        role = ask('Device role (hub/satellite)')
        if role not in ('hub', 'satellite'):
            raise InstallError('Choose hub or satellite')
    plan(role, args.dry_run)
    if args.dry_run:
        if role == 'hub' and (args.manifest or args.package):
            if not (args.manifest and args.package):
                raise InstallError('Provide both --manifest and --package for read-only release validation')
            data = validate_release(args.manifest, args.package)
            print('Release inspection passed: ' + data['version'])
        return 0
    if os.geteuid() != 0 or not sys.stdin.isatty():
        raise InstallError('Apply requires sudo and an interactive terminal; use --dry-run for inspection')
    desktop_name = args.desktop_user or os.environ.get('SUDO_USER')
    if not desktop_name:
        desktop_name = ask('Existing desktop username')
    try:
        desktop = pwd.getpwnam(desktop_name)
    except KeyError as exc:
        raise InstallError('Desktop account does not exist; create it with Pi Imager') from exc
    host = Host()
    if existing(host, role, desktop):
        return 0
    version = preflight(host, role, desktop)
    if role == 'hub':
        verify_updater_sources()
        if not (args.manifest and args.package):
            raise InstallError('Hub apply requires a trusted --manifest and --package built/tested/staged by the existing release process')
        validate_release(args.manifest, args.package)
    settings = wizard(role, desktop, args)
    if not re.fullmatch(r'[a-z_][a-z0-9_-]{0,31}', desktop.pw_name):
        raise InstallError('Desktop username cannot be represented by the narrow provisioning template')
    if settings['autologin'] and host.path('/etc/lightdm/lightdm.conf').exists():
        lightdm_main(host.safe('/etc/lightdm/lightdm.conf').read_text())
    autostart = host.safe(desktop.pw_dir + '/.config/labwc/autostart')
    if autostart.exists():
        host.run('bash', '-n', str(autostart))
    if role == 'satellite':
        wait_health(host, settings['hub_url'], settings['hub_id'], attempts=1)
    confirm('Commission this fresh device? Installs packages (maintainer scripts may restart services), sets hostname and saved Wi-Fi preferences, enables kiosk desktop and recovery. Hostname changes can affect SSH discovery. Saved autoconnect profiles may join a currently disconnected radio; an active Wi-Fi connection is never forcibly switched. No automatic reboot. Existing app/data installations are refused. Configuration changes are backed up before replacement.', 'INSTALL-FRESH')
    return commission(host, role, desktop, version, settings, args.manifest, args.package)


def commission(host, role, desktop, version, settings, manifest_source=None, package_source=None):
    host.mkdir(STATE, 0o700)
    lock = host.safe(STATE + '/install.lock').open('a')
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError as exc:
        lock.close()
        raise InstallError('Another installer is running') from exc
    try:
        installed = existing(host, role, desktop)
    except Exception:
        lock.close()
        raise
    if installed:
        lock.close()
        return 0
    receipt = {'role': role, 'desktop_user': desktop.pw_name, 'status': 'in-progress'}
    host.write(STATE + '/receipt.json', json.dumps(receipt), 0o600)
    host.backup = STATE + '/backups/' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime())
    host.mkdir(host.backup, 0o700)
    try:
        # Copy artifacts into root-private staging and revalidate before any APT or
        # service mutation. The caller's transfer directory is never used by root updater.
        manifest = package = None
        if role == 'hub':
            manifest = host.path(STATE + '/first-release.manifest.json')
            package = host.path(STATE + '/' + package_source.name)
            host.copy(manifest_source, STATE + '/first-release.manifest.json', 0o600)
            host.copy(package_source, STATE + '/' + package_source.name, 0o600)
            validate_release(manifest, package)
        for file in ('/etc/hostname', '/etc/hosts', '/etc/machine-info'):
            target = host.safe(file)
            if target.exists():
                backup = host.safe(host.backup + file)
                backup.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
                shutil.copy2(target, backup)
                backup.chmod(0o600)
        packages(host, role, version, settings['system_runtime'])
        host.run('hostnamectl', 'set-hostname', settings['hostname'])
        if role == 'hub':
            info = first_release(host, manifest, package)
            ssid = settings['hotspot_ssid'] or 'InitiativeTracker-' + uuid.UUID(info['hubId']).hex[:8]
            install_network(host, ssid, settings['hotspot_password'])
            spec = importlib.util.spec_from_file_location('updater_provision', REPO / 'hub-updater/provision.py')
            provision = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(provision)
            provision.provision(host, desktop, manifest, package)
            host.run('systemctl', 'restart', 'initiativetracker.service')
            wait_health(host, 'http://127.0.0.1:5000')
        stage_wifi(host, settings['external_wifi'], 'IT-External-1', 100)
        if role == 'satellite':
            stage_wifi(host, settings['hub_wifi'], 'IT-Hub-Private', 50)
        # Hub identity synchronization owns its permanent hostname, and may
        # change discovery. Both hostname files are backed up before it runs.
        if role == 'hub':
            host.run('systemctl', 'enable', '--now', 'initiativetracker-hub-hostname.service')
        desktop_install(host, desktop, settings)
        host.run('systemctl', 'enable', '--now', 'avahi-daemon.service')
        receipt.update(status='complete', files=host.owned, backups=host.backup)
        host.write(STATE + '/receipt.json', json.dumps(receipt, indent=2), 0o600, replace=True)
        print('Fresh-device provisioning completed. Reboot locally when ready, pair satellite through DM Connected Devices, and complete the hardware acceptance checklist before handover.')
        return 0
    except Exception:
        print('Installation stopped. Files/data are preserved; inspect root-private receipt/backups. No automatic data rollback or reboot was attempted.', file=sys.stderr)
        raise
    finally:
        lock.close()


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (InstallError, OSError, ValueError, EOFError, KeyboardInterrupt) as exc:
        print('INSTALLATION STOPPED: ' + (str(exc) or 'cancelled'), file=sys.stderr)
        sys.exit(1)
