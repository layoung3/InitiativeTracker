#!/usr/bin/env python3
"""Render isolated, exact-address nftables ingress. Never install/reload rules."""
import argparse
import json
from pathlib import Path
import re

import foundation as f


def render(bindings, assigned):
    if not isinstance(bindings,list) or not 1 <= len(bindings) <= 16:
        raise f.Rejected('Select explicit private interface/address bindings')
    selected = [];seen = set()
    for item in bindings:
        interface,address = item['interface'],f.local_address(item['address'])
        if not isinstance(interface,str) or interface == 'lo' or not re.fullmatch(r'[a-zA-Z0-9_.-]{1,15}',interface) or address in seen:
            raise f.Rejected('Ambiguous private ingress binding')
        if address not in assigned.get(interface,[]):
            raise f.Rejected('Ingress address is not currently assigned to the selected interface')
        seen.add(address);selected.append((interface,address))
    lines = ['# REVIEW ONLY. Dedicated table; no other firewall/NM state is changed.',
        'table inet initiativetracker_browser443 {',
        ' chain prerouting { type nat hook prerouting priority dstnat; policy accept;']
    for interface,address in selected:
        family = 'ip6' if ':' in address else 'ip'
        target = '['+address+']:5445' if family == 'ip6' else address+':5445'
        lines.append(f'  iifname "{interface}" {family} daddr {address} fib daddr type local tcp dport 443 dnat {family} to {target}')
    lines += [' }',' chain output { type nat hook output priority dstnat; policy accept;',
        '  ip daddr 127.0.0.1 tcp dport 443 dnat ip to 127.0.0.1:5445',
        '  ip6 daddr ::1 tcp dport 443 dnat ip6 to [::1]:5445',' }',
        ' chain input { type filter hook input priority -10; policy accept;',
        '  iifname "lo" ip saddr 127.0.0.0/8 ip daddr 127.0.0.0/8 tcp dport 5445 accept',
        '  iifname "lo" ip6 saddr ::1 ip6 daddr ::1 tcp dport 5445 accept']
    for interface,address in selected:
        family = 'ip6' if ':' in address else 'ip'
        lines.append(f'  iifname "{interface}" {family} daddr {address} tcp dport 5445 ct original proto-dst 443 ct original {family} daddr {address} accept')
    lines += ['  tcp dport 5445 drop','  tcp dport 443 drop',' }','}','']
    return '\n'.join(lines)


def assigned_interfaces():
    result = {}
    for link in json.loads(f.run(['/usr/sbin/ip','-j','address','show'])):
        result[link['ifname']] = [str(item['local']) for item in link.get('addr_info',[]) if item.get('scope') == 'global']
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bindings',type=Path,required=True,help='Reviewed JSON array of interface/address objects')
    args = parser.parse_args()
    try:
        f.private_path(args.bindings)
        if args.bindings.stat().st_size > 16384:raise f.Rejected('Bindings exceed limit')
        print(render(json.loads(args.bindings.read_text()),assigned_interfaces()),end='')
        return 0
    except (OSError,ValueError,KeyError,TypeError):
        print('Private ingress rendering rejected; no network settings were changed.')
        return 1


if __name__ == '__main__':raise SystemExit(main())
