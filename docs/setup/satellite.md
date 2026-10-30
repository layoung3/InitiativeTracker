# Fresh Raspberry Pi satellite

[Setup index](../../SetupReadme.md) · [Acceptance](acceptance.md) · [Recovery](recovery.md)

A 0.1.5 satellite can be **Chromium only**. The hub serves `/display`, pairing and
SignalR updates. There is no tracked satellite OS installer, satellite management
agent, .NET service, automatic Wi-Fi follower or satellite self-updater. The local
launcher below is an explicitly **unverified commissioning template** for a fresh
Labwc desktop; it is not a claim about an installed satellite. It does not replace
the hub's proven updater/launcher.

## 1. Image and prepare the satellite

Use a Pi 4/5 with reliable storage, appropriate official power supply, a display
and initial local recovery access. In Raspberry Pi Imager select 64-bit Raspberry
Pi OS **with desktop**, a unique satellite hostname, your Linux account, SSH key,
Wi-Fi country and initial trusted network. Use the same LAN as the hub.

**Pi Bash, on the satellite:**

```bash
uname -m
hostnamectl --static
sudo apt update
sudo apt full-upgrade -y
sudo apt install -y chromium curl python3 avahi-daemon avahi-utils libnss-mdns network-manager
sudo systemctl enable --now avahi-daemon.service
command -v chromium
systemctl is-active NetworkManager.service
```

Expected: `aarch64`, a unique satellite hostname, Chromium path, active networking.
Use the Raspberry Pi desktop configuration / `sudo raspi-config` to select
desktop autologin for this Linux user in a Labwc session, set correct display
resolution/orientation, and disable screen blanking. See
[official OS configuration](https://www.raspberrypi.com/documentation/computers/configuration.html).
Verify `systemctl get-default` reports `graphical.target` and after login the local
graphical terminal reports `echo "$XDG_SESSION_TYPE"` as `wayland`. Do not run
Chromium as root, disable its sandbox, or set autologin on the hub service account.

## 2. Choose a stable origin and verify hub identity

Read `/system/hub-info` on the actual hub and privately record its `hubId` and
`mdnsHostName`. Choose one stable URL for this satellite, normally
`http://<mdnsHostName>:5000`. Test from the satellite:

```bash
hub_base_url='http://HUB_MDNS_NAME.local:5000'
expected_hub_id='HUB_GUID'
curl --fail --silent --show-error "$hub_base_url/system/health"
curl --fail --silent --show-error "$hub_base_url/system/hub-info" | \
  python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["hubId"] == sys.argv[1]; print("HUB ID MATCH")' "$expected_hub_id"
```

Expected: healthy response and `HUB ID MATCH`. If mDNS is unavailable use the
current hub IP for diagnosis, but fix discovery before relying on a hostname at
boot. `libnss-mdns` supplies Linux `.local` name resolution through NSS; verify
`getent hosts HUB_MDNS_NAME.local` as well as Avahi queries. See
[Debian package documentation](https://packages.debian.org/trixie/libnss-mdns). The private-hotspot hub address is `10.84.0.1`; it is useful while attached
to that hotspot, not a stable external-LAN address.

There is no application satellite setting that consumes a hub ID or performs
discovery/selection. The URL determines the origin; the ID above is an operator
verification record. The template below stores that expected ID and waits for a
match before starting Chromium. It provides neither network following nor secure
remote identity attestation. Do not put pairing tokens in the URL/configuration.

## 3. Create a dedicated persistent browser profile

Run as the selected satellite desktop user. Keep a separate profile from ordinary
desktop browsing, use it on every launch, and never use incognito/guest mode.

```bash
install -d -m 0700 "$HOME/.config/initiativetracker-satellite" \
  "$HOME/.local/share/initiativetracker-satellite/chromium" \
  "$HOME/.local/bin" "$HOME/.local/state/initiativetracker-satellite"
cat > "$HOME/.config/initiativetracker-satellite/kiosk.conf" <<'CONF'
HUB_BASE_URL='http://HUB_MDNS_NAME.local:5000'
EXPECTED_HUB_ID='HUB_GUID'
CONF
chmod 0600 "$HOME/.config/initiativetracker-satellite/kiosk.conf"
```

Edit `kiosk.conf` to the verified URL/GUID above, without a trailing slash in the
base URL. This local config is shell syntax, owned by the desktop user; never
source a downloaded or untrusted config file. Keep it out of Git.

## 4. Install the fresh-satellite launcher template

This template waits for hub health/ID, launches kiosk Chromium with the dedicated
profile and restarts it if its process exits. It must be tested with the chosen
OS/Chromium. It is not a browser-renderer watchdog or automatic update service.

```bash
cat > "$HOME/.local/bin/initiativetracker-satellite-kiosk" <<'SH'
#!/usr/bin/env bash
set -uo pipefail
umask 077
source "$HOME/.config/initiativetracker-satellite/kiosk.conf"
profile="$HOME/.local/share/initiativetracker-satellite/chromium"
while true; do
  until curl --fail --silent --connect-timeout 2 --max-time 5 \
      "$HUB_BASE_URL/system/health" >/dev/null && \
    curl --fail --silent --connect-timeout 2 --max-time 5 \
      "$HUB_BASE_URL/system/hub-info" | \
      python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin)["hubId"] == sys.argv[1] else 1)' "$EXPECTED_HUB_ID"; do
    sleep 5
  done
  chromium --kiosk --no-first-run --no-default-browser-check \
    --user-data-dir="$profile" "$HUB_BASE_URL/display"
  sleep 5
done
SH
chmod 0755 "$HOME/.local/bin/initiativetracker-satellite-kiosk"
bash -n "$HOME/.local/bin/initiativetracker-satellite-kiosk"
```

Expected: syntax check exits 0. From a terminal **in the graphical desktop**,
launch it once and complete any first-run desktop/keyring prompts there. Do not
launch it from a bare SSH session and expect the graphical session variables.

Preserve the desktop's existing autostart entries. For a new Labwc user config,
if `~/.config/labwc/autostart` is absent, first copy the system file if present to
retain desktop defaults:

```bash
install -d -m 0700 "$HOME/.config/labwc"
if [ ! -f "$HOME/.config/labwc/autostart" ]; then
  if [ -f /etc/xdg/labwc/autostart ]; then
    cp /etc/xdg/labwc/autostart "$HOME/.config/labwc/autostart"
  else
    touch "$HOME/.config/labwc/autostart"
  fi
fi
if ! grep -Fq '/.local/bin/initiativetracker-satellite-kiosk' "$HOME/.config/labwc/autostart"; then
  cat >> "$HOME/.config/labwc/autostart" <<'SH'

"$HOME/.local/bin/initiativetracker-satellite-kiosk" >> "$HOME/.local/state/initiativetracker-satellite/kiosk.log" 2>&1 &
SH
fi
bash -n "$HOME/.config/labwc/autostart"
```

Expected: one launcher entry; no syntax errors. Close the initial manual instance
before testing a new desktop session so two launchers do not contend for one
profile. Logs are local diagnostics; inspect/redact before sharing. This template
does not implement log rotation, duplicate-instance locking or renderer recovery;
those are installer/launcher hardening gaps to resolve for unattended distribution.

## 5. Pair and retain trust

Open `/display` using that profile; unauthenticated requests redirect to
`/display/pair`. Match the satellite's pairing code with the pending request in
the hub's authenticated **DM → Connected Devices** (`/dm/devices`), approve it,
and optionally give it a recognizable display name. Pairing requests expire
after ten minutes; restart the request if needed. Expected: browser automatically
opens the encounter display and DM lists it under Paired Displays.

The durable paired flow issues a persistent display cookie with a five-year
lifetime, linked to a registered device and the current game session. The server
validates trust and session on requests. Keep the same profile, origin (hostname,
scheme, port), hub database and Data Protection keys. Cookies are origin-bound;
switching from hostname to IP can require pairing again even for the same hub.
Revocation, session changes, expiry, cleared cookies or restored old data can also
require pairing again. Pairing is persistent, not irrevocable.

The local Setup display QR currently uses `/display/connect?token=...`, a
session-token shortcut; it does not create a registered device ID. Use the
`/display/pair` approval flow for managed satellites. Do not save the Setup token
in autostart, bookmarks, screenshots or this documentation.

Reboot the satellite after initial approval. Expected: desktop autologin,
fullscreen display and the same trusted device, without typing a pairing code.
Verify locally with `pgrep -af initiativetracker-satellite-kiosk` and
`pgrep -af chromium`; one launcher and the dedicated `--user-data-dir` should be
present. Processes alone do not prove a working page: check the screen.

## 6. Wi-Fi and reconnection

Save the external LAN and hub's private hotspot through the satellite's desktop
network UI or `sudo nmtui`. Obtain the hotspot credentials locally from the hub's
physical Setup screen; do not copy its root-only secret file onto the satellite.
Use interactive entry or a supported QR flow. Then inspect non-secret profile
metadata with `nmcli -f NAME,UUID,TYPE,DEVICE connection show` and set selected
profile autoconnect/priorities by UUID as needed:

```bash
satellite_profile_uuid='SAVED_SATELLITE_PROFILE_UUID'
sudo nmcli connection modify uuid "$satellite_profile_uuid" \
  connection.autoconnect yes connection.autoconnect-priority 100
nmcli -f connection.uuid,connection.autoconnect,connection.autoconnect-priority \
  connection show uuid "$satellite_profile_uuid"
```

Expected: intended profile has autoconnect enabled and chosen priority. Set
different priorities for deliberate preferences, and test each boot scenario.
**This does not make the satellite follow the hub.** NetworkManager will not
abandon an already active external connection merely because that hub moved to
its private hotspot. Join the hub's network manually when needed; maintain local
or SSH access. Do not install the hub AP/fallback controllers on a satellite.

When a loaded display temporarily loses the network, `initiative-display.js`
uses SignalR reconnection, then a five-second retry after close/start failure;
on reconnection it resynchronizes theme and encounter. This does not recover a
Chromium error page that never loaded the app or a hung renderer. The template
waits for initial reachability and relaunches exited Chromium; navigation errors
or renderer hangs may need local reload/restart. Test these cases explicitly;
there is no verified keyboardless recovery guarantee for them yet.

## 7. Synchronization, updates and handover

In a test encounter, have the DM add combatants, roll/set initiative, change HP,
apply a condition/exhaustion and advance turns. Expected: satellite shows current
public data promptly, keeps its header visible while the list scrolls, and hides
DM-private combatants/HP according to encounter settings. Briefly interrupt only
the satellite's network, restore the same network, and verify it resynchronizes
without duplicate devices. Reboot and repeat.

The browser obtains web assets from the hub; after hub updates reload the page
and verify version/behavior on the hub. Satellite OS/Chromium updates use normal
OS administration (`sudo apt update`, `sudo apt full-upgrade` in a maintenance
window, then reboot/test). There is no remote fleet update or automatic satellite
management mechanism in this repository. Protect the browser profile like a
credential; do not commit or share it. Complete [satellite acceptance](acceptance.md)
before removing the commissioning keyboard.
