#!/usr/bin/env python3
"""Verify/mask only the vendor Certbot units; never install packages or start units."""
import argparse
import json
import os

import foundation as f

UNITS = ('certbot.service','certbot.timer')


def verify(*, persistent=False, _runner=f.run):
    for unit in UNITS:
        output = _runner(['/usr/bin/systemctl','show',unit,'--property=LoadState','--property=UnitFileState','--property=ActiveState'],max_output=4096).decode()
        values = dict(line.split('=',1) for line in output.splitlines() if '=' in line)
        if values.get('LoadState') != 'masked' or values.get('UnitFileState') not in ({'masked'} if persistent else {'masked','masked-runtime'}) or values.get('ActiveState') != 'inactive':
            raise f.Rejected('Both vendor Certbot units must be masked and inactive before package installation')
    return {'vendor_units_masked_and_inactive':True}


def mask(*, _runner=f.run):
    # Persistent before APT: a failed/partial transaction must remain fenced
    # across reboot, rather than leaving only an ephemeral runtime mask.
    _runner(['/usr/bin/systemctl','mask',*UNITS],capture=False)
    return verify(persistent=True,_runner=_runner)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['verify','mask'])
    parser.add_argument('--apply',action='store_true')
    parser.add_argument('--persistent',action='store_true',help='Require persistent masks after package installation')
    args = parser.parse_args()
    try:
        if args.action == 'mask':
            if not args.apply or os.geteuid() != 0:
                raise f.Rejected('Explicit root-operated mask approval required')
            result = mask()
        else:result = verify(persistent=args.persistent)
        print(json.dumps(result));return 0
    except (OSError,ValueError,KeyError):
        print('Vendor unit verification failed: stop before any package installation.')
        return 1


if __name__ == '__main__':raise SystemExit(main())
