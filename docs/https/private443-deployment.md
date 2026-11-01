# Private HTTPS deployment: approval-gated operator plan

**Draft PR #8; no stage is authorized by this document.** The existing hub is
Debian 13 ARM64, running 0.1.5. Source master is 0.1.6. Neither Pi has been
accessed or changed. The [443 architecture](private443-architecture.md) is still
under review; its transport, activation and renewal integration are not yet a
deployment-ready implementation. Stop before any stage whose software/physical
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
sudo systemctl mask --runtime certbot.service certbot.timer
sudo apt-get update
apt-cache policy certbot python3-certbot python3-certbot-dns-cloudflare python3-acme python3-cloudflare
sudo apt-get --simulate install --no-install-recommends certbot python3-certbot-dns-cloudflare openssl ca-certificates
# STOP: approve the displayed signed-repository candidate versions and transaction.
sudo apt-get install --no-install-recommends certbot python3-certbot-dns-cloudflare openssl ca-certificates
sudo systemctl disable certbot.timer
sudo systemctl mask certbot.service certbot.timer
systemctl is-active certbot.service certbot.timer || true
systemctl is-enabled certbot.service certbot.timer || true
dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\n' \
  certbot python3-certbot python3-certbot-dns-cloudflare python3-acme python3-cloudflare
```

Retain masks while this foundation's distinct timer owns renewal. Check the
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

## Later gates: application, HTTPS activation, renewal and rollback

The 443 ingress implementation/acceptance is still pending architecture review.
Do not apply the old 5445 app drop-in as if it delivered the approved normal URL.
Final commands for ingress installation, certificate activation, automated renewal
and their exact rollback will be added after that implementation passes tests.
Until then, this is an explicit software blocker, not an owner configuration task.

Preserve the [consolidated 0.1.6 gates](0.1.6-integration-release-plan.md): corrected
graphical updater components are outside publish output and need their own
approval; N9's automatic EF migration needs approval **before** the graphical
update starts the app. Keep `/var/lib/initiativetracker/Media`, database,
`DataProtection-Keys`, identity/pairing and secure state consistent in backups.
Automatic/manual whole-data restoration must fence stale secure grants before
any app restart. Do not roll the guarded updater back to an unfenced version.

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
