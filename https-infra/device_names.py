#!/usr/bin/env python3
"""Validate an owner-approved GUID/hostname inventory; never create or reseed identity."""
import argparse
import json
from pathlib import Path
import re
import uuid

class Rejected(ValueError):
    pass

def inventory(value):
    if not isinstance(value, dict) or value.get('format') != 1 or not isinstance(value.get('devices'), list) or len(value['devices']) > 4096:
        raise Rejected('Invalid or oversized approved inventory')
    domain = value.get('domain', '')
    if not isinstance(domain, str) or not re.fullmatch(r'[a-z0-9](?:[a-z0-9.-]{0,249}[a-z0-9])?', domain) or len(domain) > 210 or '.' not in domain or domain.split('.')[-1].isdigit() or domain.split('.')[-1] in {'local', 'localhost', 'test', 'invalid', 'example', 'internal', 'lan', 'home', 'arpa', 'onion'} or any(not re.fullmatch(r'[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?', label) for label in domain.split('.')):
        raise Rejected('Invalid canonical domain')
    identities, names, aliases, result = set(), set(), set(), []
    for item in value['devices']:
        if not isinstance(item, dict) or item.get('role') not in {'hub', 'satellite'}:
            raise Rejected('Unknown device role')
        try:
            identity = uuid.UUID(item['id'])
        except (KeyError, ValueError, TypeError, AttributeError):
            raise Rejected('Use the existing full stored GUID, never a MAC address') from None
        alias = item.get('short_id', '')
        if identity.int == 0 or not isinstance(alias, str) or not re.fullmatch('[0-9a-f]{8,32}', alias) or not identity.hex.startswith(alias):
            raise Rejected('Short ID must be an approved prefix of the stored GUID')
        key = identity
        name = item['role'] + '-' + alias + '.' + domain
        if key in identities or name in names or alias in aliases:
            raise Rejected('Identity/hostname collision: explicitly resolve using an unused longer GUID prefix; never reseed identity')
        identities.add(key); names.add(name); aliases.add(alias)
        result.append({'role': item['role'], 'id': str(identity), 'short_id': alias, 'hostname': name})
    return result

def lookup(value, role, identity):
    try:
        identity = str(uuid.UUID(identity))
    except (ValueError, TypeError, AttributeError):
        raise Rejected('A full stored GUID is required') from None
    entries = inventory(value)
    matches = [item for item in entries if item['role'] == role and item['id'] == identity]
    if len(matches) != 1:
        raise Rejected('Device is not in the approved inventory; do not silently assign an alias')
    return matches[0]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inventory', type=Path, required=True)
    parser.add_argument('--role', choices=['hub', 'satellite'], required=True)
    parser.add_argument('--id', required=True, help='Existing full GUID; non-secret identity metadata')
    args = parser.parse_args()
    try:
        if args.inventory.stat().st_size > 1_048_576: raise Rejected('Inventory exceeds size limit')
        result = lookup(json.loads(args.inventory.read_text()), args.role, args.id)
        print(json.dumps({'hostname': result['hostname'], 'id': result['id'], 'scope': 'approved-inventory-only'}))
        return 0
    except (Rejected, OSError, ValueError, KeyError):
        print('Approved inventory validation failed; resolve identity/alias explicitly.')
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
