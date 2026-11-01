# Hub network installation and behavior

[Setup index](../../SetupReadme.md) · [Fresh hub](fresh-hub.md) · [Recovery](recovery.md)

The following installation commands are **fresh-device templates**, source-checked
at the baseline commit. They have not been run against a live Pi in this task.
Do not apply them over an existing commissioned hub's files, gates or units.
Application releases do not install or update these OS components.

Owner-performed read-only verification on 2026-10-30 found boot fallback enabled,
the bridge socket active, recovery/return timers active, saved external profiles
present and `InitiativeTracker-Recovery` autoconnect disabled on the existing hub.
Personal network names are intentionally omitted. These observations confirm
installed configuration/status, not physical offline boot or transition-expiry
acceptance. The fresh-device checks below remain required.

## Behavior you can rely on in source

NetworkManager first gets an opportunity to autoconnect saved external networks.
The boot controller operates only while `wlan0` is disconnected, and only with
its explicit gate. It requires two successful separated scans (at least 10 seconds
apart), a **minimum 60-second uptime window**, and a final discovery refresh before
fallback. Eligible saved external profiles must be visible, station/infrastructure
mode, autoconnect enabled, and bound to `wlan0` or no specific interface. They are
ordered by autoconnect priority, addressed by UUID, and receive one bounded
15-second activation attempt each per boot. Timer delays, radio readiness and
multiple candidates can extend the time; **60 seconds is not a deadline**.

An established connection settles boot policy for the boot. Merely naming a
profile during association/authentication does not. Active or activating links,
pending journals, failed scans and unavailable radios defer fallback; the
controller does not forcibly cancel NetworkManager association. After fallback
it does not automatically leave a functioning hotspot because an external SSID
appears. This protects an ongoing game from unsolicited network switching.

This is not an Internet-reachability test. A connected external Wi-Fi network can
have no Internet and still be accepted. The requested future behavior of roughly
two minutes of **actual satellite-to-hub reachability** before satellite fallback
is not implemented. N8 automatic satellite Wi-Fi following is also absent.

## 1. Install reviewed scripts and real units

Prerequisites: the app account exists from the hub bootstrap, NetworkManager is
active, `/usr/bin/nmcli` works, the Wi-Fi interface is `wlan0`, and you have a local
recovery path. The fresh-hub package list includes `dnsmasq-base` for shared-mode
DHCP/DNS and `nftables` for an available NetworkManager shared-mode backend. Check
`command -v dnsmasq` and `command -v nft`; both must return paths. See
[Debian's NetworkManager package notes](https://packages.debian.org/trixie/network-manager)
and [NetworkManager's firewall-backend reference](https://networkmanager.dev/docs/api/latest/NetworkManager.conf.html).
Do not enable a competing standalone DHCP server or flush firewall rules to fix
shared-mode activation. Compare transferred source hashes with the reviewed checkout.

**Pi Bash, new hub only:**

```bash
getent passwd initiativetracker
systemctl is-active NetworkManager.service
nmcli -f DEVICE,TYPE,STATE,CONNECTION device status
sudo install -d -o root -g root -m 0755 /usr/local/libexec/initiativetracker
sudo install -d -o root -g root -m 0700 /var/lib/initiativetracker-network
sudo install -d -o root -g root -m 0700 /etc/initiativetracker/network
for source_file in "$HOME"/pi-network/initiativetracker-*; do
  test -f "$source_file" || exit 1
  sudo install -o root -g root -m 0755 "$source_file" /usr/local/libexec/initiativetracker/
done
for component in network-bridge.socket network-bridge.service \
  network-recovery.service network-recovery.timer \
  network-return.service network-return.timer \
  network-observer.service network-observer.timer \
  boot-fallback.service boot-fallback.timer prepare-startup.service; do
  sudo install -o root -g root -m 0644 \
    "$HOME/pi-network/systemd/initiativetracker-$component" /etc/systemd/system/
done
sudo systemctl daemon-reload
```

Expected: commands exit 0; scripts `root:root 755`, units `root:root 644`, state
and gate directories `root:root 700`. Installing all script files preserves
same-directory Python imports (notably boot fallback's discovery dependency).
Source files are often nonexecutable in Git, so the installed mode matters.
Do **not** enable `*.disabled-example`: the old supervisor/watchdog examples are
diagnostic prototypes, not substitutes for independent recovery services.

## 2. Stage a private hotspot without activating it

Use a unique SSID derived from this hub's identity, not the staging script's
historical hardcoded default. This creates a generated secret without printing it.

```bash
hub_suffix=$(curl --fail --silent --show-error http://127.0.0.1:5000/system/hub-info | \
  python3 -c 'import json,sys,uuid; print(uuid.UUID(json.load(sys.stdin)["hubId"]).hex[:8])')
test "${#hub_suffix}" -eq 8 || exit 1
sudo /usr/local/libexec/initiativetracker/initiativetracker-stage-recovery-hotspot \
  stage --device wlan0 --ssid "InitiativeTracker-$hub_suffix"
sudo /usr/local/libexec/initiativetracker/initiativetracker-stage-recovery-hotspot check --device wlan0
nmcli -f connection.id,connection.uuid,connection.interface-name,connection.autoconnect,802-11-wireless.mode,ipv4.method,ipv4.addresses,ipv6.method \
  connection show InitiativeTracker-Recovery
sudo stat -c '%U:%G %a %n' \
  /etc/NetworkManager/system-connections/InitiativeTracker-Recovery.nmconnection \
  /etc/initiativetracker/network/recovery-wifi.password
```

Expected: `N62_STAGE=SUCCESS`, `AUTOCONNECT=false`, `ACTIVATED=false`; persistent
profile named `InitiativeTracker-Recovery`, AP mode on wlan0, 2.4 GHz, IPv4 shared
`10.84.0.1/24`, IPv6 disabled, files `root:root 600`. Profile UUID is generated.
The helper refuses to overwrite any existing recovery profile/secret. A refusal
on an already configured device means inspect, not delete and recreate credentials.

The profile must remain **autoconnect=no**. Lower priority alone is insufficient:
an AP can otherwise activate before external scans finish. The preparation unit
repairs only this exact staged keyfile's autoconnect field before NetworkManager
on boot. It neither repairs arbitrary profiles nor replaces credentials. If your
profile is elsewhere or has another name, diagnose the mismatch; do not modify
every AP. NetworkManager shared mode supplies the private network; a separately
managed hostapd/dnsmasq setup is not part of this architecture.

## 3. Configure saved external Wi-Fi

The Imager/desktop connection can already provide a saved profile. For another
WPA network, use the local desktop NetworkManager UI, or `sudo nmtui` to enter
credentials interactively. Do not put passwords in command arguments, shell
history, pasted JSON, Git or screenshots. Confirm profile UUID with:

```bash
nmcli -f NAME,UUID,TYPE,DEVICE connection show
```

After identifying the intended station profile, **new-device configuration**:

```bash
saved_uuid='SAVED_UUID'
sudo nmcli connection modify uuid "$saved_uuid" connection.autoconnect yes \
  connection.autoconnect-priority 100 connection.interface-name wlan0 \
  connection.id IT-External-1
nmcli -f connection.id,connection.uuid,connection.autoconnect,connection.autoconnect-priority,connection.interface-name,802-11-wireless.mode \
  connection show uuid "$saved_uuid"
nmcli -g connection.autoconnect connection show InitiativeTracker-Recovery
```

Expected: external autoconnect `yes`, priority `100`, station mode, wlan0; AP `no`.
Use a unique simple **profile ID** such as `IT-External-1`; renaming this ID does
not change the Wi-Fi SSID or UUID. The supervised external → hotspot controller
currently permits previous profile IDs only matching `[A-Za-z0-9_.:-]{1,80}`.
An SSID may contain spaces/special characters, but using it verbatim as the profile
ID can make that supervised switch refuse. Commission/verify a simple unique ID
on the fresh hub; do not rename an active production profile mid-transition.
Use distinct priorities to express your preferences across multiple external
profiles. A higher priority does not force a switch away from an already active
connection. If an unintended saved external profile keeps winning, review its UUID
and eligibility/priority locally. Do not delete all saved profiles as a fix.

**Important:** the website's Wi-Fi provisioning helper intentionally saves new
profiles with **autoconnect=false** and does not connect them. Saving a network or
using supervised return does not automatically make it eligible on the next boot.
Set/verify autoconnect and priority through the admin procedure above if that
network should participate in boot discovery. Initial UI support is WPA password
based; do not assume enterprise Wi-Fi or captive-portal automation.

## 4. Start independent recovery and bridge before enabling gates

```bash
sudo systemctl enable --now initiativetracker-network-recovery.timer \
  initiativetracker-network-return.timer initiativetracker-network-observer.timer \
  initiativetracker-network-bridge.socket
sudo systemctl enable initiativetracker-prepare-startup.service
sudo systemctl enable --now initiativetracker-boot-fallback.timer
systemctl list-timers --all 'initiativetracker-*'
systemctl is-active initiativetracker-network-bridge.socket
stat -c '%U:%G %a %n' /run/initiativetracker/network-bridge.sock
sudo /usr/local/libexec/initiativetracker/initiativetracker-network-control status
sudo /usr/local/libexec/initiativetracker/initiativetracker-network-return status
sudo /usr/local/libexec/initiativetracker/initiativetracker-boot-fallback status
```

Expected: socket active, `root:initiativetracker 660`; timers scheduled; controller
gates initially false and fallback `DRY_RUN`; status commands report no executed
network action. Status may create locks/state directories; it does not switch Wi-Fi.
The boot service being inactive between oneshot runs is normal. The enabled
preparation unit applies next boot; do not restart NetworkManager remotely just
to exercise it.

| Component | Timer | Role / journal |
| --- | --- | --- |
| Boot fallback | On boot 10 s, 10 s after inactivity, accuracy 1 s | Disconnected boot scans / `boot-fallback.json` |
| Network recovery | On boot 30 s, every 20 s of unit activity, accuracy 5 s | Supervised transition / `transition.json` |
| Saved Wi-Fi return | On boot 20 s, 15 s after inactivity, accuracy 3 s | Hotspot → saved Wi-Fi / `return-transition.json` |
| Observer | On boot 45 s, 30 s after inactivity, accuracy 5 s | Timing observations; no network writes |

The recovery timer repairs **journaled transitions**, not every arbitrary later
Wi-Fi outage. Boot policy settling does not imply a universal connectivity watchdog.
Observer/planner scripts are not autonomous switch controllers.

## 5. Opt in on the new hub, then test with a person present

Only after local recovery, profile identity, gates, timer presence and private
credential storage have been checked, create the three new-device gates:

```bash
sudo bash <<'SH'
set -euo pipefail
umask 077
gate_dir=/etc/initiativetracker/network
for gate in live-switch.enabled return-switch.enabled boot-fallback.enabled; do
  test ! -e "$gate_dir/$gate" || { echo 'Existing gate: review before changing'; exit 1; }
done
printf '%s\n' 'ENABLE_N64C_LIVE=yes' > "$gate_dir/live-switch.enabled"
printf '%s\n' 'ENABLE_RETURN_SWITCH=yes' > "$gate_dir/return-switch.enabled"
printf '%s\n' 'ENABLE_BOOT_FALLBACK=yes' > "$gate_dir/boot-fallback.enabled"
chown root:root "$gate_dir/"*.enabled
chmod 0600 "$gate_dir/"*.enabled
SH
sudo stat -c '%U:%G %a %n' \
  /etc/initiativetracker/network/live-switch.enabled \
  /etc/initiativetracker/network/return-switch.enabled \
  /etc/initiativetracker/network/boot-fallback.enabled
```

Expected: three regular root-owned 0600 files with exactly those values. Gates
are separate permissions to act; disabling one can prevent pending rollback.
Do not remove them during a transition as a generic recovery technique.

Enable the application's allowlisted socket client on a **new** hub with a
drop-in. Preserve an existing `network-commands.conf` on deployed devices.

```bash
sudo install -d -o root -g root -m 0755 /etc/systemd/system/initiativetracker.service.d
sudo test ! -e /etc/systemd/system/initiativetracker.service.d/network-commands.conf || exit 1
sudo tee /etc/systemd/system/initiativetracker.service.d/network-commands.conf >/dev/null <<'UNIT'
[Service]
Environment=Tracker__NetworkCommandsEnabled=true
UNIT
sudo chmod 0644 /etc/systemd/system/initiativetracker.service.d/network-commands.conf
sudo systemctl daemon-reload
sudo systemctl restart initiativetracker.service
curl --retry 30 --retry-connrefused --retry-delay 2 --max-time 5 \
  --fail --silent --show-error http://127.0.0.1:5000/system/health
sudo -u initiativetracker test -w /run/initiativetracker/network-bridge.sock
```

Expected: healthy and socket permission test exits 0. The root broker verifies
the peer UID is `initiativetracker` and accepts bounded, allowlisted requests on
the Unix socket. It does not grant arbitrary shell or broad sudo access to the web
process. Do not relax service hardening or give the desktop/web account access to
root-only secret files to fix a bridge error.

## 6. Supervised changes and acceptance

From authenticated `/dm/network`, request a switch to the private hotspot; join
the hotspot on the DM device, reconnect to the hub at its advertised name or
`http://10.84.0.1:5000`, and confirm **within three minutes**. For hotspot → saved
Wi-Fi, select the saved UUID in the DM UI, join that same external network, and
confirm within three minutes. Detached systemd workers continue if the requesting
browser loses its connection. A successful request response is not proof the
target became usable; verify connection, health and confirmation state.

Without confirmation the independent recovery timer attempts the previous
profile, then recovery AP where applicable; return rollback restores the private
AP and retries if restoration fails. Gate state, valid journals and functioning
NetworkManager remain prerequisites. Handoff can confirm a pending hotspot
transition before beginning saved-network return. Do not overlap transitions.

The hub boot controller shares `boot-fallback.lock`, `control.lock` and
`return-transition.lock`, and checks both transition journals. Busy locks now
produce `RECOMMENDATION=HOLD`, `REASON=controller_lock_busy`,
`EXECUTED_NETWORK_ACTION=NONE` with successful exit; they must not modify policy
state or interrupt another controller. Pending/corrupt journals hold fallback.
Never delete journals/locks to force a switch.

Run [acceptance cases](acceptance.md) for external available, absent, visible but
unusable, late radio, manual AP retention and expired confirmation. Read-only
diagnostics on the commissioned device:

```bash
nmcli -f DEVICE,TYPE,STATE,CONNECTION device status
nmcli -f NAME,UUID,TYPE,DEVICE connection show --active
sudo /usr/local/libexec/initiativetracker/initiativetracker-boot-fallback status
sudo /usr/local/libexec/initiativetracker/initiativetracker-network-control status
sudo /usr/local/libexec/initiativetracker/initiativetracker-network-return status
sudo journalctl -b -u initiativetracker-boot-fallback.service \
  -u initiativetracker-network-recovery.service -u initiativetracker-network-return.service --no-pager
```

Expected: chosen active profile, controller terminal states after confirmation or
recovery, no repeated errors. On offline boot, eventual `ACTIVATE_PRIVATE_HOTSPOT`
is expected only after required discovery; record actual timestamps. Previously
observed external-network reboot success does not prove physical offline fallback.
Keep logs private if they reveal local network names; share sanitized outcomes.

## Trusted HTTPS hostname resolution

The owner-confirmed `hub-84eaee7c.colophon.stream` target has a separate
[private hostname resolution plan](../https/private-name-resolution.md) and
[staged HTTPS deployment guide](../https/private443-deployment.md). Treat the
home XB8 as non-cooperative; no router change or automatic phone DNS support is
assumed. Exact hotspot answers must exclude the hub's physical-screen loopback
mapping. These approval-gated additions do not change existing network recovery.
