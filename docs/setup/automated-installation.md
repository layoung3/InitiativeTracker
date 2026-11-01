# Automated fresh-device installation (source-verified; hardware acceptance pending)

[Setup index](../../SetupReadme.md) · [Manual hub](fresh-hub.md) · [Manual satellite](satellite.md)

For an **existing** hub, use only the separately reviewed
[component upgrade/rollback procedure](../../hub-updater/EXISTING-HUB-UPGRADE.md)
after explicit device-specific approval. These fresh installers always refuse
an existing installation. They are not an existing-hub upgrade mechanism.

Use only on a separately approved **new/disposable commissioning installation**.
The deliberately pinned installer/artwork snapshot is merged N9 source
`d9470510eba7af584d5c8f8ba05250d275979a62` (0.1.6). Master later merged the
branding documentation at `0a7e1a7ac86480b012fcfc46702d1b2d396a3bff`; that
does not change the source/artifact prechecks below. HTTPS tools and configuration
use the independently reviewed source and [stages](../https/private443-deployment.md).
Do not run these commands on the owner's working hub or satellite. Source merging
is not installation, reboot, certificate or production-migration approval. A fresh
hub installation starts the application and runs its automatic migrations; obtain
those approvals before applying, using the [single 0.1.6 plan](../https/0.1.6-integration-release-plan.md).

## Prepare and inspect

Image Raspberry Pi OS **desktop, 64-bit**, Debian 12 or 13, using Pi Imager.
Create an ordinary desktop user, SSH public-key access, correct Wi-Fi country,
and initial network. Start with the OS's working **Labwc + LightDM** desktop,
NetworkManager active, `wlan0`, and local keyboard/Ethernet recovery. Lite, X11,
arbitrary renamed interfaces, and other distributions fail preflight. Install
normal OS security updates first; Python must provide `tarfile.data_filter`.
The hub also checks `iw` AP capability before any installation changes.
Hub preflight also checks the **already-active** `wlan0` profile ID. Supervised
DM switches require `[A-Za-z0-9_.:-]{1,80}`; a common Imager ID such as `Home WiFi`
fails this check even if its network works normally. Installation stops before
changes and preserves connectivity. Review the active profile's UUID locally and,
in an intentional commissioning window with recovery access, rename it to a
unique simple ID such as `IT-Imager-External`, then rerun. The installer never
silently renames or switches an active profile. See the
[manual profile-ID guidance](networking.md#3-configure-saved-external-wi-fi).

Transfer a reviewed source checkout including `scripts`, `hub-updater`,
`hub-bootstrap`, `pi-network`, and
`InitiativeTracker.Web/wwwroot/images/colophon.png` to the new device.
Verify the pin and artwork before inspection. Executable bits are tracked in Git; if your transfer lost them, run `chmod +x scripts/install*.sh`.
From that source checkout, the artwork precheck is **Pi Bash, read-only**:

```bash
test "$(git rev-parse HEAD)" = d9470510eba7af584d5c8f8ba05250d275979a62 || {
  printf '%s\n' 'STOP: source revision mismatch or Git failure; do not commission.' >&2
  exit 1
}
printf '%s  %s\n' 17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1 \
  InitiativeTracker.Web/wwwroot/images/colophon.png | sha256sum --check - || {
  printf '%s\n' 'STOP: missing or incorrect artwork; do not commission.' >&2
  exit 1
}
```

An exported checkout without `.git` must instead have its pinned source/archive
receipt verified on the workstation; do not invent a matching HEAD. The installer
copies the PNG conditionally if present, so stop on a stripped/missing asset.
See [branding paths/modes and existing-device artwork maintenance](branding.md).

Run without sudo to preview; inspection does not write files, ask for passwords,
install packages, change services, or probe a live hub:

```bash
./scripts/install-hub.sh --dry-run
./scripts/install-satellite.sh --inspect
```

Hub releases still follow the existing workstation workflow:

```powershell
dotnet test
dotnet build .\InitiativeTracker.Web
.\scripts\Build-InitiativeTrackerRelease.ps1 -ReleaseNotes "Release notes"
# Then Test-InitiativeTrackerRelease.ps1, Stage-InitiativeTrackerRelease.ps1,
# hash-verify and SCP as in the canonical release procedure.
```

Do not rebuild a previously approved artifact solely to install. The fresh hub
installer requires the trusted Stage manifest and its exact package, verifies
product/platform/version, size and SHA-256, rejects unsafe/duplicate tar members,
and checks the actual publish metadata for the established **self-contained
.NET 10 ARM64** format. Hashes establish copy integrity, not publisher identity.

```bash
./scripts/install-hub.sh --dry-run \
  --manifest "$HOME/it-release-transfer/release-manifest.json" \
  --package "$HOME/it-release-transfer/InitiativeTracker-0.1.6-linux-arm64.tar.gz"

sudo ./scripts/install-hub.sh --desktop-user YOUR_DESKTOP_USER \
  --manifest "$HOME/it-release-transfer/release-manifest.json" \
  --package "$HOME/it-release-transfer/InitiativeTracker-0.1.6-linux-arm64.tar.gz"

sudo ./scripts/install-satellite.sh --desktop-user YOUR_DESKTOP_USER
# Or sudo ./scripts/install.sh to select the role interactively.
```

## Wizard and safeguards

The terminal wizard collects a commissioning hostname, optional external WPA
Wi-Fi, desktop autologin choice, optional named display output/mode, and the
role-specific settings. Hub friendly discovery remains derived from its new
persistent identity; the commissioning hostname is temporary. Satellite asks
for a stable hub origin and expected GUID from `/system/hub-info`, validates
reachability/health/identity before applying, and can save the hub's private
Wi-Fi credentials. It never puts pairing tokens in its URL or configuration.

Password prompts do not echo and reject nonterminal fallback. Secrets are passed
in memory to private keyfiles, never subprocess arguments or printed output.
Hub private-hotspot credentials default to a fresh cryptographically random
24-hex-character password; the optional initial password accepts 8–63 ASCII
letters/numbers. View the credentials privately on the physical Setup screen.
Enterprise Wi-Fi/captive portals are not automated; use the OS network tools.

Two explicit confirmations are needed for a hub: authorization for recovery
gates, and `INSTALL-FRESH` after the operation/disconnection explanation.
Package maintainer scripts can restart services; hostname changes affect
discovery. Keep local/Ethernet recovery throughout. The installer never disables
SSH, reboots automatically, restarts NetworkManager, or activates a Wi-Fi profile.
It loads only newly created profiles; NetworkManager may autoconnect them if the
radio is disconnected. Autoconnect does not preempt an active Wi-Fi connection.
The wizard explains this before confirmation. Reboot may disconnect the operator.

Existing installs, data directories, managed config/units/profile names or
symlinks are refused. A successful repeat is a no-op: it does not erase a
database, regenerate identity/credentials, replace config, or clear browser trust.
Partial installs retain an `in-progress` root-private receipt and refuse retry;
inspect them locally rather than deleting data to get past a check. There is no
force/repair/replace option. Use the graphical updater for existing app releases.

Before replacing desktop autostart, the installer saves a private copy under
`/var/lib/initiativetracker-install/backups/<UTC>/`; it preserves the desktop's
other entries. It also backs up hostname/hosts before hostname operations.
New service/config files are exclusive creations. Root-owned system ancestry
and an installer lock protect managed paths. A receipt records installed paths
and backup location without passwords or pairing material.

## Local loading artwork

The selected account receives the original Colophon PNG beside its local kiosk
components, owner desktop UID/primary GID, file mode 0644. The hub directory remains
0755 because updater provisioning creates it first; satellite directory is 0700.
`kiosk.py` is 0755; satellite `waiting.html` is 0644. The hub uses the PNG in its
Zenity health-wait dialog; satellite HTML references the adjacent PNG and uses a
separate waiting Chromium profile. These are graphical-session loading assets,
not firmware/Linux boot splash. Exact paths, supported hashes and limitations are
in the [single branding guide](branding.md). Do not recursively change home modes.

## Hub behavior and update compatibility

The first app is installed to `/opt/initiativetracker/releases/<version>-linux-arm64`
and selected by `current`; data belongs to the `initiativetracker` account under
`/var/lib/initiativetracker` (0750). No existing live release is overwritten.
Native libraries are installed for the Debian base (`libicu72`/`libssl3` on 12,
`libicu76`/`libssl3t64` on 13, plus C/C++/Kerberos/zlib dependencies). The release
bundles .NET 10; an additional system ASP.NET Core 10 ARM64 runtime is an optional
wizard choice using Microsoft's Debian feed. No SDK is installed. A system runtime
does not patch a self-contained release: release a rebuilt app when needed.

Package references: [Microsoft Debian .NET installation](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian),
[Debian 12 ICU](https://packages.debian.org/bookworm/libicu72),
[Debian 13 ICU](https://packages.debian.org/trixie/libicu76),
[Debian 13 OpenSSL](https://packages.debian.org/trixie/libssl3t64).

The installer provisions the existing socket broker and independent recovery,
return and observer timers. Private AP is always `autoconnect=false`; saved
external profiles use priority 100. Recovery controllers start before gates.
Boot fallback is enabled **for next boot**, after NetworkManager has its normal
opportunity: minimum 60 seconds, scans and bounded eligible-profile attempts.
This is not a universal radio watchdog or an Internet test. A functioning AP is
not silently abandoned when external Wi-Fi appears. See [networking](networking.md).

The selected user's Labwc entry backgrounds the resolved home launcher with
`--supervise` and that user's kiosk config. Its opt-in dispatch starts a
single-instance supervisor, which calls the same launcher without arguments.
The launcher is owned by the selected desktop user/group at 0755; no home path is
hardcoded. The no-argument path preserves existing direct Labwc integration.
Ordering remains **health → GUI checker → zero-argument sudo root
installer → health → Chromium `/setup`**. The launcher now has bounded individual
health waits with a local error dialog, and its supervisor retries after process
exit. It does not detect a hung renderer or guarantee recovery of Chromium's
network error page. Optional `wlr-randr` mode selection does not install hardware
overlays or touch calibration. The exact Waveshare SKU/connection remains unknown;
verify 1280×800, all touch corners, blanking, QR readability and power manually.

The original three sources, exact byte hashes and transfer provenance are in
[`hub-updater`](../../hub-updater/README.md). Installed working copies contain
targeted safety corrections. This is a **proposed fresh-device sudo recipe**,
not a reconstruction of a policy whose deployed evidence is still pending:

```sudoers
YOUR_DESKTOP_USER ALL=(root) NOPASSWD: /usr/local/sbin/initiativetracker-install-update ""
```

The empty argument expression permits zero arguments only. The recipe validates
sudoers, adds no grant to the web service, and installs root-controlled executables
and root-owned regular 0644 `updater.conf`. Existing OS-wide sudo privileges are
not removed. Production JSON stays non-secret: the existing release contract
copies it into releases at 0644. Network passwords stay in separate 0600 files.
Privileged Python uses the fixed system interpreter with `-I` so caller CWD,
Python environment overrides and user site packages cannot supply root imports.
Root shell entry points use a fixed Bash interpreter in privileged mode and a
fixed PATH. The recovered originals remain unchanged; install only the reviewed
runtime copies through the recipe.

The first release is staged under root-owned 0755 `incoming`, with its manifest
renamed to `InitiativeTracker-<version>-linux-arm64.manifest.json` and a bare local
`MANIFEST_SOURCE` setting. The `.manifest.json` suffix matters: Stage's
`release-manifest.json` is a transfer filename, not a valid installed source name.
Later updates continue through [Build/Test/Stage, SCP and the GUI](hub-release.md).

## Satellite and pairing

Satellite installs Chromium and its OS prerequisites only. Labwc startup waits
for healthy hub + matching GUID and then opens `/display` with the same private
profile under `~/.local/share/initiativetracker-satellite/chromium` every time.
The launcher is locked to one instance and relaunches an exited Chromium process.
An already loaded app uses its existing SignalR reconnection behavior. Browser
render freezes/error pages may require local reload/restart.

Approve the pending display code from authenticated **DM → Connected Devices**
on the hub. Preserve profile, hub origin, hub DB and Data Protection keys across
reboots. Tokens are never preseeded or copied from production. Optional external
profile priority is 100; hub-private profile priority is 50. Autoconnect does not
make the satellite follow a hub that changes networks. **Automatic Wi-Fi following
and the proposed two-minute reachability policy remain future N8 work.** Join the
new hub network locally when needed. Satellite OS/Chromium updates remain normal
OS maintenance; no fleet management component exists in this repository.

## Verification and rollback checklist

Automated fixtures are not a newly booted Pi OS image. Record the exact Pi image,
model/storage/display/connection, source SHA, release hashes and sanitized outcomes.
All physical cases below remain required; never test them on the working devices
without separate authorization.

- [ ] Fresh hub: installer exits 0; app, Avahi, socket and independent timers are
  enabled/active; `/system/health` returns healthy/current migrations/storage ready.
- [ ] Reboot: same hub GUID, database state, trust and keys; GUI updater precedes
  browser; loopback `/setup` works and remote `/setup` stays blocked.
- [ ] External Wi-Fi present: saved eligible station profile wins; AP stays inactive.
- [ ] Offline boot: no premature AP autoconnect; fallback eventually starts after
  required observation/attempts. Test a visible but unusable saved network too.
- [ ] Supervised DM switches: confirm and expire confirmation; independent
  rollback works; busy locks hold without changing network or corrupting journals.
- [ ] Screen: actual 1280×800 output, touch/scroll/corners, QR scan, no blanking,
  cold-boot autologin, one kiosk supervisor/profile, browser exit recovery.
- [ ] Satellite: reaches correct hub, pairs through approval, retains trust after
  reboot, resynchronizes after temporary outage; manual network-change limitation
  is understood. Test an initial Chromium network error separately.
- [ ] Later approved release: Build/Test/Stage and SCP; root-owned incoming
  `.manifest.json`; GUI installs version; `current` changes to a new versioned
  directory, previous release retained, data preserved, backup directory 0700 and
  archive 0600, app service can traverse the new release.
- [ ] Disposable failure release: health failure and start failure restore previous
  release plus whole data snapshot; failed migrated data retained separately.
  Rollback stop failure must leave live data/link untouched and report failure.
- [ ] Repeat installer: no config/db/profile/credential change; interrupted install
  refuses repeat. Private backup includes desktop startup and hostname files.

The updater now restarts the old service on pre-activation backup/link errors,
and attempts rollback on post-activation errors as well as health failure. It
restores into a separate directory before moving data, never removes a colliding
failed-data directory, and refuses data moves if service stop fails. **Power loss,
SIGKILL, storage failure or failures midway through rollback are not a transactional
guarantee.** Preserve all trees/backups and inspect locally before recovery.

For a failed *fresh* installation, do not remove data or replay the bootstrap.
Keep its root-private receipt and backups. On a disposable commissioning card,
the clean recovery is to retain diagnostics privately and reimage. To undo only
a new desktop setting, with the app/kiosk stopped locally, restore the recorded
autostart backup and remove the installer-created LightDM drop-in; do not blindly
restore whole system directories. No automatic uninstall/data rollback is supplied.
Use [manual recovery](recovery.md) for a versioned app rollback and validate health,
release identity and restored data before resuming play.

## Runnable automated checks

```bash
python3 -m unittest discover -s scripts/installation/tests -v
python3 -m unittest discover -s pi-network/tests
docker build -f scripts/installation/tests/Dockerfile -t initiativetracker-installer-tests .
docker run --rm --network none initiativetracker-installer-tests
```

The local suite maps system commands to a recorded fake and writes only temporary
fixture roots. Container tests execute real Bash/tar/sudo with synthetic data,
fake systemd/health, instrumented temporary paths, and no real networking. They
are skipped outside the explicitly disposable test container. Container package
installation validates test dependencies, not the Pi's complete package set.
See the [validation record](installer-validation.md) for exact outcomes and limits.
