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
| Local installer tests | 20 passed; 11 container-only tests intentionally skipped on host |
| Debian 13 disposable container | 31 passed, including the 11 container-only tests; no skips |
| Existing network controller suite | 195 passed |
| ShellCheck 0.10 | Passed for all three shell entry points and all derived runtime scripts |
| Python compilation and shell syntax | Passed for added Python modules and runtime shell scripts |
| `git diff --check` | Passed |
| Dry-run entry points | Hub and satellite preview ran without sudo or OS mutation; release validation exercised with synthetic archives |

Container base: official `python:3.13-slim-trixie`, digest
`sha256:2d44738ebf4ce5f63b9d9ea871e9c7cfdc0426f7b0c97cac915fd88ded1d86bb`.
Test image installs only test dependencies (`sudo` 1.9.16p2, ShellCheck) and
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

Application source and release scripts were not changed. The owner already has
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
