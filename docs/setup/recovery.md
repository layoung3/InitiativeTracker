# Troubleshooting and recovery

[Setup index](../../SetupReadme.md) · [Release procedure](hub-release.md) · [Networking](networking.md)

Start with non-secret metadata. These are operator instructions; no live hub was
accessed for this guide. During play avoid restarting networking, the desktop or
the application. Preserve the current data, previous release and logs before
attempting recovery. A failed network transition and a failed application release
are separate problems with separate recovery mechanisms.

## First diagnostic pass

**On the hub, Pi Bash:**

```bash
hostnamectl --static
systemctl is-active initiativetracker.service NetworkManager.service avahi-daemon.service
readlink -f /opt/initiativetracker/current
curl --fail --silent --show-error --max-time 5 http://127.0.0.1:5000/system/health
nmcli -f DEVICE,TYPE,STATE,CONNECTION device status
systemctl list-timers --all 'initiativetracker-*'
df -h /var/lib/initiativetracker /opt/initiativetracker
```

Expected: app/network/Avahi active; valid versioned target; healthy HTTP 200;
intended profile active; commissioned timers scheduled; free space. HTTP 503 means
the health check found a database/storage problem; a connection refusal suggests
the service is not listening. Check `/system/status` for pending migration count
and the last applied migration, without dumping database/config contents.

For journals, use `sudo journalctl -b -u <unit> -n 80 --no-pager`, replacing the
unit with the relevant service below. Read locally; remove credentials, network
names and private URLs before sharing. Do not attach full config files, browser
profiles or data backups to an issue.

## Symptoms and checks

| Symptom | Check and expected result | Recovery direction |
| --- | --- | --- |
| SSH says wrong user / permission denied | Use the Imager account: `ssh PI_USER@HUB_ADDRESS`; locally `whoami` / `getent passwd PI_USER` | Hub service account is nologin. Hostname sync changes the host, not the Linux username. Verify SSH key/user before changing authentication. |
| `.local` hostname not found | On hub `curl -fsS http://127.0.0.1:5000/system/hub-info`; `hostnamectl --static`; `systemctl is-active avahi-daemon`; on Linux client `avahi-resolve-host-name HUB_NAME.local` | Use returned `mdnsHostName`, same LAN and actual IP for diagnosis. Check multicast/client isolation. Do not assume systemd-resolved replaced Avahi; bootstrap supports Avahi. |
| SSH host key changed | Verify fingerprint on local console with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` | Only update the workstation's known-host entry after verifying the intended reimaged device. A name collision/wrong IP is possible. |
| Hub inaccessible on Wi-Fi | Compare local health above with `nmcli device status`; from client `curl --fail --max-time 5 http://HUB_ADDRESS:5000/system/health` | Put client and hub on the same network, include port 5000, check router isolation. On private AP use `10.84.0.1`. Preserve local/Ethernet recovery. |
| Hotspot never appears after offline boot | `sudo /usr/local/libexec/initiativetracker/initiativetracker-boot-fallback status`; `systemctl is-enabled initiativetracker-boot-fallback.timer`; inspect its journal | Verify gate root:root 0600, recovery profile exists/AP capable, wlan0 managed/disconnected, two scans, minimum uptime and no pending transitions. Errors hold fallback. It is not exactly 60 seconds. Do not delete state to force it. |
| External Wi-Fi connects instead of intended AP | `nmcli -f NAME,UUID,TYPE,DEVICE connection show --active`; inspect selected profiles' autoconnect and priorities, plus recovery AP autoconnect | External Wi-Fi is intentionally preferred on boot. AP must be autoconnect=no. To intentionally switch, use authenticated DM controls and confirmation. Priority alone does not override an active connection. |
| Newly saved external Wi-Fi not chosen on reboot | Inspect that UUID's `connection.autoconnect`, priority and mode | Web provisioning saves autoconnect=false. Explicitly configure eligibility using the commissioning guide; a supervised return alone does not set it. |
| Hotspot switch refuses an external profile with spaces in its name | Inspect `nmcli -g connection.id connection show uuid SAVED_UUID` | The previous profile ID must match `[A-Za-z0-9_.:-]{1,80}` in the current recovery controller. During commissioning choose a unique simple profile ID; preserve SSID/UUID. Do not rename profiles during pending transitions. |
| Network-switch bridge unavailable/refused | `systemctl is-active initiativetracker-network-bridge.socket`; `stat -c '%U:%G %a' /run/initiativetracker/network-bridge.sock`; expected root:initiativetracker 660 | Check app drop-in enables `Tracker__NetworkCommandsEnabled=true`, helper execute modes, expected paths and root broker journal. Verify gates/timers and target UUID. Do not add broad sudo or make secrets readable by the web account. |
| Unconfirmed network switch seems stuck | Run `initiativetracker-network-control status` and `initiativetracker-network-return status` under `sudo` at the documented libexec path; check recovery/return timers | Wait for deadline plus timer/association allowance. Expect `recovered` / `rolled_back` if successful. A gate disabled during a pending transition prevents repair; malformed journals fail closed. Preserve journal evidence and restore approved configuration with local access. |
| `controller_lock_busy` messages | Boot service log shows `HOLD`, `controller_lock_busy`, `EXECUTED_NETWORK_ACTION=NONE` | Normal deferral while another controller owns a lock. Repeated contention requires identifying the owning process; do not remove lock files or terminate the network controller during play. |
| Satellite cannot find hub | From satellite test `getent hosts HUB_NAME.local`, `/system/health` and `/system/hub-info` | Ensure it joined the hub's current network. mDNS needs local multicast. No automatic satellite Wi-Fi following exists; manually select correct saved network. A healthy wrong hub is not the intended hub: compare GUID. |
| Satellite asks to pair again | Verify same `--user-data-dir`, same URL origin, retained cookies, correct hub ID, trust in `/dm/devices` | Avoid incognito, IP/name switching or profile resets. Restored keys/database, session change, expiry or revocation can invalidate trust. Reapprove the matching code through DM; never share tokens. |
| Chromium will not launch | As desktop user: `command -v chromium`, `pgrep -af chromium`; inspect local launcher/log and profile ownership | Use the graphical session, not root/bare SSH. Avoid concurrent Chromium on one profile. Close a stale instance normally; do not delete a profile or disable sandbox. Verify Wayland/Labwc desktop and available storage/power. |
| Chromium closes or shows an error page | Inspect approved hub launcher; satellite template log under `~/.local/state/initiativetracker-satellite/` | App service restart does not restart the browser. The satellite template relaunches exited processes; renderer/error-page recovery remains unverified. Locally reload/relaunch with the same profile; complete the crash test before keyboardless sign-off. |
| Graphical updater does not appear | Confirm `graphical.target`, active LightDM, desktop-user Labwc autostart/launcher, and the two approved updater executables | Check bare local `MANIFEST_SOURCE`, valid/newer version, startup health and missing graphical dependencies/policy. Do not bypass it by extracting over `current`. Same-version update may correctly not prompt. Full installer recipe is missing; obtain approved components. |
| Manifest, filename, size or SHA mismatch | Rerun workstation `Test-InitiativeTrackerRelease.ps1`; compare SHA after SCP | Stop. Transfer the exact matching pair from the trusted artifact. Do not edit the manifest hash, rename/relabel the binary or suppress validation. A checksum proves copy consistency, not publisher authenticity. |
| Application service fails | `systemctl status initiativetracker.service --no-pager`; journal; `readlink -f .../current`; `sudo -u initiativetracker test -x /opt/initiativetracker/current/InitiativeTracker.Web` | Check ARM64 OS/package, native dependencies, persistent data/config permissions, free space and service overrides. Do not run the app as root to work around permissions. Use release rollback if needed. |
| Database migration error | Read app journal and `/system/status` if reachable; check storage and service-user write access | Startup automatically applies EF migrations. Stop app before preservation/restore. N7 adds no migration; do not delete SQLite, edit migration history or run speculative migrations on production. See data recovery below. |
| Insufficient power / USB current warning | `vcgencmd get_throttled`; `vcgencmd get_config usb_max_current_enable`; inspect local kernel power/USB warnings | Use the official model-appropriate supply and good connections; check screen/SSD power needs or use a powered hub. A 3 A supply limits Pi 5 USB current. Do not force current-limit settings to disguise an inadequate supply. |

Raspberry Pi documents Pi 5's 600 mA USB limit with a 3 A supply and 1.6 A with a
proper 5 A source. `get_throttled=0x0` means no flags at that moment/history;
nonzero flags need interpretation using the
[official power/diagnostics documentation](https://www.raspberrypi.com/documentation/computers/raspberry-pi.html).
Recorded historical undervoltage can remain until reboot; resolving the power
source comes first.

## Network transition recovery

The UI's default confirmation window is 180 seconds. Network recovery first tries
the previous profile, then the private AP; saved-network return restores the AP
after expiration. Restoration can fail and retry later. An accepted UI request or
an active profile does not prove end-to-end access. Confirm only after reaching
the hub on the target network, never just to silence a countdown.

If you cannot reach either network, use local console/Ethernet to inspect status,
gates and timers. Retain both journals, verify controller dependencies and the
exact saved profile UUIDs, and repair approved installed files if missing. Do not
reset NetworkManager, erase credentials or disable independent recovery as a
generic fix. Manually activating a known profile is a maintenance intervention
that can disconnect everyone; it is not a substitute for testing supervised
transitions. The dry-run supervisor and decision planners do not execute recovery.

## Application rollback

The graphical installer's previously audited health-failure path restores old
release **and data**. It may not catch all other command failures. First inspect
the active target and healthy version. If automatic rollback succeeded, avoid
restoring twice. Keep the failed release and private diagnostic backup.

### Binary-only rollback

Use this only when the previous application is compatible with the current data
schema/config. N7/0.1.5 introduces no migration, but that does not prove every
older/future release is compatible. Check release notes and migration metadata.
Record the currently active path and take the consistent backup described in
[the release guide](hub-release.md#5-record-the-current-release-and-take-a-consistent-private-backup).
In an operator-approved pause in play, replace the placeholder with the retained
previous versioned directory, not another machine's path:

```bash
previous_release='/opt/initiativetracker/releases/PREVIOUS_VERSION-linux-arm64'
case "$previous_release" in /opt/initiativetracker/releases/*-linux-arm64) ;; *) exit 1;; esac
test -d "$previous_release" && test -x "$previous_release/InitiativeTracker.Web" || exit 1
test ! -e /opt/initiativetracker/current.rollback && test ! -L /opt/initiativetracker/current.rollback || exit 1
sudo systemctl stop initiativetracker.service
systemctl is-active initiativetracker.service
```

Expected: `inactive` (normally exit 3). Confirm it is stopped, then:

```bash
sudo ln -s "$previous_release" /opt/initiativetracker/current.rollback
sudo mv -Tf /opt/initiativetracker/current.rollback /opt/initiativetracker/current
sudo systemctl start initiativetracker.service
readlink -f /opt/initiativetracker/current
curl --fail --silent --show-error http://127.0.0.1:5000/system/health
```

Expected: previous version path, healthy response showing that version, same hub
identity and retained current data. This switches the symlink only; it does not
extract into or overwrite a release directory. Retained releases have their own
production config copy. Check it remains compatible with the external production
source before a later update. If old binaries cannot read current data, stop them
and use the schema-compatible recovery plan below.

### Schema-dependent data restore

Restoring an older checkpoint **discards subsequent play and trust changes**.
Obtain the owner's agreement to that specific checkpoint/data loss. Preserve the
failed data before restoring. A backup contains secrets: use private local storage
and never attach it to a PR. Do not restore just a DB file while leaving unrelated
WAL/SHM or replacing Data Protection keys independently.

Choose the consistent stopped-app backup made with the release guide. Verify
its hash and archive readability, then stop the application. These are templates
for an approved recovery, not commands executed during documentation:

```bash
backup_dir='/var/backups/initiativetracker/manual-CHOSEN_UTC_TIMESTAMP'
sudo sha256sum -c "$backup_dir/backup.sha256"
sudo tar -tzf "$backup_dir/data-and-config.tar.gz" >/dev/null
sudo systemctl stop initiativetracker.service
systemctl is-active initiativetracker.service
```

Expected: checksum `OK`, archive test exits 0, app inactive. Preserve the entire
failed data directory inside a new root-only parent; refuse to overwrite it:

```bash
failed_dir="/var/backups/initiativetracker/failed-$(date -u +%Y%m%dT%H%M%SZ)"
sudo test ! -e "$failed_dir" || exit 1
sudo install -d -o root -g root -m 0700 "$failed_dir"
sudo mv /var/lib/initiativetracker "$failed_dir/data"
sudo tar --acls --xattrs -xzf "$backup_dir/data-and-config.tar.gz" -C / \
  var/lib/initiativetracker
sudo stat -c '%U:%G %a' /var/lib/initiativetracker
```

Expected: restored data `initiativetracker:initiativetracker 750`, including matching
DB/keys. Select the **matching previous release** with the symlink commands above
before starting the service. Retain the failed external config and restore only
reviewed matching config/overrides if required; do not blindly extract `/etc`
over a working OS. This manual checkpoint format is different from any external
updater's backup layout: inspect its approved restore recipe instead of assuming
these paths fit an updater-generated backup.

Start once, inspect health/migrations, identity, characters, encounter and paired
devices. Expected: healthy compatible version; zero pending migrations after
startup; known checkpoint state. Some clients may need re-pairing if their trust
was created after the checkpoint. Keep failed data for diagnosis. If integrity
investigation is needed, run SQLite `PRAGMA integrity_check;` on a **private
stopped copy**, not speculative repair commands against the live database.

## First-install failure

The bootstrap is not rollback-aware. If it created a partial release/service,
keep the failed state and logs; check dependencies, config and service permissions
before restarting. It refuses an existing release directory. Do not delete it and
rerun blindly, especially after any data was created. A new-image reinstall is an
option only for a genuinely unused device with no data to retain, not production
recovery. OS/network configuration restoration requires its own private backup;
an application package does not restore networking, launcher or graphical updater.
