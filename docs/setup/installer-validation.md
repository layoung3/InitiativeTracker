# Installer validation record

Date: **2026-10-30**. Scope: `layoung3/InitiativeTracker` only. Branch:
`feat/automated-pi-install`, originally stacked on documentation PR #2 at
`53e48642a95e528578274893e384df9526b13790`, reconciled with its final base
`a40f534fc6d30d1156206435a52f11268636da1e`. No repository/workspace AGENTS or
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
| Local installer tests | 21 passed; 20 container-only tests intentionally skipped on host |
| Debian 13 disposable container | 41 passed, including the 20 container-only tests; no skips |
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
- Fresh provisioning for an alternate account with actual home `/srv/tabletop-screen`
  installs its launcher as that desktop UID/GID 0755, generates a backgrounded
  resolved-home launcher entry, and keeps privileged binaries root:root 0755.
- The separate existing-hub component helper inspects without writes and preserves
  direct marked Labwc integration byte-for-byte. Apply/no-op repeat/rollback keep
  fixture app/current, DB/identity, Wi-Fi credentials, browser pairing, production
  config and privilege policy unchanged. Backups are private, and partial
  replacement/rollback failures recover prior component bytes where possible.
- Unknown target/candidate hashes, custom component metadata/extended attributes,
  later autostart/component edits and noninteractive approval refusal stop the
  component procedure without silently overwriting state. The helper never invokes
  a checker/installer/launcher, app update, service or network operation.

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

## Reconciled owner evidence and pending acceptance

The final documentation base records owner-performed existing-hub observations:
Debian 13 Trixie/aarch64, graphical target and LightDM active, healthy 0.1.5 with
connected DB/current migrations/storage ready and unchanged identity, boot fallback
enabled, bridge/recovery/return active, external profiles present and recovery AP
autoconnect disabled. These observations support compatibility with the existing
deployment; they are not fresh-device test outcomes from this task.

All three original updater/checker/launcher live hash receipts now match the
unchanged snapshots. The owner explicitly corrected the malformed launcher hash
to `4bd743792f68b6041fb80e5c83cb377231bce743d5194115db146bbcb8b2c44a` copied
from the Pi terminal. Its observed `/home/dnd` location and dnd:dnd 0755/974-byte
metadata are evidence only; no personal home is hardcoded in new targets. The
transcription discrepancy is resolved, with no further Pi command/checksum request.

This **does not prove a fresh Raspberry Pi OS boot**. No ARM64 Pi, real systemd
boot, NetworkManager radio/AP/DHCP, mDNS across devices, physical desktop autologin,
actual Zenity/Chromium touchscreen, satellite pairing or real graphical application
update was exercised. These remain unchecked in the
[complete installation/rollback checklist](automated-installation.md). Simulation
does not establish equivalent physical coverage, and this draft is not a certified
image or permission to run experimental installers on working devices.

Before any separately approved existing-hub change, the maintenance procedure
checks effective deployed narrow sudo policy, script/config metadata and Python
data-filter support locally. Those are operational preconditions, not claims of
missing dependencies or a request for more live retrieval now. Fresh provisioning
selects the installed Labwc session and actual selected account home. The separate
component replacement leaves existing LightDM/Labwc/privileges/config unchanged.
Exact Waveshare SKU and video/touch connection remain unknown; no overlays or
calibration are guessed. See [existing-hub upgrade](../../hub-updater/EXISTING-HUB-UPGRADE.md)
for private backups, explicit approvals, nonexecuting validation and rollback.

No automatic satellite Wi-Fi following, two-minute hub-reachability policy,
fleet updater, renderer-hang detection, or crash-consistent power-loss rollback
is implemented. Existing policy uses the 60-second-minimum hub observation plus
bounded attempts, inactive AP autoconnect, independent controllers and lock-busy
HOLD behavior. All physical claims remain subject to acceptance on disposable
commissioning devices, followed by a separate deployment approval.
