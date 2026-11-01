#!/usr/bin/env python3
"""Render isolated, exact-address nftables ingress. Never install/reload rules."""
import argparse
import ipaddress
import json
from pathlib import Path
import re
import uuid

import foundation as f


def read_hotspot(profile, *, _runner=f.run):
    if not isinstance(profile,str):raise f.Rejected('An existing profile UUID is required')
    identity = str(uuid.UUID(profile))
    fields = 'connection.uuid,connection.interface-name,802-11-wireless.mode,ipv4.method,ipv4.addresses,ipv6.method'
    output = _runner(['/usr/bin/nmcli','--escape','no','--get-values',fields,'connection','show','uuid',identity],timeout=10,max_output=4096).decode().splitlines()
    if len(output) != 6:raise f.Rejected('Incomplete NetworkManager hotspot metadata')
    return dict(zip(['id','interface','mode','method','addresses','ipv6'],output))


def validate_hotspot(binding, *, _reader=read_hotspot):
    if not isinstance(binding.get('hotspot_profile'),str):raise f.Rejected('An existing profile UUID is required')
    identity = str(uuid.UUID(binding['hotspot_profile']))
    if uuid.UUID(identity).int == 0:raise f.Rejected('An existing approved hotspot profile UUID is required')
    profile = _reader(identity)
    address = f.local_address(binding['address'])
    if ':' in address or not isinstance(profile,dict) or any(profile.get(key) != value for key,value in
        {'id':identity,'interface':binding['interface'],'mode':'ap','method':'shared','ipv6':'disabled'}.items()):
        raise f.Rejected('Inactive binding must match an interface-bound shared IPv4 AP with IPv6 disabled')
    if str(ipaddress.IPv4Interface(profile['addresses']).ip) != address:
        raise f.Rejected('Configured hotspot address differs from the selected binding')
    return address


def validated_bindings(bindings, assigned, *, _hotspot_reader=read_hotspot):
    if not isinstance(bindings,list) or not 1 <= len(bindings) <= 16:
        raise f.Rejected('Select explicit private interface/address bindings')
    selected = [];seen = set()
    for item in bindings:
        interface,address = item['interface'],f.local_address(item['address'])
        if not isinstance(interface,str) or interface == 'lo' or not re.fullmatch(r'[a-zA-Z0-9_.-]{1,15}',interface) or address in seen:
            raise f.Rejected('Ambiguous private ingress binding')
        if interface not in assigned:raise f.Rejected('Selected ingress interface is not present')
        if 'hotspot_profile' in item:
            validate_hotspot(item,_reader=_hotspot_reader)
        elif address not in assigned.get(interface,[]):
            raise f.Rejected('Ingress address is not currently assigned to the selected interface')
        seen.add(address);selected.append((interface,address))
    return selected


def render(bindings, assigned, *, _hotspot_reader=read_hotspot):
    selected = validated_bindings(bindings,assigned,_hotspot_reader=_hotspot_reader)
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
