# Fresh Raspberry Pi hub

[Setup index](../../SetupReadme.md) · [Networking](networking.md) · [Acceptance](acceptance.md)

For current N9 commissioning, start with [automated installation](automated-installation.md).
It includes the recovered source snapshots, reviewed provisioning recipe and
targeted updater/launcher corrections. The manual steps below describe the
original deployment baseline; their updater caveats still apply to existing
devices running the originals. No existing device has been changed by this PR.

These are **commissioning templates**, checked against source but not executed on
a newly imaged Pi during this task. Run them only on the new intended hub. They
are not an in-place repair or upgrade procedure. The app bootstrap exists;
graphical-updater/launcher provisioning uses the recovered components listed
below and the [reviewable recipe](../../hub-updater/README.md). Do not declare
keyboardless readiness until fresh-device acceptance checks pass.

The owner has verified an existing hub on **Debian 13 Trixie ARM64/aarch64**, with
LightDM, graphical target and the 0.1.5 application active, healthy persistent data
and unchanged identity. This supports the deployed baseline; it is not evidence
that this fresh-image procedure or draft automation passed on new hardware.

For N9 artwork use the [single branding guide](branding.md): bundled release PNG,
separately provisioned desktop PNG/launcher/supervisor, actual owner/modes and
existing-device compatibility/backup/rollback. Its source pin is merged master
`d9470510eba7af584d5c8f8ba05250d275979a62`. The legacy manual baseline below does
not itself provision N9 loading support; do not combine its original launcher with
a PNG and claim N9 support. Merged source is 0.1.6; any app start invokes migration
checks, requiring separate application/database approval before installation.

## 1. Hardware and imaging

Recommended hub: Raspberry Pi 5, cooling/case, reliable microSD or supported SSD,
official 27 W USB-C power supply, and the owner's Waveshare 10.1-inch 1280×800
touchscreen. Record the exact Waveshare model/revision and HDMI/USB versus DSI
connection before wiring; size/resolution alone do not identify a model. Power
down before fitting ribbons. Some displays need their own power supply.

Use [Raspberry Pi Imager](https://www.raspberrypi.com/documentation/computers/getting-started.html)
and choose **Raspberry Pi OS with desktop, 64-bit** for the correct board. Lite
does not supply the proven desktop session. In Imager configure a unique temporary
hostname, your chosen Linux username, SSH public-key access, time zone, keyboard
and Wi-Fi country/initial trusted network. Record the OS image date and Debian
base version. Keep Ethernet/local access during commissioning.

First boot, **Windows PowerShell** (substitute your actual values):

```powershell
$piUser = 'PI_USER'
$hubAddress = 'INITIAL_HOSTNAME.local'
ssh "${piUser}@${hubAddress}"
```

Expected: a shell as your selected user. If `.local` fails, obtain the IP from
your router or local desktop; a public Wi-Fi/client-isolation network is unsuitable
for first setup. See [SSH recovery](recovery.md).

## 2. Update OS and verify dependencies

**Pi Bash:**

```bash
uname -m
dpkg --print-architecture
cat /etc/os-release
sudo apt update
sudo apt full-upgrade -y
sudo apt install -y curl ca-certificates python3 sqlite3 avahi-daemon avahi-utils libnss-mdns \
  network-manager dnsmasq-base nftables iw chromium
sudo systemctl enable --now avahi-daemon.service
sudo reboot
```

Expected before installation: `aarch64`, `arm64`, desktop OS based on supported
Debian (12 or 13 for the runtime instructions below). Package install exits 0;
after reconnecting, `systemctl is-active NetworkManager avahi-daemon` returns
`active` for both, `command -v chromium` returns a path, and `nmcli device status`
shows the intended Wi-Fi device. The tracked controllers assume **wlan0**. Stop
if the adapter has a different name; adapt and review the controller configuration
as a separate task rather than claiming it supports arbitrary interfaces.

The self-contained package needs native .NET dependencies even though it bundles
managed runtimes. The optional runtime installation below resolves dependencies
through APT on its supported Debian base. If omitting the system runtime, install
the native dependencies listed for your OS in Microsoft's
[Debian dependency guidance](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian#dependencies)
and verify the application executable reports no missing library errors. Do not
copy obsolete `libicu` version numbers from a different Debian release.

## 3. .NET 10 ARM64 runtime (optional for this release format)

The build script uses `--self-contained true`; the service launches the apphost
executable directly. It does not call `/usr/bin/dotnet`. A system ASP.NET Core
runtime is useful for framework-dependent tooling/releases but is not necessary
for the current archive. A system-runtime update does not patch the runtime bundled
inside a self-contained release; rebuild/release that application when needed.

For an ARM64 Debian 12/13 base, Microsoft's
[Debian installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/linux-debian)
documents .NET 10 ARM64 packages. This is a new-device template, not evidence that
Raspberry Pi OS hardware has been certified here:

```bash
debian_version=$(. /etc/os-release; printf '%s' "$VERSION_ID")
case "$debian_version" in 12|13) ;; *) echo 'Check official instructions for this OS'; exit 1;; esac
test "$(dpkg --print-architecture)" = arm64 || exit 1
curl --fail --location \
  "https://packages.microsoft.com/config/debian/$debian_version/packages-microsoft-prod.deb" \
  --output /tmp/it-packages-microsoft-prod.deb
sudo dpkg -i /tmp/it-packages-microsoft-prod.deb
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0
dotnet --list-runtimes
```

Expected: `Microsoft.AspNetCore.App 10.0.x` and `Microsoft.NETCore.App 10.0.x`.
Use `aspnetcore-runtime`, not only `dotnet-runtime`, for framework-dependent web
apps. If packages are unavailable, consult the official
[manual/scripted ARM64 installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual);
do not install x64 packages or a random third-party image.

## 4. Transfer and validate the first release

On Windows use the existing approved artifact and perform sections 1–3 of the
[release procedure](hub-release.md); skip the build if already completed. Copy the
whole `hub-bootstrap` folder from the **same reviewed source checkout**, as well
as the manifest and package. Also transfer the reviewed `pi-network` folder for
the next guide. These folders are not inside the application archive.

**Windows PowerShell, repository root**, with `$piTarget` from the release guide:

```powershell
scp -r .\hub-bootstrap .\pi-network "${piTarget}:"
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap/source transfer failed' }
```

Expected: folders at `~/hub-bootstrap` and `~/pi-network`, package and
`release-manifest.json` under `~/it-release-transfer`. Run the transfer hash check
in the release guide before using the bootstrap. Keep a workstation record of the
approved source SHA and transferred script hashes; hashes compare copies, not trust.

## 5. Install the application once

**Pi Bash, fresh hub only:**

```bash
test ! -e /opt/initiativetracker/current && test ! -L /opt/initiativetracker/current || { echo 'Existing install: use updater'; exit 1; }
chmod +x "$HOME/hub-bootstrap/install-hub-release.sh"
sudo "$HOME/hub-bootstrap/install-hub-release.sh" \
  "$HOME/it-release-transfer/release-manifest.json" \
  "$HOME/it-release-transfer/InitiativeTracker-0.1.6-linux-arm64.tar.gz"
```

Use the actual approved package filename if different. Expected:
`Release validation passed.`, `Health check passed:`,
`InitiativeTracker hub installation completed.` and hub info. **Source caveat:**
the bootstrap runs APT before release validation; it is not a read-only validator.
It creates the user/directories/service, changes `current`, and has no complete
transactional rollback on failure. It checks archive paths and hashes but is not
a signed-package verifier. Use only the earlier validated, trusted release. If it
fails, inspect the service and retained files; do not rerun over a working device.

What the source installs:

| Resource | Configuration / verification |
| --- | --- |
| System account | `initiativetracker`, nologin shell; `getent passwd initiativetracker` |
| Release | `/opt/initiativetracker/releases/0.1.6-linux-arm64`, root-owned; `readlink -f /opt/initiativetracker/current` |
| Persistent data | `/var/lib/initiativetracker`, service user/group, mode 0750; `sudo stat -c '%U:%G %a' /var/lib/initiativetracker` |
| Config | Bootstrap production template copied into release; external `Tracker:DataDirectory`, LAN HTTP port 5000, friendly hostname |
| App unit | `User/Group=initiativetracker`, working directory `current`, `Restart=always`, `RestartSec=3`, `ReadWritePaths=/var/lib/initiativetracker` |
| Identity / mDNS | Hostname sync service reads `/system/hub-info`, sets persisted `initiative-<id-prefix>` hostname and refreshes Avahi |

Application startup applies EF migrations, creates `initiative-tracker.db` and
`DataProtection-Keys` under the configured data directory. Hub ID/hostname, game
session and trust are stored in SQLite. **Do not regenerate these every boot or
put the database/keys in the release directory.** Do not clone a populated hub's
database to initialize another hub; a new hub needs its own identity.

```bash
systemctl is-enabled initiativetracker.service initiativetracker-hub-hostname.service
systemctl is-active initiativetracker.service
curl --fail --silent --show-error http://127.0.0.1:5000/system/health
curl --fail --silent --show-error http://127.0.0.1:5000/system/hub-info
hostnamectl --static
systemctl is-active avahi-daemon.service
sudo -u initiativetracker test -w /var/lib/initiativetracker
sudo -u initiativetracker test -d /var/lib/initiativetracker/DataProtection-Keys
```

Expected: enabled units, active app/Avahi, HTTP 200 healthy with database and
storage ready, stable hub GUID and `mdnsHostName`, matching static hostname, last
two tests exit 0. After hostname sync the Imager hostname can stop resolving;
use the new name. From a second machine test `http://<mdnsHostName>:5000/` and
SSH with the original Linux account. Hub identity does not rename that account.

## 6. Establish the production configuration source for the updater

Bootstrap puts configuration only in the release. The observed deployed updater
expects `/etc/initiativetracker/appsettings.Production.json` and copies it into
later releases; the app does not automatically load `/etc` itself. On the new hub
create that source from the reviewed **non-secret** bootstrap template:

```bash
sudo install -d -o root -g root -m 0755 /etc/initiativetracker
sudo test ! -e /etc/initiativetracker/appsettings.Production.json && \
  sudo install -o root -g initiativetracker -m 0640 \
    "$HOME/hub-bootstrap/appsettings.Production.json" \
    /etc/initiativetracker/appsettings.Production.json
```

Expected: new source config exists. If already present, review locally instead of
overwriting it. The current release initially uses its bootstrap copy. Any future
site-specific changes must be made consistently to the source and active copy
in a maintenance window (and verified after restart); an `/etc` edit alone does
not change a running process. Do not print config contents in a shared transcript.
Keep this config **non-secret**. The existing updater installs its release copy
root-owned **0644**, even when the `/etc` source is 0640; protecting the source
alone does not make future copies private. The bootstrap copy is also 0644.
Secret configuration needs a separately approved design, not added fields here.

## 7. Install network components

Continue with [the fresh-device network procedure](networking.md). Verify root
socket access, profiles, timers and gates before attempting DM switches. Leave
independent recovery enabled after commissioning. Test both external-Wi-Fi and
offline cold boots; the latter has not yet been physically verified in supplied
deployment evidence.

## 8. Desktop, touchscreen and graphical updater

The observed working hub uses **LightDM**, `graphical.target`, a **Labwc** desktop
session with autologin to the selected desktop user, and that user's
`~/.config/labwc/autostart`. It invokes:

```text
~/.local/lib/initiativetracker-hub/initiativetracker-hub-launch
```

The launcher waits indefinitely for app health, runs the graphical updater,
waits for app health again, then `exec`s Chromium. It has **no browser crash-restart
loop**. This is the N7-era installed mechanism;
do not replace it with old LXDE autostart, an invented Chromium system service,
or a launcher that races the updater. Audited original sources are preserved
under [`hub-updater/original`](../../hub-updater/original), separately from the
proposed derived runtime/provisioning. Neither source preservation nor fixture
tests certify a newly imaged device. The new [fresh-device recipe](automated-installation.md)
installs derived working copies with bounded health waits and optional process
supervision. Existing hubs have a [separate component-only procedure](../../hub-updater/EXISTING-HUB-UPGRADE.md)
that preserves their direct Labwc integration. No proposed hardening is installed
on the existing device by this PR. An unhealthy app can hold the **original**
launcher before either the update prompt or Chromium without a deadline.

The original launches `/setup` with profile
`<DESKTOP_HOME>/.config/initiativetracker-hub/chromium`, kiosk/start-maximized
flags, first-run/crash-dialog suppression, TranslateUI and pinch/overscroll
options, and `--password-store=basic`. Preserve the chosen profile; it contains
browser trust and must stay private. These are inspected original-source flags,
not evidence of a new display configuration or a browser recovery supervisor.

Required approved component inventory:

- `/usr/local/sbin/initiativetracker-install-update`
- `/usr/local/bin/initiativetracker-check-update`
- `/etc/initiativetracker/updater.conf` and its required graphical/privilege dependencies
- The desktop user's `initiativetracker-hub-launch` and Labwc autostart integration
- Any narrowly scoped privilege policy used by that updater (not broad passwordless sudo)

Audit hashes for comparison are in [the verification record](evidence-and-gaps.md).
The graphical checker needs Zenity in the desktop session and calls
`sudo -n /usr/local/sbin/initiativetracker-install-update` with **zero arguments**.
The new proposed recipe supplies a validated zero-argument-only sudo entry for
that exact path; the deployed device's privilege recipe remains separate pending
evidence. Original checker exit 0 can also mean an install failure was displayed;
the derived working copy returns nonzero. Always verify release and health.
Do not copy credentials from another machine.

New installations must resolve the **selected desktop account's actual home**
from the account database and generate that user's Labwc autostart entry with
the resolved launcher path, backgrounded with `&`. Do not assume `/home/<name>`
or use root's `$HOME`. The owner observed a marked user-autostart block with this
background invocation; the launcher was desktop-user owned, mode 0755, 974 bytes.
Read-only checks, after replacing `DESKTOP_USER`:

```bash
desktop_user='DESKTOP_USER'
desktop_home=$(getent passwd "$desktop_user" | cut -d: -f6)
test -n "$desktop_home" && test -d "$desktop_home" || exit 1
test "$desktop_user" != root || exit 1
launcher="$desktop_home/.local/lib/initiativetracker-hub/initiativetracker-hub-launch"
sudo -u "$desktop_user" test -x "$launcher"
sudo -u "$desktop_user" grep -F "$launcher" "$desktop_home/.config/labwc/autostart"
sudo stat -c '%U:%G %a %s %n' "$launcher"
pgrep -u "$desktop_user" -af chromium
```

Expected on the original configured hub: autostart line for that resolved path
ending with `&`; desktop account ownership, mode 755 and 974 bytes; Chromium using
the expected persistent profile. The original launcher `exec`s Chromium: an empty
`pgrep` for `initiativetracker-hub-launch` after startup is **not a failure**.
Check Chromium, local health and the actual screen. A proposed supervised runtime
can differ; verify its own reviewed recipe rather than applying original byte-size
or process-name expectations to it.

On a fresh desktop use Raspberry Pi Configuration / `raspi-config` to choose
**desktop autologin for your selected user**, with a Labwc session. Verify locally:

```bash
systemctl get-default
systemctl is-active lightdm.service
test -x "$HOME/.local/lib/initiativetracker-hub/initiativetracker-hub-launch"
grep -F 'initiativetracker-hub-launch' "$HOME/.config/labwc/autostart"
test -x /usr/local/bin/initiativetracker-check-update
sudo test -x /usr/local/sbin/initiativetracker-install-update
```

Expected: `graphical.target`, `active`, launcher entry present, executable checks
exit 0. Run home-directory checks as the **desktop user**, not root. A desktop
autostart entry must launch once per session, and updater completion must precede
Chromium; inspect the approved recipe for how it handles cancellation or errors.
Do not execute an unaudited update script just to test for a prompt.

In the desktop's **Screen Configuration**, choose landscape 1280×800 and the
orientation matching the mounted panel. Check every corner with touch, drag,
scroll, tap Setup controls, and scan every QR from a phone. If HDMI touch uses a
separate USB cable, confirm both video and USB are connected. If it is DSI, follow
the exact model's vendor wiring/overlay instructions. Waveshare has multiple
10.1-inch products, including [HDMI LCD (B)](https://www.waveshare.com/wiki/10.1inch_HDMI_LCD_(B)_(with_case))
and [DSI LCD (C)](https://www.waveshare.com/wiki/10.1inch_DSI_LCD_(C)); neither is
established as the deployed model by resolution alone. Do not copy arbitrary
`config.txt`, X11 rotation, or touchscreen calibration settings into Labwc.

Verify in the local graphical session with `wlr-randr` if installed: output must
show the intended active mode/transform. Use desktop settings to disable screen
blanking for the kiosk and validate after reboot. Confirm fullscreen has no browser
chrome, no hidden Setup controls and a usable local escape route. A webpage's
fullscreen button can require a user gesture; the approved kiosk launcher owns
boot fullscreen.

**Unattended browser recovery is missing in the audited launcher:** it ends by
`exec`ing Chromium, so it does not relaunch Chromium after exit. A reviewed restart
policy and keyboardless acceptance are still required; do not describe that as
already installed. Do not restart LightDM during an update. Application systemd
restart handles app crashes, not browser crashes.

## 9. Initial use and handover

Open `http://127.0.0.1:5000/setup` **on the hub**. Setup is loopback-only; an SSH
shell does not make a remote browser local. Use the displayed DM login/QR privately,
then configure play from the authenticated DM pages. The four N7 connection cards
and network panel target 1280×800; real touch/QR readability still needs a physical
check. Verify a remote request to `/setup` returns 404 rather than exposing setup
credentials. Do not disable this protection to simplify setup.

Finish [all acceptance checks](acceptance.md), including the graphical update on
a later approved release, persistence and restoration. Record the OS image,
non-secret inventory and outcomes locally. Outstanding updater provisioning or
failed crash/offline tests mean the unit is not yet ready for keyboardless use.
