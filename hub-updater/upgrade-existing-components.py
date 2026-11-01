#!/usr/bin/python3 -I
"""Inspect or replace only the three approved updater/kiosk runtime components.

Separate from fresh commissioning; never runs an updater, changes configuration,
or touches application releases/data/network/pairing. Apply/rollback need root,
an interactive confirmation, and a root-controlled reviewed bundle/backup.
"""
import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import pwd
import secrets
import stat
import sys
import tempfile
import time

BACKUPS = '/var/backups/initiativetracker-components'
LOCK = '/run/lock/initiativetracker-updater.lock'
NAMES = ('initiativetracker-install-update', 'initiativetracker-check-update', 'initiativetracker-hub-launch')


class UpgradeError(Exception):
    pass


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Components:
    # Alternate roots are a Python test interface only, never a CLI option.
    def __init__(self, desktop, root=Path('/'), bundle=None):
        self.root = root
        self.bundle = bundle or Path(__file__).resolve().parent
        self.desktop = desktop
        if desktop.pw_uid < 1000 or not Path(desktop.pw_dir).is_absolute() or '..' in Path(desktop.pw_dir).parts:
            raise UpgradeError('Select an existing ordinary desktop user with an absolute home')
        self.targets = {
            NAMES[0]: '/usr/local/sbin/' + NAMES[0],
            NAMES[1]: '/usr/local/bin/' + NAMES[1],
            NAMES[2]: desktop.pw_dir + '/.local/lib/initiativetracker-hub/' + NAMES[2],
        }
        self.guards = ('/etc/initiativetracker/updater.conf',
                       '/etc/initiativetracker/appsettings.Production.json',
                       desktop.pw_dir + '/.config/labwc/autostart')

    def path(self, value):
        if not value.startswith('/') or '..' in Path(value).parts:
            raise UpgradeError('Expected an absolute managed path')
        return self.root / value.lstrip('/')

    @contextmanager
    def updater_lock(self):
        """Hold the graphical updater's inode; never truncate, chmod or unlink it.

        Only /run/lock may be root-owned sticky 1777. Open directory descriptors
        without following links, then validate the file descriptor before flock.
        The sticky bit protects a root-owned file from unprivileged replacement.
        Administrators able to replace root-owned paths remain trusted.
        """
        directory = os.open(self.root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        fd = None
        try:
            for part in ('', 'run', 'lock'):
                if part:
                    child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC,
                                    dir_fd=directory)
                    os.close(directory)
                    directory = child
                info = os.fstat(directory)
                sticky_lock = part == 'lock' and stat.S_IMODE(info.st_mode) == 0o1777
                if info.st_uid != 0 or info.st_gid != 0 or (info.st_mode & 0o022 and not sticky_lock):
                    raise UpgradeError('Unsafe updater lock directory ownership/mode: ' + (part or '/'))
            name = Path(LOCK).name
            flags = os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC
            try:
                fd = os.open(name, flags | os.O_CREAT | os.O_EXCL, 0o600, dir_fd=directory)
            except FileExistsError:
                fd = os.open(name, flags, dir_fd=directory)

            def validate():
                info = os.fstat(fd)
                entry = os.stat(name, dir_fd=directory, follow_symlinks=False)
                if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_gid != 0 or
                        info.st_mode & 0o022 or info.st_nlink != 1 or
                        (info.st_dev, info.st_ino) != (entry.st_dev, entry.st_ino)):
                    raise UpgradeError('Unsafe updater lock file type/ownership/mode/link identity; inspect locally')

            validate()
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError as exc:
                raise UpgradeError('An application update is running; component replacement refused') from exc
            validate()
            yield fd
        finally:
            if fd is not None:
                os.close(fd)
            os.close(directory)

    def no_links(self, path, trusted=False):
        for node in (path, *path.parents):
            if node == self.root.parent and self.root != Path('/'):
                break
            if not node.exists() and not node.is_symlink():
                continue
            info = node.lstat()
            if stat.S_ISLNK(info.st_mode):
                raise UpgradeError('Symlink in component/config/backup path; inspect locally')
            if trusted and (info.st_uid != 0 or info.st_mode & 0o022):
                raise UpgradeError('Root-controlled path must be root-owned and not group/world writable')

    def read(self, path, trusted=False):
        self.no_links(path, trusted)
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, 'rb') as stream:
            before = os.fstat(stream.fileno())
            if not stat.S_ISREG(before.st_mode):
                raise UpgradeError('Expected regular component/config file')
            data = stream.read()
            after = os.fstat(stream.fileno())
            if (before.st_ino, before.st_size, before.st_mtime_ns) != (after.st_ino, after.st_size, after.st_mtime_ns):
                raise UpgradeError('File changed during inspection')
            return data, {'sha256': sha(data), 'uid': before.st_uid, 'gid': before.st_gid,
                          'mode': stat.S_IMODE(before.st_mode), 'xattrs': sorted(os.listxattr(stream.fileno()))}

    def candidates(self, trusted=False):
        original = json.loads((self.bundle / 'provenance.json').read_text())
        expected = {entry['name']: entry for entry in original['files']}
        manifest_path = self.bundle / 'runtime-manifest.json'
        if trusted:
            self.no_links(self.bundle, True)
            self.no_links(manifest_path, True)
            self.no_links(self.bundle / 'provenance.json', True)
        manifest = json.loads(manifest_path.read_text())
        if manifest.get('formatVersion') != 1 or set(manifest.get('sha256', {})) != set(NAMES):
            raise UpgradeError('Invalid reviewed runtime manifest')
        result = {}
        for name in NAMES:
            snapshot, _ = self.read(self.bundle / 'original' / name, trusted)
            if sha(snapshot) != expected[name]['sha256'] or len(snapshot) != expected[name]['bytes']:
                raise UpgradeError('Original source provenance mismatch')
            data, info = self.read(self.bundle / 'runtime' / name, trusted)
            if info['sha256'] != manifest['sha256'][name]:
                raise UpgradeError('Candidate runtime hash mismatch; inspect reviewed bundle')
            result[name] = {'data': data, 'sha256': info['sha256'], 'original_sha256': expected[name]['sha256']}
        return result

    def inspect(self, trusted=False):
        candidates = self.candidates(trusted)
        home = self.path(self.desktop.pw_dir)
        self.no_links(home)
        if not home.is_dir() or home.stat().st_uid != self.desktop.pw_uid:
            raise UpgradeError('Selected desktop home must exist and belong to that user')
        components = {}
        for name, path in self.targets.items():
            data, info = self.read(self.path(path), trusted=name != NAMES[2])
            expected_owner = (self.desktop.pw_uid, self.desktop.pw_gid) if name == NAMES[2] else (0, 0)
            if (info['uid'], info['gid'], info['mode']) != (*expected_owner, 0o755):
                raise UpgradeError('Existing component ownership/mode differs from approved layout; inspect locally')
            if info['xattrs']:
                raise UpgradeError('Component extended attributes/ACLs/capabilities require local review; no metadata will be discarded')
            if info['sha256'] not in (candidates[name]['original_sha256'], candidates[name]['sha256']):
                raise UpgradeError('Existing component hash is unrecognized; preserve and review before replacement')
            components[name] = {'path': path, 'before': info, 'data': data,
                                'after_sha256': candidates[name]['sha256']}
        guards = {}
        for path in self.guards:
            data, info = self.read(self.path(path), trusted=path.startswith('/etc/'))
            guards[path] = {'metadata': info, 'data': data}
        config = guards[self.guards[0]]['metadata']
        if config['mode'] != 0o644 or config['uid'] != 0:
            raise UpgradeError('updater.conf must already be root-owned regular 0644; no configuration repair is performed')
        current = self.path('/opt/initiativetracker/current')
        if not current.is_symlink():
            raise UpgradeError('Existing versioned current symlink is required')
        target = os.readlink(current)
        prefix = '/opt/initiativetracker/releases/'
        leaf = target.removeprefix(prefix)
        if (not target.startswith(prefix) or '/' in leaf or '..' in leaf or not leaf.endswith('-linux-arm64')):
            raise UpgradeError('Current must select the established versioned ARM64 release directory')
        executable = self.path(target + '/InitiativeTracker.Web')
        self.no_links(executable, trusted=True)
        if not executable.is_file() or not executable.stat().st_mode & 0o111:
            raise UpgradeError('Current release executable is unavailable; inspect application health locally')
        return candidates, components, guards, target

    def atomic(self, target, data, metadata):
        path = self.path(target)
        self.no_links(path, trusted=target.startswith('/usr/'))
        fd, temporary = tempfile.mkstemp(prefix='.it-components-', dir=path.parent)
        try:
            with os.fdopen(fd, 'wb') as stream:
                stream.write(data)
                stream.flush()
                os.fsync(stream.fileno())
                os.fchown(stream.fileno(), metadata['uid'], metadata['gid'])
                os.fchmod(stream.fileno(), metadata['mode'])
            os.replace(temporary, path)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)

    def unchanged(self, guards, current):
        if os.readlink(self.path('/opt/initiativetracker/current')) != current:
            raise UpgradeError('Application current changed during component replacement; inspect locally')
        for path, info in guards.items():
            if self.read(self.path(path))[1] != info['metadata']:
                raise UpgradeError('Configuration/autostart changed during component replacement; inspect locally')

    def backup(self, components, guards, current):
        base = self.path(BACKUPS)
        self.no_links(base, True)
        if not base.exists():
            base.mkdir(mode=0o700, parents=True)
        if base.stat().st_uid != 0 or stat.S_IMODE(base.stat().st_mode) != 0o700:
            raise UpgradeError('Component backup root must be root-owned 0700')
        directory = base / (time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()) + '-' + secrets.token_hex(4))
        directory.mkdir(mode=0o700)
        receipt = {'formatVersion': 1, 'desktop_user': self.desktop.pw_name, 'desktop_home': self.desktop.pw_dir,
                   'current': current, 'components': {}, 'guards': {}}
        for name, item in components.items():
            (directory / name).write_bytes(item['data'])
            (directory / name).chmod(0o600)
            receipt['components'][name] = {key: value for key, value in item.items() if key != 'data'}
        for index, (path, item) in enumerate(guards.items()):
            filename = 'config-' + str(index)
            (directory / filename).write_bytes(item['data'])
            (directory / filename).chmod(0o600)
            receipt['guards'][path] = {'metadata': item['metadata'], 'backup': filename}
        (directory / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
        (directory / 'receipt.json').chmod(0o600)
        return directory

    def apply(self):
        candidates, components, guards, current = self.inspect(trusted=True)
        if all(item['before']['sha256'] == item['after_sha256'] for item in components.values()):
            return None
        directory = self.backup(components, guards, current)
        changed = []
        try:
            for name, item in components.items():
                if self.read(self.path(item['path']))[1] != item['before']:
                    raise UpgradeError('Component changed after inspection; replacement refused')
                self.atomic(item['path'], candidates[name]['data'], item['before'])
                changed.append(name)
            self.unchanged(guards, current)
            return directory
        except Exception:
            failures = []
            for name in reversed(changed):
                try:
                    self.atomic(components[name]['path'], components[name]['data'], components[name]['before'])
                except Exception:
                    failures.append(name)
            raise UpgradeError('Component replacement stopped; original scripts restored where possible. Recovery failures: ' + str(len(failures)) + '. Private backup: ' + str(directory))

    def rollback(self, directory):
        base = self.path(BACKUPS)
        if directory.parent != base:
            raise UpgradeError('Rollback requires one direct private component-backup directory')
        receipt_data, info = self.read(directory / 'receipt.json', trusted=True)
        if info['mode'] != 0o600 or stat.S_IMODE(directory.stat().st_mode) != 0o700:
            raise UpgradeError('Unsafe rollback receipt/backup permissions')
        receipt = json.loads(receipt_data)
        if (receipt.get('formatVersion') != 1 or receipt.get('desktop_user') != self.desktop.pw_name or
                receipt.get('desktop_home') != self.desktop.pw_dir or set(receipt.get('components', {})) != set(NAMES) or
                set(receipt.get('guards', {})) != set(self.guards)):
            raise UpgradeError('Rollback receipt differs from selected desktop/layout')
        guards = receipt['guards']
        self.unchanged(guards, receipt['current'])
        snapshots = {}
        installed_snapshots = {}
        for name, item in receipt['components'].items():
            if item['path'] != self.targets[name]:
                raise UpgradeError('Rollback component path mismatch')
            data, _ = self.read(directory / name, trusted=True)
            if sha(data) != item['before']['sha256']:
                raise UpgradeError('Rollback snapshot hash mismatch')
            installed_data, installed = self.read(self.path(item['path']))
            if installed != dict(item['before'], sha256=item['after_sha256']):
                raise UpgradeError('Installed component changed since upgrade; refusing rollback overwrite')
            snapshots[name] = data
            installed_snapshots[name] = (installed_data, installed)
        changed = []
        try:
            for name, item in receipt['components'].items():
                self.atomic(item['path'], snapshots[name], item['before'])
                changed.append(name)
            self.unchanged(guards, receipt['current'])
        except Exception:
            failures = []
            for name in reversed(changed):
                try:
                    self.atomic(receipt['components'][name]['path'], *installed_snapshots[name])
                except Exception:
                    failures.append(name)
            raise UpgradeError('Script rollback stopped; installed candidates restored where possible. Recovery failures: ' + str(len(failures)) + '. Private backup: ' + str(directory))


def confirmation(token):
    if not sys.stdin.isatty() or input('Type ' + token + ' to continue: ').strip() != token:
        raise UpgradeError('Interactive approval required; no component was replaced')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--desktop-user', required=True)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--inspect', action='store_true')
    actions.add_argument('--apply', action='store_true')
    actions.add_argument('--rollback', type=Path)
    args = parser.parse_args()
    manager = Components(pwd.getpwnam(args.desktop_user))
    if not args.apply and not args.rollback:
        candidates, components, _, current = manager.inspect()
        print('Inspection only. Selected desktop home: ' + manager.desktop.pw_dir)
        print('Existing versioned symlink: ' + current)
        for name, item in components.items():
            print(name + ': installed=' + item['before']['sha256'] + ' candidate=' + candidates[name]['sha256'])
        print('Only the three scripts may be replaced. Config/autostart/privileges/app/data/network/pairing remain untouched.')
        return 0
    if os.geteuid() != 0:
        raise UpgradeError('Apply/rollback requires root and separately approved local commissioning')
    print('Do not use this on a working Pi without explicit approval. Pause kiosk/update activity locally; keep recovery access. No updater or application release will be executed.')
    confirmation('ROLLBACK-COMPONENTS' if args.rollback else 'REPLACE-COMPONENTS')
    with manager.updater_lock():
        if args.rollback:
            manager.rollback(args.rollback)
            print('Previous scripts restored. Validate metadata and unchanged health locally; do not execute the updater as a test.')
        else:
            backup = manager.apply()
            print('Already at approved component hashes; no changes.' if backup is None else 'Scripts replaced; private rollback backup: ' + str(backup))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (UpgradeError, OSError, ValueError, KeyError, EOFError, KeyboardInterrupt) as exc:
        print('COMPONENT UPGRADE STOPPED: ' + str(exc), file=sys.stderr)
        sys.exit(1)
