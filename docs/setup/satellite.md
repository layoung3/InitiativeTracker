# Fresh Raspberry Pi satellite

[Setup index](../../SetupReadme.md) · [Acceptance](acceptance.md) · [Recovery](recovery.md)

A satellite remains **Chromium only**; N9 adds local waiting artwork, not an app
server. The hub serves `/display`, pairing and
SignalR updates. The merged [fresh satellite installer](automated-installation.md)
provisions its OS prerequisites and desktop-local supervisor/waiting artwork.
There is no satellite .NET service, management agent, automatic Wi-Fi follower or
self-updater in this release. Use the fresh installer only on separately approved
new/disposable installations; it refuses existing installations. Existing working
satellites must use the compatibility gate in the [branding guide](branding.md),
not a rerun of a fresh installer. The former manual shell wrapper is historical
and lacks N9 waiting artwork; copying a PNG does not upgrade it.

For the application display's compact layout, automated viewport evidence and
remaining physical checks, see [0.1.6 satellite readability](../N9-satellite-readability.md).

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

The current installer stores the selected hub origin and exact hub ID in the
selected account's private `~/.config/initiativetracker-satellite/kiosk.json`.
It checks both health and that ID before opening Chromium. This is browser-display
pairing, not N8 agent enrollment or Wi-Fi following. Never put tokens in the URL.

## 3. Provision the fresh desktop components

Use the pinned complete source and [automated installation procedure](automated-installation.md)
for a new satellite. It installs these into the selected actual passwd home:

- `~/.local/lib/initiativetracker-satellite/` — desktop UID/GID, 0700.
- `kiosk.py` — 0755; `waiting.html` and original `colophon.png` — 0644, in that directory.
- `~/.config/initiativetracker-satellite/kiosk.json` — 0600.
- A quoted `/usr/bin/python3 -I` supervisor/config entry in the preserved Labwc autostart.

See [exact artwork hashes, deployment paths and copy/rollback commands](branding.md).
Do not substitute the old `~/.local/bin/initiativetracker-satellite-kiosk` shell
wrapper or `kiosk.conf`; they do not implement the current loading experience.
No bounded existing-satellite component upgrader is supplied; an older/mismatched
supervisor, waiting page or autostart needs separate device-specific review.

## 4. Preserve profiles and waiting behavior

The paired Chromium profile stays at
`~/.local/share/initiativetracker-satellite/chromium`. The temporary waiting browser
uses `~/.local/state/initiativetracker-satellite/waiting-chromium` and the local HTML;
the supervisor closes it before launching `/display`. Missing/invalid TLS or hub
identity never permits certificate-warning bypass, cached success or HTTP fallback.
The supervisor has duplicate-instance locking and restarts an exited browser; it
is not a renderer watchdog. Preserve `kiosk.json`, origin/ID, Labwc entry and both
profiles during artwork-only maintenance. Neither app archives nor the three-script
hub upgrader install these satellite files. Local loading art is distinct from
optional Linux/Plymouth/firmware boot splash, which is not installed.

## 5. Pair and retain trust

Open `/display` using that profile; unauthenticated requests redirect to
`/display/pair`. Match the satellite's pairing code with the pending request in
the hub's authenticated **DM → Connected Devices** (`/dm/devices`), approve it,
and optionally give it a recognizable display name. Pairing requests expire
after ten minutes; restart the request if needed. Expected: browser automatically
opens the encounter display and DM lists it under Paired Displays.

For N9 private portraits/campaign banners, use the separately commissioned trusted
HTTPS origin and fresh **Secure authorization** for that display browser, approved
by a securely authorized DM. Display pairing alone does not grant private media
access. Keep the current origin/profile until a separately approved cutover;
never change `hub_url` or certificate trust as part of copying artwork. See the
[single 0.1.6 plan](../https/0.1.6-integration-release-plan.md).

The durable paired flow issues a persistent display cookie with a five-year
lifetime, linked to a registered device and the current game session. The server
validates trust and session on requests. Keep the same profile, origin (hostname,
scheme, port), hub database and Data Protection keys. Cookies are origin-bound;
switching from hostname to IP can require pairing again even for the same hub.
Revocation, session changes, expiry, cleared cookies or restored old data can also
require pairing again. Pairing is persistent, not irrevocable.

The local Setup display QR still uses `/display/connect?token=...` as the
invitation URL, but a valid token now opens `/display/pair` for explicit DM
approval instead of issuing an unregistered Display cookie. Once paired, the
display has a trusted device ID. It still needs separate trusted-HTTPS secure
approval before viewing private media. Do not save the Setup token in autostart,
bookmarks, screenshots or this documentation.

Only after separately approved fresh-device startup/reboot acceptance, test the next desktop session/boot. Expected: desktop autologin,
fullscreen display and the same trusted device, without typing a pairing code.
Verify locally with `pgrep -af initiativetracker-satellite/kiosk.py` and
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
