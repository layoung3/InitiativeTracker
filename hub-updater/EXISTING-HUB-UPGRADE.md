# Existing hub: replace updater components only, after separate approval

This procedure is **not authorized to run on the working Pi by this PR**. It is
a reviewable future maintenance procedure, tested only in disposable roots.
Do not invoke `scripts/install-hub.sh`, the bootstrap, or any fresh-device recipe
on an existing hub. No merge/deployment approval is implied.

`upgrade-existing-components.py` is separate from fresh commissioning. Its default
is read-only inspection. Apply replaces exactly these three scripts:

| Script | Target / required owner and mode |
| --- | --- |
| Hardened root installer | `/usr/local/sbin/initiativetracker-install-update`, root:root 0755 |
| Hardened GUI checker | `/usr/local/bin/initiativetracker-check-update`, root:root 0755 |
| Hardened home launcher | selected account's actual passwd home + `/.local/lib/initiativetracker-hub/initiativetracker-hub-launch`, selected user:group 0755 |

It never invokes an updater/checker/launcher, restarts services, installs packages,
edits sudoers/LightDM/Labwc/config, stages a release, changes `current`, or touches
databases, keys, network profiles/credentials, journals or browser pairing data.
It preserves the existing user's Labwc integration byte-for-byte, including its
marked direct-launch block ending in `&`. It does not add the optional fresh
installer's process supervisor to an existing desktop. A launcher can disappear
from `pgrep` after startup because it `exec`s Chromium; that alone is not failure.

The graphical versioned workflow remains: Build/Test/Stage → SCP to incoming →
root-controlled bare `.manifest.json` → Labwc health/checker/zero-argument sudo
installer/health/Chromium. Replacing scripts is **not an application release**.

## 1. Establish the approved source and target receipt

Before approving use on an existing hub, review the exact final commit and its CI,
the original/runtime diff, `runtime-manifest.json`, the current live script hashes,
ownership, selected desktop account/home, autostart block, interpreter dependencies
and effective zero-argument sudo rule. Keep SSH/Ethernet/local recovery available.
Use a planned local maintenance window with kiosk/update activity paused so the
desktop cannot race the file replacement. This helper does not pause it for you.

Owner-supplied read-only evidence reports Debian 13 Trixie/aarch64, active LightDM
and graphical target, healthy application 0.1.5/current migrations/storage, unchanged
identity, enabled boot fallback, active bridge/recovery/return controllers, saved
external profiles and AP autoconnect disabled. These are **existing-device
observations**, not proof of a fresh installation or offline radio acceptance.
Never copy personal profile names/UUIDs/credentials into generic templates.

The original installer/checker hashes were verified in the live receipt and match
[`provenance.json`](provenance.json). The preserved launcher is **974 bytes** and
rehashes to `4bd743792f68b6041fb80e5c83cb377231bce743d5194115db146bbcb8b2c44a`.
The owner corrected the malformed earlier transcription by supplying the exact
64-character value copied from the Pi terminal; it matches this snapshot. The
observed launcher is `/home/dnd/.local/lib/initiativetracker-hub/initiativetracker-hub-launch`,
owner dnd:dnd, mode 0755. That path is **evidence only**, not a provisioning default.
This discrepancy is resolved; no additional Pi command/checksum request is needed
for reconciliation. The snapshot stays unchanged. Future approved replacement
still checks its actual targets before writing as an ordinary safety precondition.

Local read-only checks, **only when separately approved for collection**, include:
These are reference checks for a future approved maintenance window, not a
request for more live evidence now.

```bash
desktop_user='SELECTED_DESKTOP_USER'
desktop_home=$(getent passwd "$desktop_user" | cut -d: -f6)
test -n "$desktop_home" && test "$desktop_home" != /root
sudo stat -c '%U:%G %a %s %n' \
  /usr/local/sbin/initiativetracker-install-update \
  /usr/local/bin/initiativetracker-check-update \
  "$desktop_home/.local/lib/initiativetracker-hub/initiativetracker-hub-launch"
sudo sha256sum /usr/local/sbin/initiativetracker-install-update \
  /usr/local/bin/initiativetracker-check-update \
  "$desktop_home/.local/lib/initiativetracker-hub/initiativetracker-hub-launch"
sudo /usr/bin/python3 -I -c 'import tarfile; assert hasattr(tarfile, "data_filter")'
sudo -l -U "$desktop_user" -- /usr/local/sbin/initiativetracker-install-update
```

The last command lists policy; it does not execute the installer. Do **not** use
`sudo -n ...install-update`, the checker, or the launcher as a diagnostic test:
they can install a pending staged application release. Review policy/configuration
privately; do not post secrets or full production JSON. A policy/mode/hash mismatch
stops this procedure rather than silently repairing privileges/configuration.

## 2. Prepare a sealed reviewed bundle

On the workstation, from a clean reviewed checkout, choose the **explicitly
approved final commit SHA**. Export tracked sources only; do not bundle private
Pi files, browser profiles, databases, config backups or untracked files:

```bash
approved_sha='APPROVED_40_CHARACTER_COMMIT_SHA'
git archive --format=tar --output=it-components.tar "$approved_sha" hub-updater
sha256sum it-components.tar
```

Record the tar hash and the candidate hashes from the reviewed commit's
`hub-updater/runtime-manifest.json` through your trusted approval record. Transfer
the tar to the chosen SSH account on the intended approved hub; verify its hash
there against that record before unpacking. A checksum is copy integrity, not a
substitute for approving the source. Inspect the archive listing: only expected
tracked regular files/directories under `hub-updater/` belong in this bundle.

The following commands are a **future approved Pi maintenance step**, not work
performed here. Run as the selected commissioning operator; replace placeholders
with the approved SHA/account and verified transfer path. They create an unused
root-controlled source directory and copy only the bounded required files:

```bash
set -euo pipefail
approved_sha='APPROVED_40_CHARACTER_COMMIT_SHA'
desktop_user='SELECTED_DESKTOP_USER'
transfer="$HOME/it-components.tar"
sha256sum "$transfer"  # compare with the approved workstation receipt first
tar -tvf "$transfer"  # verify expected regular files/directories; no symlinks
unpack=$(mktemp -d)
tar -xf "$transfer" -C "$unpack" --no-same-owner
stage="/var/lib/initiativetracker-components/source-$approved_sha"
sudo test ! -e "$stage"
sudo install -d -o root -g root -m 0700 "$stage"
sudo install -d -o root -g root -m 0755 \
  "$stage/hub-updater" "$stage/hub-updater/original" "$stage/hub-updater/runtime"
for file in upgrade-existing-components.py provenance.json runtime-manifest.json; do
  sudo install -o root -g root -m 0644 "$unpack/hub-updater/$file" "$stage/hub-updater/$file"
done
for name in initiativetracker-install-update initiativetracker-check-update initiativetracker-hub-launch; do
  for variant in original runtime; do
    sudo install -o root -g root -m 0644 "$unpack/hub-updater/$variant/$name" "$stage/hub-updater/$variant/$name"
  done
done
rm -rf -- "$unpack"
sudo /usr/bin/python3 -I "$stage/hub-updater/upgrade-existing-components.py" \
  --desktop-user "$desktop_user" --inspect
```

Compare all printed installed/candidate hashes with the reviewed original/runtime
records. The helper refuses unknown target/candidate hashes, wrong ownership/mode,
component ACLs/extended attributes/capabilities needing manual preservation,
symlinks, untrusted source ancestry, changed desktop-home identity or absent
versioned `current`. It obtains the actual home from the selected passwd account;
no `/home/dnd` or other fixed account home is encoded in targets.

## 3. Private backup and explicit replacement approval

Approve the concrete source/target receipt before proceeding. Record the current
symlink, health/version/identity and script/config metadata privately. If retaining
the existing sudo/LightDM policy files too, back up **only their confirmed exact
paths** in a root-owned 0700 directory with files 0600; do not guess a sudoers
filename or alter its grant. The helper never changes those files.

The helper itself takes a private backup **before any replacement** under
`/var/backups/initiativetracker-components/<UTC>-<random>/`: all three old scripts,
their hash/UID/GID/mode/path receipt, updater config, production config and selected
user's complete Labwc autostart. Directory is root:root 0700; backup files/receipt
are root:root 0600. Private config stays on the Pi; never export it into Git/PRs.
This is a component/config backup, **not a consistent application-data backup**.
No DB migration occurs. Before a later real application update, retain the
separate [consistent private data backup](../docs/setup/hub-release.md#5-record-the-current-release-and-take-a-consistent-private-backup).

**Only after explicit authorization to change this specific existing hub**, run:

```bash
sudo /usr/bin/python3 -I "$stage/hub-updater/upgrade-existing-components.py" \
  --desktop-user "$desktop_user" --apply
```

It requires typing `REPLACE-COMPONENTS`, holds the existing updater lock, backs up,
atomically replaces each script with its reviewed runtime counterpart, preserves
the original owners/modes, and verifies config/autostart/current did not change.
It makes no sudoers or application/network changes. A complete identical repeat
is a no-op. A failure attempts to restore already-replaced scripts and prints the
private backup path; a power loss or recovery failure still needs local review.

## 4. Validate without launching an application update

Keep the existing kiosk/desktop integration. Use metadata/hash checks, read-only
inspection, shell **syntax-only** parsing and application health. Do not run the
checker, launcher or installer just to validate the replacement:

```bash
sudo /usr/bin/python3 -I "$stage/hub-updater/upgrade-existing-components.py" \
  --desktop-user "$desktop_user" --inspect
sudo /bin/bash -p -n /usr/local/sbin/initiativetracker-install-update
sudo /bin/bash -p -n /usr/local/bin/initiativetracker-check-update
desktop_home=$(getent passwd "$desktop_user" | cut -d: -f6)
sudo /bin/bash -p -n "$desktop_home/.local/lib/initiativetracker-hub/initiativetracker-hub-launch"
sudo visudo -c
readlink /opt/initiativetracker/current
systemctl is-active initiativetracker.service
curl --fail --silent --show-error http://127.0.0.1:5000/system/health
```

Expected: candidate hashes/owners match, syntax/policy valid, same `current`, same
version/identity, healthy/current migrations/storage, and byte-identical existing
autostart/config. Browser profile, trust, credentials, saved Wi-Fi priorities/AP
autoconnect, gates and independent controllers are unchanged. Empty launcher
`pgrep` after `exec Chromium` is normal; verify the screen/Chromium profile instead.
No process/screen observation here constitutes a new application release test.

A future Labwc startup may offer whatever release the existing `MANIFEST_SOURCE`
already names. Review that pending source and any existing update-mode overrides
privately before choosing a restart; do not accept an application update without
its own approval. This component procedure does not change the manifest or mode.
Existing direct launcher startup remains direct; the fresh supervisor is not
silently installed into the working user's autostart.

## 5. Roll back only the scripts

If replacement validation fails, retain all diagnostics/backups locally. Select
the exact backup path printed by apply and obtain authorization to restore it:

```bash
backup='/var/backups/initiativetracker-components/EXACT_PRINTED_BACKUP_DIRECTORY'
sudo /usr/bin/python3 -I "$stage/hub-updater/upgrade-existing-components.py" \
  --desktop-user "$desktop_user" --rollback "$backup"
```

Type `ROLLBACK-COMPONENTS`. It verifies the private receipt/snapshot hashes, desktop
layout, unchanged config/autostart/current and still-installed candidate hashes
before restoring exact prior bytes/UID/GID/mode. Later administrator/user edits
cause refusal, preserving them for review. It never overwrites configuration or
restores an application DB. Repeat the syntax/hash/read-only health checks. The
old originals have their old import/lifecycle limitations; restoring them is not
security remediation. Do not execute an old updater from an untrusted directory.
No privileges are silently removed or added as part of rollback.

## Disposable next step and merge ordering

Cloud fixture/CI tests cover alternate homes/real UID/GID/modes, state preservation,
backup privacy, cancellation, hash rejection, partial-failure recovery and script
rollback. They do not establish real ARM64 desktop/radio/update behavior. First
complete the [fresh disposable-Pi checklist](../docs/setup/automated-installation.md)
on a separate card/Pi with synthetic game data and new pairing. Exercise the
component upgrade/rollback there against the exact approved original snapshots,
and verify the live-style direct Labwc block survives unchanged. Only afterward
consider a separately authorized existing-hub change.

Documentation PR #2 precedes installer PR #3. Both require explicit merge approval;
merging either is separate from permission to deploy or run this procedure.
N8 remains the next priority after this work, not part of this change.
