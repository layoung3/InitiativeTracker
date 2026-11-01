#!/usr/bin/env python3
"""Owner-operated terminal-only token entry. Never accept a token in argv/environment."""
import argparse
import getpass
import os
from pathlib import Path
import re
import resource
import stat
import sys
import uuid
import warnings

class Rejected(ValueError):
    pass

def write_credentials(destination, token, *, replace=False):
    path = Path(destination)
    if not path.is_absolute() or not re.fullmatch(r'[A-Za-z0-9_-]{20,256}', token):
        raise Rejected('Invalid credential destination or token format')
    for parent in (path.parent, *path.parent.parents):
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or not stat.S_ISDIR(info.st_mode) or info.st_uid not in {0, os.geteuid()}:
            raise Rejected('Untrusted credential directory')
        if info.st_mode & 0o022 and not (str(parent) in {'/tmp','/var/tmp'} and info.st_mode & stat.S_ISVTX):
            raise Rejected('Credential directory is writable by others')
    if path.parent.stat().st_mode & 0o077:
        raise Rejected('Issuer directory must be private (0700)')
    if os.path.lexists(path):
        info = path.lstat()
        if not replace or not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_uid != os.geteuid() or info.st_mode & 0o077:
            raise Rejected('Existing credential file requires explicit safe rotation')
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    temporary = '.cloudflare-' + uuid.uuid4().hex
    try:
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=directory)
        try:
            data = ('dns_cloudflare_api_token = ' + token + '\n').encode('ascii')
            offset = 0
            while offset < len(data):
                written = os.write(descriptor, data[offset:])
                if written <= 0: raise OSError('Credential write failed')
                offset += written
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
        if replace:
            os.replace(temporary, path.name, src_dir_fd=directory, dst_dir_fd=directory)
        else:
            # link is atomic and refuses an unexpected concurrent destination.
            os.link(temporary, path.name, src_dir_fd=directory, dst_dir_fd=directory, follow_symlinks=False)
            os.unlink(temporary, dir_fd=directory)
        os.fsync(directory)
    finally:
        try: os.unlink(temporary, dir_fd=directory)
        except FileNotFoundError: pass
        os.close(directory)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--path', type=Path, required=True)
    parser.add_argument('--replace', action='store_true', help='Explicitly rotate an existing private regular file')
    args = parser.parse_args()
    if os.geteuid() != 0 or not sys.stdin.isatty():
        print('Use an approved root-owned helper in a real local terminal; redirected token input is refused.', file=sys.stderr)
        return 1
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    os.umask(0o077)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter('error', getpass.GetPassWarning)
            token = getpass.getpass('Cloudflare API token (hidden; not sent to chat): ')
        write_credentials(args.path, token, replace=args.replace)
        print('Private credential file written (0600). Token was not displayed.')
        return 0
    except (Rejected, OSError, EOFError, KeyboardInterrupt, getpass.GetPassWarning):
        print('Credential setup failed; inspect private file permissions locally. Token was not displayed.', file=sys.stderr)
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
