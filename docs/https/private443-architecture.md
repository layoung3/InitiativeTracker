# Private port-443 HTTPS: design review

Base: master `d9470510eba7af584d5c8f8ba05250d275979a62` (source version 0.1.6).
The working Debian 13 ARM64 hub remains 0.1.5. This new task branch does not alter
the merged PR #6 branch, either Pi, DNS, packages, certificates or live services.
Canonical SetupReadme/docs/setup branding work belongs to the concurrent task;
this task initially owns HTTPS code/tests, docs/https and its new deployment guide.

## Confirmed owner inputs

- Domain `colophon.stream`, Cloudflare DNS. The owner already holds the API token;
  it must never be requested in chat, displayed, committed or passed in argv.
- Existing HTTPS name `hub-84eaee7c.colophon.stream`; normal URL has no port.
- Hub/satellite convention is role plus an approved prefix of its stored GUID.
  Preserve the actual GUID and existing short alias; never use Wi-Fi MACs.
- XB8-T/CGM4981COM is treated as non-cooperative. No router replacement, bridge
  mode, hardware purchase, router DNS change or whole-network DNS assumption.

## Proposed transport decision, before implementation

| Option | Security/operational assessment |
|---|---|
| TLS-terminating proxy | Adds a second key/reload boundary or replaces existing Kestrel TLS; needs a nonforgeable proxy provenance contract. Forwarded Host/Proto/For alone cannot become authority. |
| Ordinary TCP passthrough proxy | Keeps TLS but turns remote peers into loopback at Kestrel, exposing anonymous physical proof if trusted as local. Not acceptable without additional trusted ingress handling. |
| Direct Kestrel 443 | Preserves real peer identity and is simple, but requires a reviewed bind capability or inherited privileged socket. Socket activation must not restart an intentionally stopped app during graphical updater backup/restore. |
| Source-preserving kernel redirect 443 to 5445 | Preferred candidate matching the requested retained listener: one TLS terminator/store/reload, genuine socket peer identity, no app bind capability or forwarded-header trust. New root-owned nftables rules remain separately approval-gated. |

The proposed kernel profile separates canonical authority 443 from backend 5445.
Evaluate kernel original-destination metadata on accepted sockets as an additional
independent direct-backend rejection check; unavailable/malformed metadata must
fail closed. No application route may treat a header as proof of this metadata.
Network templates must support approved private interfaces/addresses only, reject
direct remote 5445, handle IPv4/IPv6 deliberately, and include local OUTPUT rules
for genuine loopback physical access. Never flush NetworkManager/other nft tables.
Any unsupported kernel/path is an explicit failed preflight, not a bypass.

Canonical origin is `https://hub-84eaee7c.colophon.stream`. Exact Host/Origin,
__Host Secure cookies, CSRF and WebSockets remain end-to-end at Kestrel. Existing
5445-origin grants do not transfer: fresh secure enrollment/recovery is required.
Physical proof GET and POST require genuine loopback socket peer/local addresses
AND trusted canonical transport. Internal certificate acknowledgement is a
separate narrowly scoped loopback status probe, retaining peer-leaf fingerprint
and generation checks; backend connection port and HTTP authority must separate.
No direct backend URL may become an alternate private application origin.

## Identity and collision scope

An approved local inventory must detect duplicate role/alias mappings and compare
full stored GUIDs before DNS/certificate setup and client acceptance. Existing
`84eaee7c` stays assigned to its actual GUID. New aliases can use longer GUID
prefixes. A collision stops setup and requires explicit selection of an unused
longer prefix for the conflicting/new device, without reseeding either identity.
There is no claim of global collision detection without an owner-selected registry.
Client acceptance must compare `/system/hub-info`'s full GUID with the inventory,
not just a familiar short label or a trusted certificate. Display-only satellites
do not need browser-server certificates; their future hostname convention is
separate from N8 pinned agent identity/enrollment.

## Resolver scope and XB8 support

| Scope | Supported behavior |
|---|---|
| Hub itself | Exact FQDN to loopback for physical proof; changes only the hub. |
| Private hotspot | Exact-host A/AAAA from the managed local DNS service. Suppress /etc/hosts import so hub loopback answers never leak to phones. Verify from a simulated client. |
| XB8 desktop | Explicit per-client exact-host override/supported split DNS, verified in the actual browser and with ordinary internet intact. No router changes. |
| Selected mobile/client DNS | Only when the client is explicitly configured to use the approved resolver and verified; never assume all phones use a Pi server. Prefer exact-host/split handling over making all internet DNS depend on hub uptime. |
| Unconfigured XB8 phone, strict alternate DNS/VPN | Not automatically supported. Do not disable DNS security or invent a public secondary resolver for a private-only name. |
| Unfamiliar Wi-Fi | Managed hotspot is a separately chosen fallback, not an automatic reaction to DNS failure. Single-radio Wi-Fi cannot be assumed to retain home uplink/internet while serving the hotspot. |

Public ACME TXT and private runtime A/AAAA are separate. No wildcard public DNS,
private addresses in public DNS, public inbound ports, router forwarding, global
rebinding bypass or certificate-warning bypass is introduced. Stale address sets
and AAAA answers fail preflight; client rollback removes only this task's exact
mapping and verifies original DNS/internet behavior.

## Debian backend profile

Official Trixie packages currently provide Certbot/python3-certbot
`4.0.0-2+deb13u1`, Cloudflare plugin `4.0.0-1`, python3-acme `4.0.0-1`, and
python3-cloudflare `2.20.0-1`. Qualify an explicit Debian-13/Certbot-4/Cloudflare-4
profile using actual package CLI/parser behavior in disposable tests, keeping the
existing reviewed 5.9 profile separate. No unmanaged global pip or mixed plugin
families. Capture signed APT candidate/provenance/version/license receipts and
permit reviewed Debian revision updates rather than permanent package holds.

The native profile may admit only the exact root-owned Debian vendor cli.ini
SHA-256 `c0d5962eea9582f552fd2a235e0c0c3869c6c84c20359c8926eee8809d4d6872`
(two benign renewal/log settings), with an explicit bounded log-backup CLI
override. Any modification, symlink, other ambient config or use under the legacy
5.9 profile is still rejected. Actual installed parser tests must demonstrate
additive defaults and rejection of a modified vendor file. This narrowly scoped
exception requires security review; it is not a claim that --config isolates files.

Certbot's package timer may enable/start during installation. Future install
commands must preserve existing state and explicitly prevent unintended vendor
timer/cron activation; plain apt install is not an inert first stage. Smallest
first stage should stage reviewed private tools/config only, with no app start or
migration; package setup, token entry, staging validation, production issuance,
certificate installation, renewal service and app/network activation remain
independently approved stages with receipts and rollback.

## Primary sources

- [Debian certbot](https://packages.debian.org/trixie/certbot),
  [Cloudflare plugin](https://packages.debian.org/trixie/python3-certbot-dns-cloudflare),
  [acme](https://packages.debian.org/trixie/python3-acme),
  [Cloudflare library](https://packages.debian.org/trixie/python3-cloudflare).
- [Cloudflare plugin credentials](https://certbot-dns-cloudflare.readthedocs.io/en/stable/):
  zone-scoped DNS Edit tokens, file credentials and propagation options.
- [Certbot 4 parser](https://raw.githubusercontent.com/certbot/certbot/v4.0.0/certbot/certbot/_internal/cli/helpful.py):
  preserve additive ambient-config protections, not an empty-config assumption.
- [nftables NAT](https://wiki.nftables.org/wiki-nftables/index.php/Performing_Network_Address_Translation_(NAT)):
  stateful destination redirect in prerouting/output.
- [Kestrel socket feature](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.connections.features.iconnectionsocketfeature?view=aspnetcore-10.0),
  [Socket raw options](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket?view=net-10.0).
- [systemd sockets](https://www.freedesktop.org/software/systemd/man/latest/systemd.socket.html):
  socket activation/dependencies require updater stop-safety review.
