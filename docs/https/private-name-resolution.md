# Private hostname resolution: XB8, hotspot and physical screen

Target: `hub-84eaee7c.colophon.stream`. No runtime public A/AAAA, wildcard, router
forwarding, router replacement, bridge mode or new hardware. The XB8-T/likely
CGM4981COM is deliberately treated as non-cooperative. Nothing in this guide
changes its settings or assumes it can distribute custom DNS. DNS and application
deployment remain separate [approval gates](private443-deployment.md).

## Acceptance matrix

| Client/scope | Supported strategy and required proof |
|---|---|
| Hub physical browser | Hub-only exact hosts/resolver mapping to loopback; trusted FQDN certificate; genuine loopback 443 ingress; physical proof GET/POST acceptance. |
| Hotspot client using its DHCP resolver | Exact local record from the existing shared DNS instance, with hosts import disabled; A/AAAA tested from the client. Browser policy can still bypass that DNS. |
| Windows/Linux desktop on XB8 | Explicit exact-host mapping to the hub's current private LAN address. OS preflight, actual browser HTTPS/full identity and ordinary internet acceptance required. |
| Other client with supported per-domain split DNS | Configure only this exact hostname through an explicitly controlled resolver and verify. Not supplied automatically by the XB8 or by merely running DNS on the Pi. |
| Arbitrary unconfigured phone on XB8; forced DoH/private DNS/VPN | No automatic private-name support claimed. Use an approved per-client method if available, or deliberately select the hub hotspot. Do not disable DNS security to force success. |
| Unfamiliar external Wi-Fi | Only an actually cooperative resolver/explicit client configuration, otherwise the deliberately selected hotspot fallback. Client isolation can block even correct DNS. |

A public DNS server is **not a secondary fallback** for a private-only record: a
resolver may choose it and return NXDOMAIN. A Pi-only hosts change fixes only the
Pi. Never globally disable rebinding protection, browser checks or certificate
validation. An exact administrator-controlled resolver exception, if needed, must
be approved for that resolver; this guide does not apply one on the XB8.

## Approved identity and current addresses

Record the hub's existing full stored GUID through an authenticated/trusted
inspection of the existing installation. The familiar eight digits are only 32
bits, not a global uniqueness guarantee. Do not generate a replacement identity.
Create a **local owner-approved inventory**, format 1:

```json
{
  "format": 1,
  "domain": "colophon.stream",
  "devices": [
    {"role": "hub", "id": "REPLACE_WITH_EXISTING_FULL_HUB_GUID", "short_id": "84eaee7c"}
  ]
}
```

`device_names.py --inventory INVENTORY.json --role hub --id FULL_GUID` rejects
unknown identities, duplicate GUIDs, aliases or names, and a prefix not matching
the full GUID. It never writes identity. For a collision, preserve the existing
assignment; explicitly approve an unused longer prefix of the conflicting/new
device's GUID, then update its inventory/certificate/private DNS/label together.
Do not silently reseed, change the established hub alias, or derive IDs from MACs.
This detects collisions only within the supplied approved inventory. A future
fleet needs a separately selected authoritative registry, not a fictional global
check. Do not copy the synthetic GUIDs from automated tests into the inventory.

Select only currently assigned reachable RFC1918 IPv4/ULA IPv6 addresses for each
network. The LAN address/subnet is not inferred from the name `Logan`. Confirm it
locally with `ip -j address show`, current connection details and the approved
hub identity. Do not advertise another interface's address, link-local/global IPv6,
or stale AAAA. No router reservation is presumed; revalidate after DHCP changes.

Read-only generation/preflight commands (reviewed tools installed separately):

```bash
# Run on the hub; generation checks the selected address is actually assigned.
python3 /usr/local/lib/initiativetracker-https/local_resolution.py hotspot-template \
  --inventory INVENTORY.json --id FULL_GUID --address CURRENT_HOTSPOT_PRIVATE_IP
# Generate a desktop mapping; it does not edit that desktop.
python3 /usr/local/lib/initiativetracker-https/local_resolution.py client-hosts \
  --inventory INVENTORY.json --id FULL_GUID --address CURRENT_LAN_PRIVATE_IP
# Only for the hub's physical browser, never distribute to clients/DHCP:
python3 /usr/local/lib/initiativetracker-https/local_resolution.py physical-hosts \
  --inventory INVENTORY.json --id FULL_GUID
```

## XB8: explicitly configured desktops

Prefer exact-host mapping so ordinary internet DNS remains independent of the
hub's uptime. Back up the client's hosts file, record its initial hash and check
for an existing entry before editing. Administrator approval is required on that
client. Add only one owned block using the **current private LAN address**, e.g.:

```text
# BEGIN InitiativeTracker hub-84eaee7c (owner-approved)
CURRENT_LAN_PRIVATE_IP hub-84eaee7c.colophon.stream
# END InitiativeTracker hub-84eaee7c (owner-approved)
```

The address above is intentionally not guessed. Windows uses
`%SystemRoot%\System32\drivers\etc\hosts`; Linux uses `/etc/hosts`. Preserve all
unrelated lines/ACLs. Never add loopback on a remote client. After editing, Windows
Administrator PowerShell can flush cached answers and inspect its OS resolver:

```powershell
ipconfig /flushdns
[System.Net.Dns]::GetHostAddresses('hub-84eaee7c.colophon.stream') |
  ForEach-Object { $_.IPAddressToString }
```

On Linux use `getent ahosts hub-84eaee7c.colophon.stream`. Direct `nslookup`/`dig`
tests the selected DNS server and can bypass hosts; it does not validate the
hosts-file path. Restart/refresh the intended browser as necessary, then verify
the normal no-port HTTPS URL **without** warnings, the expected full hub identity,
fresh secure enrollment, and an ordinary unrelated internet site. A CLI result
does not prove a browser's DoH/VPN policy. Leave the router and general DNS servers
unchanged. Do not promise this desktop procedure works on arbitrary phones.

If Python tools are available on the configured client, the additional read-only
check uses normal public trust, port 443, exact inventory identity, bounded response
size and no redirect following:

```bash
python3 local_resolution.py client-preflight --inventory INVENTORY.json \
  --id FULL_GUID --address CURRENT_LAN_PRIVATE_IP
```

It rejects any extra/missing A/AAAA (including loopback) and a wrong full GUID.
Its success still explicitly requires actual browser and internet acceptance.
The 443 profile is opt-in and uncommissioned, so a failure on today's
0.1.5 HTTP-only installation is expected, not authority to bypass TLS.

For client rollback remove only the owned block, preserving later unrelated
changes; restore the exact backup only after confirming no other edits occurred.
Flush/recheck caches and ordinary internet again. When a private DHCP address
changes, stop sensitive use, verify the hub's full identity and new address
independently, update only the owned block, and repeat acceptance. Never accept a
familiar short hostname as identity if the full GUID differs.

## Hotspot exact DNS and the loopback leakage boundary

Use the **existing NetworkManager shared-mode dnsmasq instance**, not a competing
wildcard/open resolver. Verify the installed NetworkManager/dnsmasq version and
actual startup arguments/configuration first. The supported NM integration is
`/etc/NetworkManager/dnsmasq-shared.d/`; availability/effective configuration on
this Debian image remains a physical acceptance item. See
[NetworkManager shared DNS documentation](https://networkmanager.dev/docs/api/latest/NetworkManager.conf.html).

The generated, separately reviewed snippet is:

```ini
no-hosts
local=/hub-84eaee7c.colophon.stream/
host-record=hub-84eaee7c.colophon.stream,CURRENT_HOTSPOT_PRIVATE_IP
```

`no-hosts` is essential: the hub will have a private loopback mapping in
`/etc/hosts`. Review the **entire effective dnsmasq configuration** for `addn-hosts`,
`hostsdir`, duplicate host-record/address/server rules or other imports which could
reintroduce it; `no-hosts` alone does not disable explicit extra-host imports.
Preserve NetworkManager's interface binding, DHCP, firewall and upstream behavior.
The snippet does not change upstream internet DNS or enable a new hotspot.
Do not install it blindly on a system with other shared connections: review its
scope and ensure it answers only on the intended managed private interface/subnet.

Take an exact existing-file/absence receipt before separately approved snippet
installation. A shared DNS restart may disconnect clients; schedule it and keep
the existing network recovery path. Do not overwrite NetworkManager files or
flush its firewall. Check A **and** AAAA from a real attached client:

```bash
# On a capable attached test client, explicitly query its actual hotspot DNS IP.
dig @CURRENT_HOTSPOT_DNS_IP hub-84eaee7c.colophon.stream A +short
dig @CURRENT_HOTSPOT_DNS_IP hub-84eaee7c.colophon.stream AAAA +short
```

Expected: only the selected reachable hotspot private addresses, no `127.0.0.1`,
`::1`, LAN address or unapproved AAAA. Without approved IPv6, AAAA is empty. Also
test the client's ordinary resolver/browser path; explicit `dig @server` alone
does not prove DHCP/client policy uses it. A synthetic container test exercises
the actual Debian dnsmasq with hub-local loopback hosts present, but does not
prove NetworkManager integration or radio behavior on a Pi.

The hub-local hosts block maps the same FQDN to `127.0.0.1` and `::1` **only on the
hub**. Validate with `foundation.py physical-preflight --config SETTINGS.json` and
the actual local HTTPS physical page. A literal `https://localhost` has the wrong
SAN; HTTP Setup is not a physical-code display. Network clients must never receive
the loopback block. During rollback remove/restore only this stage's owned DNS and
hosts changes, preserving upstream configuration and verifying legacy recovery.

## Deliberate fallback and offline limits

If an external network/client cannot resolve or reach the private name, select
the hub's managed hotspot deliberately through the existing approved controls.
Do not infer network failure from DNS alone or change the router automatically.
The hub's single Wi-Fi radio may leave the XB8 network to host its hotspot; hub
internet/renewal and hotspot-client internet may then be absent. No concurrent
home-Wi-Fi/hotspot uplink or internet continuity is promised. The XB8 and other
home devices remain unchanged. Returning to the saved network follows existing
network recovery, then requires rechecking private addresses/resolution.

Local play can continue offline only while routing/local DNS, correct clocks and
the certificate's actual validity/trust remain usable. An expired certificate or
bad clock fails sensitive access closed. Restore time/DNS/renewal through approved
maintenance; no warning bypass or HTTP secret fallback. Keep legacy gameplay and
paired display origins unchanged until their separate migration is approved.
