#!/usr/bin/env python3
"""Verify updater tar members against both bundled manifests without extraction.

This checks bundle consistency, not source authenticity. Approve the pinned
commit and record the archive SHA-256 separately before any transfer or use.
"""
import argparse
import hashlib
import json
from pathlib import PurePosixPath
import sys
import tarfile

NAMES = {'initiativetracker-install-update', 'initiativetracker-check-update',
         'initiativetracker-hub-launch'}


def verify(path):
    with tarfile.open(path, 'r:') as archive:
        members = {}
        for member in archive:
            name = member.name.rstrip('/')
            parts = PurePosixPath(name).parts
            if (not parts or parts[0] != 'hub-updater' or '..' in parts or
                    '\\' in name or name.startswith('/') or
                    not (member.isfile() or member.isdir())):
                raise ValueError('Unexpected archive member: ' + member.name)
            if name in members:
                raise ValueError('Duplicate archive member: ' + name)
            members[name] = member

        def read(name):
            member = members.get('hub-updater/' + name)
            if member is None or not member.isfile() or member.size > 1024 * 1024:
                raise ValueError('Missing, non-regular or oversized member: ' + name)
            return archive.extractfile(member).read()

        provenance = json.loads(read('provenance.json'))
        originals = provenance['files']
        if len(originals) != len(NAMES) or {f['name'] for f in originals} != NAMES:
            raise ValueError('Invalid original provenance file set')
        originals = {f['name']: f for f in originals}
        runtime = json.loads(read('runtime-manifest.json'))
        if runtime.get('formatVersion') != 1 or set(runtime['sha256']) != NAMES:
            raise ValueError('Invalid reviewed runtime manifest')
        results = []
        for variant in ('original', 'runtime'):
            for name in sorted(NAMES):
                entry = variant + '/' + name
                data = read(entry)
                digest = hashlib.sha256(data).hexdigest()
                expected = originals[name]['sha256'] if variant == 'original' else runtime['sha256'][name]
                if digest != expected or (variant == 'original' and len(data) != originals[name]['bytes']):
                    raise ValueError(f'{entry}: manifest mismatch; bytes={len(data)} sha256={digest} expected={expected}')
                results.append(f'{entry}: bytes={len(data)} sha256={digest}')
        return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', help='tar created with git archive --format=tar --output=...')
    args = parser.parse_args()
    try:
        results = verify(args.archive)
    except (OSError, ValueError, KeyError, TypeError, tarfile.TarError) as exc:
        print('ARCHIVE VERIFICATION STOPPED: ' + str(exc), file=sys.stderr)
        return 1
    print('\n'.join(results))
    print('PASS: all six updater script entries match their bundled manifests.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
