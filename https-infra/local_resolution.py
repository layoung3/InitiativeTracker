#!/usr/bin/env python3
"""Exact-host resolver templates and a read-only client preflight; never edit DNS."""
import argparse
import http.client
import json
from pathlib import Path
import socket
import ssl
import uuid

import device_names
import foundation as f


def hotspot_dns(host, addresses, assigned):
    # NetworkManager owns the shared instance, interfaces, upstreams and DHCP.
    # Importing the hub's /etc/hosts would expose its physical-screen loopback name.
    return f.render_dns(host, addresses, assigned)


def client_hosts(host, addresses):
    f.hostname(host)
    selected = sorted({f.local_address(item) for item in addresses})
    if not selected:
        raise f.Rejected('At least one current private address is required')
    return ''.join(address + '\t' + host + '\n' for address in selected)


def physical_hosts(host):
    f.hostname(host)
    return '127.0.0.1\t' + host + '\n::1\t' + host + '\n'


def https_identity(host, expected_id, *, _connection=http.client.HTTPSConnection):
    f.hostname(host)
    expected = uuid.UUID(expected_id)
    if expected.int == 0:
        raise f.Rejected('An approved stored identity is required')
    # Default public trust, SNI and hostname validation. No CLI CA/verify bypass.
    connection = _connection(host, 443, timeout=8, context=ssl.create_default_context())
    try:
        connection.request('GET', '/system/hub-info', headers={'Accept':'application/json', 'Connection':'close'})
        response = connection.getresponse()
        if response.status != 200 or response.getheader('Content-Type', '').split(';')[0].strip().lower() != 'application/json':
            raise f.Rejected('The canonical TLS endpoint did not return hub identity')
        body = response.read(8193)
        if len(body) > 8192:
            raise f.Rejected('Hub identity response exceeds limit')
        value = json.loads(body)
        if uuid.UUID(value['hubId']) != expected or value.get('protocolVersion') != 1:
            raise f.Rejected('The full hub identity differs from the approved inventory')
    except (OSError, ValueError, KeyError, TypeError, http.client.HTTPException):
        raise f.Rejected('Canonical TLS/identity verification failed') from None
    finally:
        connection.close()


def client_preflight(host, expected_id, addresses, *, _resolver=socket.getaddrinfo, _identity=https_identity):
    f.hostname(host)
    expected = {f.local_address(item) for item in addresses}
    if not expected:
        raise f.Rejected('Explicit current private addresses are required')
    resolution = f.resolve_preflight(host, expected, _resolver)
    if not resolution['ready']:
        return {'ready':False, 'reason':resolution['reason'], 'browser_verification_required':True}
    try:
        _identity(host, expected_id)
    except f.Rejected:
        return {'ready':False, 'reason':'tls-or-identity-failed', 'browser_verification_required':True}
    return {'ready':True, 'reason':'os-resolver-and-public-tls-identity-verified', 'browser_verification_required':True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['hotspot-template','client-hosts','physical-hosts','client-preflight'])
    parser.add_argument('--inventory', type=Path, required=True)
    parser.add_argument('--id', required=True, help='Approved existing full hub GUID')
    parser.add_argument('--address', action='append', default=[], help='Current private hub address, repeat for each advertised family')
    args = parser.parse_args()
    try:
        if args.inventory.stat().st_size > 1_048_576:
            raise f.Rejected('Inventory exceeds limit')
        item = device_names.lookup(json.loads(args.inventory.read_text()), 'hub', args.id)
        host = item['hostname']
        if args.action == 'hotspot-template':
            print(hotspot_dns(host, args.address, f.assigned_addresses()), end='')
        elif args.action == 'client-hosts':
            print(client_hosts(host, args.address), end='')
        elif args.action == 'physical-hosts':
            if args.address:
                raise f.Rejected('Physical mapping must be hub-local loopback only')
            print(physical_hosts(host), end='')
        else:
            result = client_preflight(host, item['id'], args.address)
            print(json.dumps(result))
            return 0 if result['ready'] else 1
        return 0
    except (OSError, ValueError, KeyError, TypeError):
        print('Exact-host configuration/preflight rejected; no resolver settings were changed.')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
