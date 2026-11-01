# InitiativeTracker setup and operations

Start here to assemble a Raspberry Pi hub and satellite, or to update an existing
hub. The owner-observed deployed baseline is **0.1.5** on Debian 13 Trixie ARM64,
with the application and LightDM active and health/database/storage checks passing
on the existing hub. See [the evidence record](docs/setup/evidence-and-gaps.md)
for verification limits. This guide was
reconciled with `master` commit `0a7e1a7ac86480b012fcfc46702d1b2d396a3bff`
(merged N9/0.1.6 source and branding documentation), plus the private HTTPS
checkpoint on this task branch. The reported device installation remains 0.1.5.

**A release package is not a complete device image.** Merged source includes an
[automated fresh-device installer](docs/setup/automated-installation.md), including
the recovered original graphical updater sources and separately derived runtime
safety changes. It is validated with disposable fixtures
and a Debian container, pending physical fresh-Pi acceptance; it is not an
installed upgrade or hardware acceptance result. Do not run a fresh installer on
an existing working hub or satellite. Existing-hub component maintenance has a
[separate backed-up approval-gated procedure](hub-updater/EXISTING-HUB-UPGRADE.md).
Complete hardware acceptance before handover.

## Choose your route

| Task | Guide |
| --- | --- |
| Preview/install a fresh hub or satellite automatically | [Automated installation and rollback checks](docs/setup/automated-installation.md) |
| Review a backed-up component-only upgrade for an existing hub | [Separate approval-gated maintenance procedure](hub-updater/EXISTING-HUB-UPGRADE.md) |
| Review N9 application branding and fresh/existing desktop loading artwork | [Pinned artwork paths, copy/backup/rollback and compatibility gates](docs/setup/branding.md) |
| Update an already configured hub | [Canonical hub release procedure](docs/setup/hub-release.md) |
| Image a new Pi and install the hub | [Fresh hub installation](docs/setup/fresh-hub.md) |
| Prepare private trusted HTTPS, Cloudflare issuance and port 443 | [Approval-gated HTTPS stages and rollback](docs/https/private443-deployment.md) |
| Install hub networking and understand fallback | [Hub networking](docs/setup/networking.md) |
| Set up a browser-only satellite | [Fresh satellite installation](docs/setup/satellite.md) |
| Diagnose or restore a device | [Troubleshooting and recovery](docs/setup/recovery.md) |
| Prove that installation works | [Acceptance checklist](docs/setup/acceptance.md) |
| Check evidence and outstanding installer work | [Verification record and gaps](docs/setup/evidence-and-gaps.md) |

If your release is already built, start at **Validate and stage** in the release
guide. Do not rebuild merely to follow this documentation. Never use the
first-install bootstrap as an updater or copy files over the live application.

Updating the **application** and hardening the existing hub's **OS updater** are
separate operations. The latter needs a backed-up, approval-gated maintenance
procedure in the linked component guide. Follow only reviewed documentation;
do not run a fresh-device installer or copy proposed runtime files onto a working
hub. The original installed behavior and its limitations remain documented here.

## What runs where

The **hub** is an ASP.NET Core/.NET 10 application with SQLite, persistent hub
identity, device trust and Data Protection keys. The versioned application lives
under `/opt/initiativetracker/releases/`; `/opt/initiativetracker/current` selects
the active version. `/var/lib/initiativetracker` survives release changes. The
`initiativetracker` system account runs the application, separately from the Linux
desktop/SSH account.

The **satellite** can be a Raspberry Pi running Chromium, displaying the hub's
`/display` page. It does not require the .NET application, its own database, or a
copy of hub secrets. Browser cookies and the hub's device registry preserve trust.
Automatic satellite Wi-Fi following is future N8 work, not a feature of 0.1.5.

The hub's restricted root socket broker performs allowlisted NetworkManager
operations for authenticated DM controls. Independent systemd timers handle
supervised transition recovery, return-to-Wi-Fi recovery and boot fallback. A
single Wi-Fi radio changes networks; connected clients must reconnect after a
switch. Existing encounter data persists, but uninterrupted connectivity is not
guaranteed.

## Before you start

Use a Pi 5 hub with cooling, reliable storage (a good microSD or supported SSD), a
case, and the specified Waveshare 10.1-inch **1280×800** touchscreen. A Pi 4 or Pi 5
is a practical browser-satellite choice; this is a recommendation, not a measured
minimum. Use the official 27 W USB-C supply for Pi 5 and the official supply
appropriate to other models. Check the screen's separate power needs. See the
[official hardware/power guidance](https://www.raspberrypi.com/documentation/computers/raspberry-pi.html).

Have another computer with PowerShell, Git, .NET 10 SDK, `tar`, and OpenSSH/SCP for
release work; a trusted LAN; and temporary local keyboard/mouse or Ethernet access
while commissioning. The finished units should work without a keyboard, but do
not remove recovery access before testing cold boot and network rollback.

Examples use placeholders such as `PI_USER`, `HUB_ADDRESS`, and `SAVED_UUID`.
Replace them with your own values; never assume a default `pi` account. Commands
marked **Windows PowerShell** run on the workstation; **Pi Bash** commands run on
the intended Pi. Commands in these guides are instructions for an operator, not a
record of actions executed during this documentation task.

## Evidence labels

- **Source-verified:** checked against the baseline and task-branch source above; does not prove installed
  files, radio behavior, permissions or a physical touchscreen work.
- **Previously observed:** reported deployment/audit evidence supplied by the
  owner, including successful graphical updating and external Wi-Fi after reboot.
  These observations were not repeated in this task.
- **Template / unverified on hardware:** concrete commissioning instructions or
  checks that still need execution on a fresh device. Stop at a missing component
  rather than substituting an invented updater.

Keep passwords, pairing tokens, QR payloads, production configuration, databases,
certificates, browser profiles and private keys out of Git, screenshots and PRs.
Store private backups separately from release artifacts. Read only selected
configuration metadata when diagnosing; never use `nmcli --show-secrets` in a
shared transcript.

## N9 branding and startup artwork

The bundled blue phoenix is `InitiativeTracker.Web/wwwroot/images/colophon.png`.
The app archive places it at `wwwroot/images/colophon.png` in each versioned hub
release. Fresh installers separately copy it to the selected desktop account's
`~/.local/lib/initiativetracker-hub/colophon.png` or
`~/.local/lib/initiativetracker-satellite/colophon.png`. Application updates and the
three-script hub component upgrader do not copy these desktop assets.

Use the [single branding procedure](docs/setup/branding.md) for exact hashes,
actual fresh installer modes, Windows PowerShell transfer and approval-gated Pi
SSH Bash inspection/copy/backup/rollback. Older launchers/satellite wrappers must
stop for a separately reviewed component upgrade; copying a PNG cannot add N9
loading support. Preserve Labwc/updater order, Chromium profiles and pairing.
No restart, reboot, app start or 0.1.6 migration is authorized by these docs.

Customers can replace the application logo/name through **secure DM appearance
settings** after trusted HTTPS, persistent storage and the migration are separately
commissioned, without editing source. This does not replace desktop artwork.
Default characters remain the generic fantasy silhouette, with a neutral banner;
the phoenix is never the default character portrait. Desktop loading pages/dialogs
are distinct from [optional true Linux boot splash](docs/N9-optional-boot-splash.md),
which is not installed by N9. Follow the [single 0.1.6 deployment/rollback plan](docs/https/0.1.6-integration-release-plan.md).

## Optional shared trusted HTTPS checkpoint

The disabled-by-default [shared HTTPS foundation](docs/https/architecture-security.md) provides direct browser TLS, protected certificate activation/renewal orchestration and local DNS preflight. Read the [certificate/DNS setup guide](docs/https/certificate-dns-setup.md), [staged migration/rollback plan](docs/https/migration-rollback.md) and [validation limits](docs/https/validation.md) before requesting any installation. It does not choose a domain, install a DNS/ACME backend, change either Pi, enable redirects or install the merged N9 application. N8 remains a separate hardware-validation checkpoint.

The owner-confirmed Cloudflare/`colophon.stream` setup is being prepared in
[private port-443 deployment](docs/https/private443-deployment.md), with a separate
[XB8/client, hotspot and physical-screen resolution guide](docs/https/private-name-resolution.md).
Review [443 software evidence and physical acceptance limits](docs/https/private443-validation.md).
The normal target is `https://hub-84eaee7c.colophon.stream`. The XB8 remains
unchanged: external-network support requires a verified client-specific resolver
path; hotspot DNS and hub-only loopback mapping have different scopes. Independent transport
review, separate installation approvals and physical acceptance remain required.
