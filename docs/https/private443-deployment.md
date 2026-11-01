# Private HTTPS deployment: approval-gated operator plan

**Draft PR #8; no stage is authorized by this document.** The existing hub is
Debian 13 ARM64, running 0.1.5. Source master is 0.1.6. Neither Pi has been
accessed or changed. The [443 architecture](private443-architecture.md) now has an
opt-in source implementation; independent review and exact-head software/physical
acceptance are required. Stop before any stage whose software/physical
acceptance receipt or separate owner approval is missing.

## Fixed choices and decisions still needed

Use `hub-84eaee7c.colophon.stream`, Cloudflare DNS and the owner's existing token.
The eventual normal address is `https://hub-84eaee7c.colophon.stream`, without a
port. DNS-01 needs outbound internet, not inbound public connectivity. Public
ACME TXT is separate from private runtime A/AAAA. Do not publish private addresses,
wildcards, tunnels or port forwarding. Public certificate hostnames are visible
in Certificate Transparency.

Still record the full existing stored hub GUID, account contact and CA terms
approval, token's actual zone/scope, whether challenge delegation is needed,
renewal ownership/alerts, and the supported clients/current private addresses.
Do not ask for or record the token value. Cloudflare's standard plugin uses a
zone-scoped DNS Edit token; zone scope is broader than one TXT record. A separate
challenge zone may reduce authority, but a CNAME/delegation is not assumed to work
automatically with this plugin: approve and staging-test the exact arrangement.
Never replace the token with a global API key.

One hub gets one leaf key/name and private issuer state. Display-only satellites
need no server certificate. Future browser-serving satellites use approved
`satellite-<GUID-prefix>.colophon.stream` names, with their own keys/scopes;
browser certificates do not replace N8's pinned agent identity.

## Stage 0: inspect, select and back up; no changes

Record 0.1.5 health, identity, current version, updater components, desktop user,
Chromium profile, app unit/drop-ins and persistent data from the existing
[release procedure](../setup/hub-release.md). Do not run a bootstrap, start new
binaries, run a migration, or use the installer as a diagnostic. Establish the
approved whole-data/keys/media/pairing backup and private config receipts before
later application work. Certificates and DNS tokens need separate encrypted
backups outside ordinary application rollback.

On the hub, future operator read-only checks include:

```bash
cat /etc/os-release
dpkg --print-architecture
ps -p 1 -o comm=
test -d /run/systemd/system
systemctl is-active initiativetracker.service
systemctl is-enabled certbot.timer certbot.service || true
systemctl is-active certbot.timer certbot.service || true
dpkg-query -W -f='${Package}\t${Version}\t${db:Status-Status}\n' \
  certbot python3-certbot python3-certbot-dns-cloudflare python3-acme python3-cloudflare || true
ip -j address show
timedatectl status
```

Do not dump production JSON, databases, profiles, credentials or private keys to a
shared transcript. Stop for review if Certbot/issuer state already exists; the
fresh-package steps below must not alter a pre-existing CA account or automation.
Capture an exact file backup/absence receipt before every subsequent changed path.

## Stage 1: smallest safe installation — reviewed tools/config only

This is the recommended first separately approved installation on 0.1.5: stage
root-owned Python tools and a disabled, secret-free configuration. No app unit,
network, certificate, database or startup change is needed. These commands are
future **Pi Bash**, after verifying the approved source archive/commit out of band:

```bash
REVIEWED_SOURCE=/absolute/path/to/verified-source
test -f "$REVIEWED_SOURCE/https-infra/foundation.py" || exit 1
sudo install -d -o root -g root -m 0755 /usr/local/lib/initiativetracker-https
sudo install -o root -g root -m 0644 "$REVIEWED_SOURCE"/https-infra/*.py \
  /usr/local/lib/initiativetracker-https/
sudo install -d -o root -g root -m 0700 /etc/initiativetracker-https
sudo install -o root -g root -m 0600 \
  "$REVIEWED_SOURCE/https-infra/templates/cloudflare-debian13.example.json" \
  /etc/initiativetracker-https/settings.json
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py issue \
  --config /etc/initiativetracker-https/settings.json
```

The last command **must fail** until a real owner contact and explicit review
flags are supplied; it must not contact a CA/DNS API even with valid settings
without `--apply`. A failure here is not permission to change unrelated files.
Verify root ownership/modes and unchanged app health/version. Rollback restores
only these paths from their private stage-0 backup, or removes only files recorded
absent before this stage. Do not recursively delete a shared directory or any
state created by a later stage.

## Stage 2: qualify/install native Debian packages; prevent ambient automation

Use Debian's coherent Trixie packages, not unmanaged global pip. The qualified
minimum set is Certbot/python3-certbot `4.0.0-2+deb13u1`, plugin/acme `4.0.0-1`,
Cloudflare library `2.20.0-1`. The explicit profile permits later Debian packaging
revisions of these same upstream versions, rejecting a new/mixed upstream family.
No package holds are required. A newer upstream needs qualification, not bypass.
Confirm signed Debian APT sources/candidate provenance and security updates;
HTTPS downloads alone do not prove signed APT provenance.

After separate package-stage approval, **on a systemd hub with no existing Certbot
installation/state or automation**, preserve the unit-state receipt and block the
vendor service and timer before package installation. Debian maintainer scripts
can start/enable its timer. A simulated install is not a no-side-effects guarantee
for a subsequent real install.

```bash
test "$(ps -p 1 -o comm= | tr -d ' ')" = systemd || exit 1
test -d /run/systemd/system || exit 1
test ! -e /etc/letsencrypt || exit 1
test ! -e /var/lib/letsencrypt || exit 1
test ! -e /var/log/letsencrypt || exit 1
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/package_masks.py mask --apply || exit 1
sudo apt-get update || exit 1
apt-cache policy certbot python3-certbot python3-certbot-dns-cloudflare python3-acme python3-cloudflare
sudo apt-get --simulate install --no-install-recommends certbot python3-certbot-dns-cloudflare openssl ca-certificates nftables iproute2 curl || exit 1
# STOP: approve the displayed signed-repository candidate versions and transaction.
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/package_masks.py verify --persistent || exit 1
sudo apt-get install --no-install-recommends certbot python3-certbot-dns-cloudflare openssl ca-certificates nftables iproute2 curl || exit 1
sudo systemctl disable certbot.timer || exit 1
sudo systemctl mask certbot.service certbot.timer || exit 1
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/package_masks.py verify --persistent || exit 1
systemctl is-active certbot.service certbot.timer || true
systemctl is-enabled certbot.service certbot.timer || true
dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' \
  certbot python3-certbot python3-certbot-dns-cloudflare python3-acme python3-cloudflare
```

Both units are persistently masked and verified **before APT**, and rechecked
immediately before installation. Any mask/check failure exits before installing;
partial package failure remains fenced across reboot. Retain masks while this
foundation's distinct timer owns renewal. Check the
transaction for shared-runtime updates or restarts of existing services; stop for
a reviewed maintenance plan if either would affect the working hub. OpenSSL's
CLI and the maintained public CA bundle are required by certificate validation.
Check the
installed `/etc/cron.d/certbot`: Debian's qualified entry skips when
`/run/systemd/system` exists; do not apply these steps on a non-systemd image or
silently replace an existing cron. Verify masks after reboot/update. Do not
activate the vendor timer alongside the foundation service. Preserve installed
copyright/license notices under `/usr/share/doc/{certbot,python3-certbot,
python3-certbot-dns-cloudflare,python3-acme,python3-cloudflare}/copyright`, package
versions and APT transaction/security-review receipts. Dependency/license audit
is required for the actual ARM64 installation; cloud parser tests do not prove it.

The adapter admits only the exact reviewed root-owned Debian vendor `cli.ini`
hash described in the architecture, overriding its log-backup setting. Modified
vendor files, user defaults and hooks fail closed. Do not delete them to make a
check pass: isolate/review the existing environment. The 5.9 profile still rejects
all ambient config. Package rollback is an explicit reviewed APT removal/version
transaction with state retained; never purge CA state automatically. Restore only
the previously recorded mask/enablement states, not a blanket `unmask --now`.

## Stage 3: private issuer directories, token entry and approval flags

After separate private-file approval (still no application or certificate change):

```bash
sudo install -d -o root -g root -m 0700 /var/lib/initiativetracker-issuer
sudo install -d -o root -g root -m 0700 /var/lib/initiativetracker-issuer-staging
# Real terminal, hidden input; do not substitute echo, argv, environment or a pipe.
sudo /usr/bin/python3 -I /usr/local/lib/initiativetracker-https/cloudflare_credentials.py \
  --path /var/lib/initiativetracker-issuer/cloudflare.ini
sudo install -o root -g root -m 0600 \
  /var/lib/initiativetracker-issuer/cloudflare.ini \
  /var/lib/initiativetracker-issuer-staging/cloudflare.ini
sudo stat -c '%U %G %a %n' /var/lib/initiativetracker-issuer/cloudflare.ini \
  /var/lib/initiativetracker-issuer-staging/cloudflare.ini
```

No token value is printed or put in history/process arguments. Do not screen-share
or paste private Certbot logs; they remain root-only and outside releases. Rotate
with this helper's explicit `--replace` after provider-side review/revocation;
rotate the staging copy too. The app never receives issuer group membership.

After owner approval of **actual token scope**, contact and CA terms, edit the
private settings locally (no token belongs in JSON), setting the real `email`,
`scope_reviewed: true` and `terms_approved: true`. Make a separate staging copy:

```bash
sudo /usr/bin/python3 -I - <<'PY'
import json, os
from pathlib import Path
source = Path('/etc/initiativetracker-https/settings.json')
value = json.loads(source.read_text())
assert value['acme']['scope_reviewed'] is True and value['acme']['terms_approved'] is True
assert value['acme']['email'] != 'OWNER_SELECTED_ACCOUNT_CONTACT'
value['acme'].update(
    state_directory='/var/lib/initiativetracker-issuer-staging',
    credentials_file='/var/lib/initiativetracker-issuer-staging/cloudflare.ini',
    directory_url='https://acme-staging-v02.api.letsencrypt.org/directory')
path = Path('/etc/initiativetracker-https/staging.json')
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
with os.fdopen(fd, 'w') as stream:
    json.dump(value, stream, indent=2); stream.write('\n'); stream.flush(); os.fsync(stream.fileno())
PY
```

The stage copy refuses overwrite; do not casually delete existing state. These
commands do not grant permission to use the API. Retain any material created
later until owner-approved secure disposal; a deleted token file does not revoke
its Cloudflare authority.

## Stage 4: ACME staging validation — explicit DNS API approval

```bash
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py stage \
  --config /etc/initiativetracker-https/staging.json
# Separate approval: this creates/removes public challenge TXT via Cloudflare.
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py stage \
  --config /etc/initiativetracker-https/staging.json --apply
```

The real Certbot `certonly --dry-run` uses the explicit staging CA and DNS plugin,
with separate account/work/log state; no browser certificate is exported or
activated. The permanent staging-only marker rejects reuse for production. Check
normal challenge cleanup in the selected public authoritative zone without
disclosing credentials. A failure/timeout requires private inspection for stale
TXT records and provider/account access; never broaden the token automatically.
Staging success is not browser trust or production issuance acceptance.

## Stage 5: production issuance/export — separate API/CA approval

```bash
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py issue \
  --config /etc/initiativetracker-https/settings.json
# Separate production issuance approval:
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py issue \
  --config /etc/initiativetracker-https/settings.json --apply
```

This validates chain, single exact SAN, EKU, key match and current validity before
publishing a private immutable export receipt. It does not install the certificate,
start the app or change networking. Exports/account/keys stay root-only outside
releases. Do not publish or attach them as CI/release artifacts. A failed issuance
leaves the running 0.1.5 installation untouched. Retain successful state and account
for renewal; do not reissue repeatedly to work around setup mistakes/rate limits.

## Stage 6: graphical updater, application and database — distinct approvals

Preserve the [consolidated 0.1.6 gates](0.1.6-integration-release-plan.md): corrected
graphical updater components are outside publish output and need their own
approval; N9's automatic EF migration needs approval **before** the graphical
update starts the app. Keep `/var/lib/initiativetracker/Media`, database,
`DataProtection-Keys`, identity/pairing and secure state consistent in backups.
Automatic/manual whole-data restoration must fence stale secure grants before
any app restart. Do not roll the guarded updater back to an unfenced version.

Use the existing graphical versioned-update procedure with the approved final
archive/manifest/hash; do not manually switch release symlinks, bootstrap an
existing hub, or start the new app as a diagnostic. Initially leave TLS disabled,
HTTP health/gameplay/discovery and satellite origins unchanged. Verify the expected
0.1.6 version, additive migration, identity/pairing, media modes and combat before
the separately approved TLS configuration below. Installing files does not approve
starting a binary against production data or running the migration.

## Stage 7: private TLS store and app listener — separate configuration approval

The following fresh-store example stops if a store/drop-in already exists; review
and back it up rather than overwriting an earlier installation. Inspect an
existing TLS group and its membership before accepting it. No issuer group or
credentials may be granted to the app. After application/migration and service
configuration approvals:

```bash
set -euo pipefail
REVIEWED_SOURCE=/absolute/path/to/verified-source
getent group initiativetracker-tls || sudo groupadd --system initiativetracker-tls
test ! -e /var/lib/initiativetracker-browser-tls
sudo install -d -o root -g initiativetracker-tls -m 2750 /var/lib/initiativetracker-browser-tls
sudo install -d -o root -g root -m 0755 /etc/systemd/system/initiativetracker.service.d
test ! -e /etc/systemd/system/initiativetracker.service.d/browser-tls443.conf
sudo install -o root -g root -m 0644 \
  "$REVIEWED_SOURCE/https-infra/templates/browser-tls443.conf.example" \
  /etc/systemd/system/initiativetracker.service.d/browser-tls443.conf
sudo systemctl daemon-reload
# This restart requires the app/database approvals above, not just file approval.
sudo systemctl restart initiativetracker.service
systemctl is-active --quiet initiativetracker.service
```

Kestrel still binds unprivileged 5445 and keeps existing HTTP endpoints. No bind
capability, proxy daemon, forwarded-header middleware, redirect or HSTS migration
is introduced. TLS can initially reject handshakes without active material while
the graphical updater's HTTP health path works. Check that path and the retained
legacy origins before continuing. Do not use the old direct-5445 drop-in for the
new canonical authority. Python settings must have `port: 5445, public_port: 443`.

## Stage 8: exact local DNS and owned kernel ingress — network approval

Follow the [scoped resolver guide](private-name-resolution.md) for per-client XB8,
hotspot `no-hosts` isolation and hub-only loopback. No global router/DNS change is
required. Review actual interface names/current addresses; do not copy fixture
addresses into production. Source-preserving destination **port** translation
keeps the destination address and peer intact for IPv4/IPv6. Kestrel captures
Linux original-destination metadata before TLS. Missing/malformed/wrong-family
metadata cannot grant canonical or physical authority. Physical also requires
original loopback destination port 443 and genuine loopback peer/local addresses.

Create root-owned 0600 `/etc/initiativetracker-https/bindings.json` only after an
absence/backup receipt, containing the selected current private bindings, e.g.:

```json
[{"interface":"OWNER_INTERFACE","address":"OWNER_CURRENT_PRIVATE_IP"}]
```

Only advertise ULA IPv6 if it is assigned, reachable and accepted by clients.
Generated rules always include hub-only IPv4/IPv6 loopback OUTPUT; no public/global
ingress binding is accepted. The renderer checks selected addresses against the
actual interface, refuses ambiguous/duplicate/unassigned bindings and emits only
the dedicated `inet initiativetracker_browser443` table. Never flush rulesets or
NetworkManager tables. Check for an existing port-443 listener/table before the
first install; hold on any conflict. Under separate network-stage approval:

```bash
set -euo pipefail
REVIEWED_SOURCE=/absolute/path/to/verified-source
IT_443_LISTENERS=$(sudo /usr/bin/ss -H -ltn 'sport = :443')
test -z "$IT_443_LISTENERS"
sudo /usr/sbin/nft -j list tables | /usr/bin/python3 -I -c \
  'import json,sys; d=json.load(sys.stdin); assert not any(x.get("table",{}).get("family")=="inet" and x["table"].get("name")=="initiativetracker_browser443" for x in d["nftables"])'
IT_443_RULES=$(mktemp)
trap 'rm -f "$IT_443_RULES"' EXIT
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/private443.py \
  --bindings /etc/initiativetracker-https/bindings.json > "$IT_443_RULES"
sudo /usr/sbin/nft --check -f "$IT_443_RULES"
sha256sum "$IT_443_RULES"
# STOP: review exact interface/address rules and retain their approved hash.
sudo install -o root -g root -m 0600 "$IT_443_RULES" /etc/initiativetracker-https/private443.nft
sudo install -o root -g root -m 0644 \
  "$REVIEWED_SOURCE/https-infra/templates/private443.service.example" \
  /etc/systemd/system/initiativetracker-browser443.service
sudo systemctl daemon-reload
sudo systemctl start initiativetracker-browser443.service
systemctl is-active --quiet initiativetracker-browser443.service
rm -f "$IT_443_RULES"
trap - EXIT
```

The oneshot does not start/restart the app. It has only the network-admin
capability; the app gets no added capabilities. Rules deny direct remote 5445 and
unconfigured 443 ingress. Other firewall chains can still reject traffic: do not
globally bypass them. Address/interface changes require stopping sensitive use,
rendering/reviewing current bindings and the corresponding resolver change; a
controlled restart removes/reinstalls **only this table**. No privileged automatic
DHCP/DNS rewriting daemon is introduced. Legacy recovery remains independently
operational. Enable this unit at boot only after the following acceptance gate.

## Stage 9: certificate promotion and trusted-origin acceptance

Record the previous non-secret `active.json` generation (or absence) privately.
After separate certificate-installation approval:

```bash
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py activate \
  --config /etc/initiativetracker-https/settings.json
# Separate promotion approval:
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py activate \
  --config /etc/initiativetracker-https/settings.json --apply
curl --fail --silent --show-error \
  --resolve hub-84eaee7c.colophon.stream:443:127.0.0.1 \
  https://hub-84eaee7c.colophon.stream/system/https-status
curl --fail --silent --show-error --output /dev/null \
  --resolve hub-84eaee7c.colophon.stream:443:127.0.0.1 \
  https://hub-84eaee7c.colophon.stream/system/health
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py physical-preflight \
  --config /etc/initiativetracker-https/settings.json
```

No `-k`, private CA or warning bypass. `--resolve` affects only this curl call;
the separate physical preflight/browser verifies the actual hub-local resolver.
The activation probe connects backend loopback 5445 with canonical SNI/Host 443,
checks the actual peer-leaf fingerprint and served generation, and commits or
rolls back the manifest. Only exact GET/HEAD `/system/https-status` without query
or path base has the direct-loopback exception; it never sets general trust or
physical authority. `/system/health` and all application/proof routes still need
validated canonical ingress, so status alone is not a full ingress acceptance.

Verify from each selected client: expected A/AAAA/full GUID, normal no-port URL,
public browser trust and unrelated internet access on XB8. Verify from the actual
hub graphical browser that the HTTPS physical screen loads; remote GET/POST must
be forbidden, even with spoofed forwarding headers. Do not output/photograph its
codes. Establish fresh DM physical recovery and player/display approval, ownership,
CSRF, banners/portraits, real-time combat/WebSockets and retained legacy gameplay.
An old 5445-origin cookie must not authorize 443. Preserve Chromium profiles and
pairing; no URL/token QR shortcut grants secure authority. Only then, with boot
activation approval:

```bash
sudo systemctl enable initiativetracker-browser443.service
```

## Stage 10: automatic renewal and monitoring — separate service approval

The owner-approved production issuer retains CA account/token state; the app
cannot read it. The root renewal service's only browser-key access is the dedicated
TLS store. It uses the same adapter/export/validate/atomic activation pipeline,
retains valid last-known-good material on failure, and verifies the actual served
leaf/generation. It does not restart the app or affect N8 agent pins.

After staged success, actual production trust and separate automation approval:

```bash
set -euo pipefail
REVIEWED_SOURCE=/absolute/path/to/verified-source
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/package_masks.py verify --persistent
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py renew \
  --config /etc/initiativetracker-https/settings.json
sudo install -o root -g root -m 0644 \
  "$REVIEWED_SOURCE/https-infra/templates/initiativetracker-certificate-renew.service.example" \
  /etc/systemd/system/initiativetracker-certificate-renew.service
sudo install -o root -g root -m 0644 \
  "$REVIEWED_SOURCE/https-infra/templates/initiativetracker-certificate-renew.timer.example" \
  /etc/systemd/system/initiativetracker-certificate-renew.timer
sudo systemctl daemon-reload
# Separate first real renewal/API/promotion approval; backend may decide not due.
sudo systemctl start initiativetracker-certificate-renew.service
systemctl show initiativetracker-certificate-renew.service -p Result -p ExecMainStatus
sudo systemctl enable --now initiativetracker-certificate-renew.timer
systemctl list-timers initiativetracker-certificate-renew.timer
```

Review/backup existing units first; do not overwrite a prior service blindly.
Verify directory/file modes and group inheritance: issuer 0700/files0600;
TLS store/generations2750 root:TLS-group; PFX/manifest0640. No keys/tokens in
appsettings, release archives, startup logs, URLs or command arguments. Private
Certbot logs/account backups stay root-only. A failed renewal must leave the
previous served valid leaf intact; inspect only static service result/status in
shared evidence, and private logs locally. Recheck the served certificate and
generation after a due renewal, not just the exported file. Configure an
owner-selected alert/check with repair time based on actual expiry/lifetime and
offline travel; the timer alone does not send an alert. Issuance/renewal needs
outbound CA/Cloudflare/DNS access; offline play has the documented trust/time limit.

## Stage 11: exact rollback by changed component

Keep separate before/after file, unit state, generation, source/hash and backup
receipts; stop if the current state differs unexpectedly. Never restore broad
system/network directories over later unrelated changes.

| Failure/change | Controlled rollback |
|---|---|
| Issuer/API/staging | Stop automation; keep private state for diagnosis/retry. Inspect stale challenge TXT privately; separately approve exact cleanup/token revocation. Do not delete a CA account or broaden scope automatically. |
| Candidate certificate | Failed activation retains/restores the old manifest and verifies its served fingerprint. On unconfirmed recovery hold sensitive use. For explicit manual rollback, use the generation command below; expired/untrusted material is rejected. |
| Renewal service | `systemctl disable --now initiativetracker-certificate-renew.timer`; ensure the service is inactive before file restoration. Restore only its saved units/config/state. Keep the vendor masks while independent foundation renewal ownership exists. |
| Kernel ingress | `systemctl disable --now initiativetracker-browser443.service`; verify its dedicated table is absent and unrelated tables unchanged. Direct remote 5445 remains rejected by the app. Restore only this stage's unit/rules/bindings receipts. Never flush all rules. |
| Host/client/hotspot DNS | Remove only the owned mapping/snippet or restore exact unchanged backup; preserve upstreams/router and recheck internet/legacy recovery. Never distribute hub loopback to clients. |
| App TLS profile or canonical-origin rollback | Stop app, preserve private data/config, fence secure grants with the reviewed helper linked below, restore only the approved drop-in and restart under app/DB approval. Origin changes require fresh physical recovery; do not revive old 5445 tickets by toggling configuration. |
| App/database/media rollback | Use the guarded graphical updater and consolidated stopped-data restore, preserving DB/Media/keys/identity consistency and fencing secure grants before restart. Do not downgrade via manual symlink manipulation or roll back current valid issuer/browser state with app data. |

Explicit certificate rollback, only after approval and a recorded valid prior
generation (never guess or read secrets to choose it):

```bash
IT_PREVIOUS_TLS_GENERATION=APPROVED_PREVIOUS_32_HEX_GENERATION
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py rollback \
  --config /etc/initiativetracker-https/settings.json --generation "$IT_PREVIOUS_TLS_GENERATION"
# Separate rollback approval:
sudo /usr/bin/python3 -E -s /usr/local/lib/initiativetracker-https/foundation.py rollback \
  --config /etc/initiativetracker-https/settings.json --generation "$IT_PREVIOUS_TLS_GENERATION" --apply
```

For origin/data rollback use the **exact hash-checked, durable fence command** in
[the consolidated recovery plan](0.1.6-integration-release-plan.md#failure-and-exact-rollback-decisions)
while the app is stopped, targeting the restored/current approved private data
tree as appropriate, before any restart. Do not merely delete secure state or
remove a fence, reseed identity or replace Data Protection keys. Fresh physical
recovery establishes the new epoch. A manual stale backup restored without the
fence can revive revoked authority; no external anti-rollback anchor is claimed.

No HTTP redirect, legacy-cookie promotion, satellite-origin rewrite, Chromium
profile reset or network-agent enrollment is implicit. A changed canonical origin
requires fresh secure authorization. Certificate rollback must validate current
trust/time and served fingerprint/generation; never promote an expired archive or
downgrade sensitive routes to HTTP. DNS/clock/certificate failures follow the
[failure table](certificate-dns-setup.md#offline-and-failure-behavior).

Branding is separately prepared in [PR #7's reviewed guide](https://github.com/layoung3/InitiativeTracker/blob/a193c5eca8b5f626dc62dc81bf72b961f950cf8d/docs/setup/branding.md).
It covers optional Colophon startup assets outside app releases, preserves the
graphical updater/Labwc/Chromium order and neutral character/banner defaults. OS
boot branding remains a distinct optional approval; no branding is installed here.
