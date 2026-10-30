# Installer validation record

Date: **2026-10-30**. Scope: `layoung3/InitiativeTracker` only. Branch:
`feat/automated-pi-install`, stacked on documentation PR #2 / exact baseline
`53e48642a95e528578274893e384df9526b13790`. No repository/workspace AGENTS or
`.agents/skills` instructions were present. No file contents in other mounted repositories were read or changed. No live hub/satellite access, deployment, merge, master
push, host OS installation or production data/secret transfer occurred.

## Recovered source evidence

All three files under `hub-updater/original` match the supplied byte lengths and
SHA-256 values in `hub-updater/provenance.json`. They were reconstructed from the
complete exact source JSON supplied through the authorized parent task after
the supported Library materialization helper failed through the environment's
network proxy. The archive's supplied hash is provenance from the independently
verified parent handoff; archive bytes were not available here. Original scripts
were never executed here. Runtime changes are separately reviewable against them.

## Automated validation

| Check | Result / scope |
| --- | --- |
| Local installer tests | 21 passed; 13 container-only tests intentionally skipped on host |
| Debian 13 disposable container | 34 passed, including the 13 container-only tests; no skips |
| Existing network controller suite | 195 passed |
| ShellCheck 0.10 | Passed for all three shell entry points and all derived runtime scripts |
| Python compilation and shell syntax | Passed for added Python modules and runtime shell scripts |
| `git diff --check` | Passed |
| Dry-run entry points | Hub and satellite preview ran without sudo or OS mutation; release validation exercised with synthetic archives |

Container base: official `python:3.13-slim-trixie`, digest
`sha256:2d44738ebf4ce5f63b9d9ea871e9c7cfdc0426f7b0c97cac915fd88ded1d86bb`.
Test image installs only test dependencies (system Python, `sudo` 1.9.16p2, ShellCheck) and
synthetic accounts. Build needed the environment's existing HTTP proxy routing
and public CA bundle as a transient BuildKit certificate secret (0444 for APT's
unprivileged reader). No certificate or proxy credentials are stored in Git or
the image. The default networkless test invocation is documented in the
[installation guide](automated-installation.md); normal CI does not need that
environment-specific routing. Package installation was confined to Docker.

Fixture checks cover fresh hub/satellite completion; private modes and random
AP credentials; complete/no-op repeat and interrupted-install refusal; preservation
of database/profile/config; release size/hash/runtime metadata and unsafe tar
rejection; symlink refusal; secure terminal-only password entry; secret exclusion
from argv/output; no active network operation; saved-profile/AP policy; independent
recovery-before-gates ordering; preserved desktop defaults and private backups;
LightDM main-file precedence; expected hub origin/identity and application version.

Container checks execute actual derived Bash scripts, tar, sudo and visudo with
synthetic data and instrumented temporary fixed paths. Systemctl, health, Zenity
and Chromium are recordings/stubs, not live services or windows. They verify:

- Successful versioned update retains previous release/data backup with 0700/0600
  backup privacy and service-user-readable release root.
- Initial stop, backup and pending-link failures preserve data/current and restart
  the previous service when appropriate.
- Failed new-version health/start restores prior symlink **and whole data**,
  including fixture identity and Data Protection material, and retains failed data.
- Rollback stop failure leaves the new live data/link untouched and reports failure.
- Staged traversal is rejected before service stop.
- Actual GUI checker prompt calls the fixed installer with **zero source arguments**,
  installs a versioned release in the success fixture, and returns failure after a
  failed update/attempted rollback.
- Original hub ordering remains health/checker/health/Chromium with its persistent
  profile, including after a checker failure.
- Real sudo permits the proposed desktop rule's exact zero-argument entry, rejects
  additional source arguments/shell execution, and grants the app account nothing.
- Real sudo from an operator-owned hostile CWD: a deliberately unisolated
  configuration-check control imports the synthetic `pathlib.py` as UID 0,
  proving exploit coverage; the hardened complete update imports none of the
  hostile `pathlib`, `json`, `tarfile` or site customization modules.
- A second real-sudo fixture deliberately preserves hostile `PYTHONPATH`,
  nonexistent `PYTHONHOME`, `PYTHONUSERBASE` and `BASH_ENV` through a **test-only**
  sudoers environment allowance, confirms they reached the root process, and
  verifies a successful update with no imported module/shell-startup marker.
  The production recipe does not add that allowance.
- A spaced active Imager profile ID stops hub preflight before mutation with
  explicit commissioning guidance; no rename, activation or connectivity change.

Interpreter audit: all privileged stdin/command Python calls in the derived
updater, checker and bootstrap/hostname helper use `/usr/bin/python3 -I`.
Fresh installer entry points reset PATH and invoke the same isolated interpreter.
Installed Python network helpers use `#!/usr/bin/python3 -I`; their intentional
sibling loads use explicit `__file__`-relative paths under the root-owned helper
directory, with no caller-CWD `sys.path` insertion. Kiosk invocation also uses
isolated mode. Root shell entry points use `/bin/bash -p` to ignore inherited
shell startup/functions, as well as fixed interpreter/PATH selection. Exact
updater originals and their provenance hashes are unchanged.

Application source and Build/Test/Stage scripts were not changed. The owner already has
the approved 0.1.5 artifact; no new application release/build was produced solely
for this installer. No .NET SDK is installed on the development host. The existing
Build/Test/Stage/GUI workflow remains the release procedure, and real app health
on ARM64 still needs the hardware exercise below.

## Pending acceptance and metadata

This **does not prove a fresh Raspberry Pi OS boot**. No ARM64 Pi, real systemd
boot, NetworkManager radio/AP/DHCP, mDNS across devices, physical desktop autologin,
actual Zenity/Chromium touchscreen, satellite pairing or real graphical application
update was exercised. These remain unchecked in the
[complete installation/rollback checklist](automated-installation.md). Simulation
does not establish equivalent physical coverage, and this draft is not a certified
image or permission to run experimental installers on working devices.

Pending owner evidence (being collected separately): effective deployed narrow
sudo grant/location; allowlisted network unit override; LightDM autologin/session
fields; exact Pi OS/package baseline and Python data-filter support. These are
not claims that those dependencies are missing. The recipe is explicit and newly
proposed; it does not guess the deployed recipe. It selects the installed Labwc
desktop session instead of hardcoding a session name. Exact Waveshare SKU and
video/touch connection remain unknown; no overlays or calibration are guessed.

No automatic satellite Wi-Fi following, two-minute hub-reachability policy,
fleet updater, renderer-hang detection, or crash-consistent power-loss rollback
is implemented. Existing policy uses the 60-second-minimum hub observation plus
bounded attempts, inactive AP autoconnect, independent controllers and lock-busy
HOLD behavior. All physical claims remain subject to acceptance on disposable
commissioning devices, followed by a separate deployment approval.
