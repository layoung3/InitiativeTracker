# Verification record and remaining installation work

[Setup index](../../SetupReadme.md) · [Acceptance checklist](acceptance.md)

## Scope and evidence levels

Documentation review date: **2026-10-30**. Repository:
`layoung3/InitiativeTracker`, refreshed `origin/master` at
`353a1c78b0eb9150d6587a6c442c7dca76aaf5e4` (merged N7 PR #1),
`Directory.Build.props` version **0.1.5**. No repository `AGENTS.md` or
`.agents/skills` files were present; workspace `.agents` was empty. Only this
repository was modified. No live Pi connection, deployment, merge, security
configuration change, installer implementation or repeated application build was
performed. The owner explicitly reported the build was already done.

Evidence labels throughout this documentation mean:

| Label | Meaning |
| --- | --- |
| Source-verified | Inspected current tracked implementation; no proof it is installed or works on this OS/hardware |
| Previously observed / reported | Supplied historical deployment or read-only audit evidence, not repeated by this documentation task |
| Template / pending | Concrete new-device instructions or acceptance steps needing execution/confirmation |

The owner supplied a successful cutover receipt from another chat at
**2026-10-30T01:25:29.8001487+00:00**: service active, current symlink resolving to
`/opt/initiativetracker/releases/0.1.5-linux-arm64`, and `/system/health` reporting
InitiativeTracker 0.1.5 healthy with database connected, migrations current and
storage ready. This is owner-provided historical device evidence, not a new live
inspection by this task. The desktop audit independently verifies the local
package. The earlier 0.1.4 record predates this cutover receipt; neither receipt
establishes physical offline fallback, touchscreen or satellite acceptance.

## Source-to-procedure map

| Area | Tracked evidence | What it establishes / limit |
| --- | --- | --- |
| Release build | [Build script](../../scripts/Build-InitiativeTrackerRelease.ps1), [version props](../../Directory.Build.props) | Self-contained ARM64 publish, manifest format 1, artifacts/releases layout; no source SHA recorded in manifest, no tests or OS provisioning |
| Validation/staging | [Test script](../../scripts/Test-InitiativeTrackerRelease.ps1), [Stage script](../../scripts/Stage-InitiativeTrackerRelease.ps1) | Filename/size/hash/archive checks; local staging replaces version directory and renames manifest to release-manifest.json; no deployment |
| First app install | [Bootstrap](../../hub-bootstrap/install-hub-release.sh), [unit](../../hub-bootstrap/initiativetracker.service), [config template](../../hub-bootstrap/appsettings.Production.json) | Account, versioned releases, persistent data, service, initial health; not complete OS/kiosk/updater/network install and not rollback-aware |
| Identity/mDNS | [Hub identity](../../InitiativeTracker.Web/Services/HubIdentityService.cs), [hostname sync](../../hub-bootstrap/initiativetracker-sync-hostname) | SQLite-persisted GUID/name; bootstrap enables Avahi and hostname service; depends on LAN multicast/client support |
| SQLite/keys/migrations | [Program.cs](../../InitiativeTracker.Web/Program.cs) | External data option, DB/keys paths, migration-on-start, cookie/trust checks and health routes |
| Setup/display | [Setup handler](../../InitiativeTracker.Web/Pages/Setup/Index.cshtml.cs), [pairing](../../InitiativeTracker.Web/Pages/Display/Pair.cshtml.cs), [display JS](../../InitiativeTracker.Web/wwwroot/js/initiative-display.js) | Loopback-only Setup returns 404 remotely; QR generation, registered-device flow, persistent pairing, SignalR retries; physical touch/browser boot not proven |
| Hotspot provisioning | [Staging helper](../../pi-network/initiativetracker-stage-recovery-hotspot), [preparation](../../pi-network/initiativetracker-prepare-startup) | Root-only generated secret, inactive AP with autoconnect=false, exact-keyfile pre-NM repair |
| External Wi-Fi provisioning | [Provisioner](../../pi-network/initiativetracker-wifi-provision) | WPA profile saved root-only, autoconnect=false, no immediate connection; explicit admin opt-in needed for boot preference |
| Boot preference/fallback | [Boot controller](../../pi-network/initiativetracker-boot-fallback), [discovery](../../pi-network/initiativetracker-network-discovery) | Minimum 60 s, spaced scans, eligible external UUIDs, bounded attempts, active-link/pending/lock guards; not exact timing or reachability-based following |
| Independent recovery/return | [Recovery controller](../../pi-network/initiativetracker-network-control), [return controller](../../pi-network/initiativetracker-network-return), [handoff](../../pi-network/initiativetracker-return-handoff) | Durable pending state, supervised confirmation and timer recovery; gate/NM dependencies; not universal outage recovery |
| Root broker/DM controls | [Socket broker](../../pi-network/initiativetracker-network-socketd), [bridge](../../pi-network/initiativetracker-web-network-bridge), [app client](../../InitiativeTracker.Web/Services/NetworkCommandBridge.cs), [systemd units](../../pi-network/systemd) | UID and allowlist boundary, exact paths, gates and timers; requires installed files and app enable setting |
| N7 history | [N7 release notes](../N7-0.1.5-release.md) | Prior source tests/review chronology; earlier 0.1.4 baseline/candidate counts are historical, not current deployed claims |

## Supplied local artifact evidence

Desktop read-only audit of the existing build reported:

| Field | Value |
| --- | --- |
| Artifact | `InitiativeTracker-0.1.5-linux-arm64.tar.gz` |
| Informational version | `0.1.5+353a1c78b0eb9150d6587a6c442c7dca76aaf5e4` |
| Executable | ELF ARM64 Linux |
| Package bytes | `54173863` |
| Package SHA-256 | `845876f32de61bb4426aec4cf943b4a552dfed8961f339af70cf6423c94778e5` |
| Manifest SHA-256 | `1cb591a09a24d3555920202805e535c747bec3826e7e7ecd7447eb2fa4ece429` |
| Archive inspection | 625 entries, no known private-data paths; package hash/size match manifest |
| Staging state | Completed release folder exists; expected local Stage directory absent |

This identifies **that build only**, not every future package labeled 0.1.5. Use
actual manifest values for subsequent artifacts. Rebuilding may change bytes/hash.
Run validation/staging on the retained artifact; absent staging is not a reason to
repeat a completed build. A package-content scan is not proof of publisher trust.

## Supplied installed-OS observations

The working layout was previously audited as:

- `/opt/initiativetracker/current` → versioned release;
  persistent SQLite and `DataProtection-Keys` under `/var/lib/initiativetracker`.
- App service user/group `initiativetracker`, working directory `current`,
  `ReadWritePaths=/var/lib/initiativetracker`, with existing `network-commands.conf`.
- Production source `/etc/initiativetracker/appsettings.Production.json` copied
  into each new release by the external installer. Earlier 0.1.4 source/active
  config hashes matched; this does not prove today's config equivalence.
- Bare local `MANIFEST_SOURCE` under `/var/cache/initiativetracker-updater/incoming`,
  with filename matching `*.manifest.json`; the Stage name `release-manifest.json`
  must be renamed for that incoming copy. Package remains beside the manifest.
  Graphical updater supports 0.1.5; no replacement updater was required for that
  app upgrade. Network resources are separate from the package.
- LightDM/graphical target, Labwc autostart, desktop-user launcher (974 bytes):
  unbounded app-health wait → graphical updater → unbounded app-health wait →
  `exec` Chromium Setup, without a browser restart loop. Checker uses Zenity and
  zero-argument `sudo -n` installer invocation; checker exit 0 can include failure.
- Installer stops app before full-data backup, then switches releases; health
  failure restores old release **and data**. Other failures may bypass rollback.
  Backup root is created mode 0755 and archive permissions inherit process umask.
  Its backup privacy remains unresolved. A global umask 077 workaround can make
  root-owned release directories 0700 and break service access; the guide scopes
  restrictive modes to the separate manual checkpoint only.
- Installer always copies production config into the release root-owned 0644.
  Source mode 0640 does not protect that copy: configuration must remain non-secret
  until a separate approved secret-configuration design exists.
- External Wi-Fi worked after reboot with the corrected boot controller; the
  supplied check saw no new errors in 56 records. Physical offline fallback and
  satellite following were not verified. Lock contention logs HOLD/no action.

Prior audited installed-file hashes (comparison references, not files supplied by
this repo):

```text
/usr/local/sbin/initiativetracker-install-update
17bfda06c20f3d8b932d7f7f1bf341ed17107e32c602586f9680e463d9542c56
/usr/local/bin/initiativetracker-check-update
16e57846ace608c5e81d394107612d7d34100e82c06ef23e39b3aa409d607d4d
desktop-user initiativetracker-hub-launch
4bd743792f68b6041fb80e5c83cb377231bce743d5194115db146bbcb8b2c44a
corrected installed initiativetracker-boot-fallback
da1f8ee834c49cb2563ac0c77789ab837fc4ed2d7d4defd8b1a9e1bdc94ce859
```

The tracked boot-fallback script at the inspected baseline has that same SHA-256;
this task verified the repository file hash, not a new installed-device copy.

The owner preserved the unchanged installer, checker and launcher in
`InitiativeTracker-updater-source-handoff-17bfda06.zip`. Independent review of that
handoff verified all three hashes and supplied the suffix, invocation, permission
and launcher findings above. Local source-byte inspection in this documentation
workspace remains pending because the Library handoff could not be downloaded;
these findings are credited to the independent exact-source review. No OS script
was executed here.

Preserved source is not yet a reproducible first-device provisioning recipe.
Approved distribution, complete GUI/native dependencies, precise narrow privilege
integration, config installation and keyboardless recovery/acceptance still need
to be assembled and reviewed. Preserve the proven mechanism rather than improvise
an updater to fill that gap.

## Missing automation and proposed installer architecture

No functional installer work is included in this documentation branch.

| Missing piece | Proposed follow-up / completion criterion |
| --- | --- |
| Approved graphical updater distribution | Source is preserved/audited; still package its approved install recipe, Zenity/native dependencies, zero-argument noninteractive privilege integration, config and backup layout. Preserve the current healthy-release workflow and `.manifest.json` incoming-name requirement. |
| Hub desktop/display provisioning | Install Labwc autostart and health/updater/browser sequence for a selected desktop user; record exact Waveshare SKU/wiring/mode/touch mapping, fullscreen and blanking. Current launcher has unbounded health waits and no browser restart loop; add a separately reviewed recovery policy and validate hardware. |
| Complete first-install orchestrator | Compose existing release validation/bootstrap, external production config source, native dependencies, network files/units and approved desktop/updater provisioning. Detect OS/architecture/interface and refuse existing production installs unless explicitly supported. |
| Transactional preflight and recovery | Validate before mutating, check disk/power/dependencies, record provenance, explicitly private backup files without breaking release readability, full error-path rollback and explicit schema/data-loss policy. Fix unconditional 0644 production-config copies before supporting secrets there. Test failures without production data. |
| Network commissioning tool | Stage unique AP/secret, configure saved profile eligibility/priority and unique compatible profile IDs (current previous-name validation is restrictive), install helpers/socket/independent timers and exact gates only after recovery checks. Keep installation, dry-run validation and opt-in activation distinct. Preserve existing profiles/secrets. |
| Satellite installer/launcher | Persistent dedicated browser profile and stable-origin/GUID configuration, desktop autostart, bounded health waiting, process/renderer recovery, duplicate-process guard and private rotating logs. The guide template needs hardware validation and hardening. |
| Satellite network following (N8) | Define actual hub-reachability checks (~2 min requested intent), safe manual fallback/confirm flow and hub identity handling. Saved-profile priority alone is insufficient. No current implementation/verification. |
| Repeatable acceptance/image record | Run both fresh devices, online/offline/unusable Wi-Fi boots, touch/QR, confirmation expiry, game sync, newer release install, persistence and restore. Save non-secret receipts per OS/hardware/version. |

A future installer should expose **preflight**, **fresh provision**, **verify** and
**recovery** phases with idempotent checks and explicit existing-install refusal.
Application releases should continue through the existing versioned graphical
updater. OS/network changes need their own reviewed manifests, backups and local
recovery path. Do not bundle production DBs, passwords, tokens or machine identity
into a generic image or Git repository.

## Documentation validation for this branch

Checks completed for the documentation:

- 10 Markdown files reviewed; 71 internal file links/section targets resolve.
- 31 fenced shell blocks and six nested shell scripts pass `bash -n`.
- Five Python snippets parse; the transfer-check example accepts a matching
  synthetic pair and rejects both corruption and a traversal filename (three checks
  using disposable temporary files).
- 18 disposable recovery-script cases pass: successful binary/data restoration;
  rejected release/backup pairing and checksum; failed stop, preservation-directory
  creation, failed-data move, extraction, integrity/migration check, data install,
  symlink operations and health. Service, SQLite, health and ownership checks are
  stubbed; ACL flags are omitted on the temporary test filesystem. These verify
  shell failure sequencing and preservation, not live restore correctness.
- The reproducible incoming-manifest name check below passes for the versioned
  incoming name and rejects a disposable copy using the original generic name.
- Referenced source paths, PowerShell parameters, units, gates, endpoints,
  profile semantics and the boot-controller hash were cross-checked with source.
- Documentation-only diff, placeholders and secret handling reviewed;
  `git diff --check` passes.

PowerShell is unavailable in this environment, so its examples were reviewed
against the scripts rather than executed. No .NET rebuild, packaging, live Pi
check, network activation or physical acceptance is claimed. Historical N7 test
results remain in the N7 notes and are not new runs. The PR records the final
branch/commit and validation summary.

### Reproduce the incoming-manifest name regression check

Run from repository root on a workstation with Python 3. This validates the
documented rename against the installed installer's reviewed `*.manifest.json`
requirement; it does not execute the installer or inspect a device:

```bash
python3 - <<'PY'
from pathlib import Path
import fnmatch
import re
guide = Path('docs/setup/hub-release.md').read_text()
source = re.search(r'^MANIFEST_SOURCE=(\S+)$', guide, re.M).group(1)
destination = re.search(r'0644 release-manifest\.json \\\n\s+(/\S+)', guide).group(1)
assert source.startswith('/var/cache/initiativetracker-updater/incoming/')
assert fnmatch.fnmatchcase(Path(source).name, '*.manifest.json')
assert not fnmatch.fnmatchcase('release-manifest.json', '*.manifest.json')
assert source == destination
print('INCOMING MANIFEST NAME CHECK PASSED')
PY
```

Expected: `INCOMING MANIFEST NAME CHECK PASSED`. Replacing the documented incoming
filename with Stage's generic filename must fail this check; do not weaken the
installer to make that filename work.
