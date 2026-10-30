# InitiativeTracker setup and operations

Start here to assemble a Raspberry Pi hub and satellite, or to update an existing
hub. The deployed baseline reported by the owner is **0.1.5**. This guide was
checked against `master` commit `353a1c78b0eb9150d6587a6c442c7dca76aaf5e4`.

**A release package is not a complete device image.** The application bootstrap
and network components are tracked here; the proven graphical updater and hub
desktop launcher are installed OS components whose distributable installers are
missing. A fresh hub cannot yet be certified as reproducible from this repository
alone. Follow the documented steps and checkpoints, obtain the missing approved
components, and finish the hardware acceptance checklist before calling it ready.

## Choose your route

| Task | Guide |
| --- | --- |
| Update an already configured hub | [Canonical hub release procedure](docs/setup/hub-release.md) |
| Image a new Pi and install the hub | [Fresh hub installation](docs/setup/fresh-hub.md) |
| Install hub networking and understand fallback | [Hub networking](docs/setup/networking.md) |
| Set up a browser-only satellite | [Fresh satellite installation](docs/setup/satellite.md) |
| Diagnose or restore a device | [Troubleshooting and recovery](docs/setup/recovery.md) |
| Prove that installation works | [Acceptance checklist](docs/setup/acceptance.md) |
| Check evidence and outstanding installer work | [Verification record and gaps](docs/setup/evidence-and-gaps.md) |

If your release is already built, start at **Validate and stage** in the release
guide. Do not rebuild merely to follow this documentation. Never use the
first-install bootstrap as an updater or copy files over the live application.

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

- **Source-verified:** checked against the commit above; does not prove installed
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
