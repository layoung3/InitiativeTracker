# Canonical hub release procedure

[Setup index](../../SetupReadme.md) · [Recovery](recovery.md)

This is the existing **Windows build → validate → stage → SCP → incoming → local
manifest → reboot → graphical updater** workflow. It preserves versioned releases
and persistent data. The scripts are source-verified; the graphical installer
behavior below comes from prior installed-OS audit evidence, not a fresh run.
This documentation task performs no build or deployment. If already built, use
the existing reviewed archive and begin at section 2.

## 1. Select source/version and build only when needed

Use a clean workstation checkout. Save local work before changing branches.
`0.1.5` is the current deployed baseline, not an instruction to relabel an unrelated
build or reinstall an existing release directory. For a future release, select
the reviewed commit and its approved version change in `Directory.Build.props`.
There is no `-Version` parameter on the build script. Never edit a manifest's
version after packaging.

**Windows PowerShell, repository root:**

```powershell
git status --short
git fetch origin
git switch master
git pull --ff-only origin master
git rev-parse HEAD
[xml]$props = Get-Content .\Directory.Build.props -Raw
$version = [string]($props.Project.PropertyGroup.VersionPrefix | Select-Object -First 1)
$version
dotnet --version
```

Expected: clean status; intended source SHA; `0.1.5` for the inspected baseline;
SDK `10.x`. Check the SHA against the release approval. A clean tree and matching
label alone do not prove an older artifact came from that SHA; keep its build log.

For a release not already built:

```powershell
dotnet build .\InitiativeTracker.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet test .\InitiativeTracker.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
.\scripts\Build-InitiativeTrackerRelease.ps1 -ReleaseNotes @('Approved release notes')
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
```

Expected: successful build/tests and `Release created:`. N7's prior test record was
75 .NET tests with eight known warnings; future counts can differ. The script
publishes **self-contained** `linux-arm64`, archives the publish root, and emits
format-1 manifest metadata, size and SHA-256. It does **not** run tests, install
networking or package the graphical OS updater. Self-contained publishing means a
system .NET runtime is not required to launch this archive; native OS libraries
are still required. The release manifest has no source-commit field or signature:
retain the source SHA/build log separately and use a trusted transfer source.

## 2. Validate and stage the existing artifact

If resuming after an earlier build, set `$version` to **that artifact's approved
version**, not whatever today's source reports. The example uses the reported
0.1.5 baseline. Point `$manifest` to your actual retained output if different.

```powershell
$version = '0.1.5'
$releaseName = "InitiativeTracker-$version-linux-arm64"
$manifest = ".\artifacts\releases\$releaseName\$releaseName.manifest.json"
.\scripts\Test-InitiativeTrackerRelease.ps1 -ManifestPath $manifest
if ($LASTEXITCODE -ne 0) { throw 'Release validation failed' }
.\scripts\Stage-InitiativeTrackerRelease.ps1 -ManifestPath $manifest -StagingRoot .\artifacts\staged
if ($LASTEXITCODE -ne 0) { throw 'Staging failed' }
$stage = ".\artifacts\staged\$version-linux-arm64"
Get-ChildItem $stage
$m = Get-Content "$stage\release-manifest.json" -Raw | ConvertFrom-Json
Get-FileHash "$stage\$($m.package.fileName)" -Algorithm SHA256
```

Expected: `Release validation PASSED`; `Release staged successfully`; staged
`release-manifest.json`, versioned `.tar.gz`, `staged-release.json`. Hash must match
`package.sha256`. Validation checks product, format, platform, version syntax,
health path, package filename/size/hash, archive traversal names and root
executable. It is not a malware/signature check. Stage reruns validation and
**replaces an existing local staging directory for that version**; it activates
nothing. Keep approved artifacts outside staging if you need immutable history.

## 3. Transfer from Windows

Use the desktop/SSH account selected in Imager, not the service account. Resolve
the hub's current hostname from `/system/hub-info`, or use a known LAN IP.

```powershell
$piUser = 'PI_USER'
$hubAddress = 'HUB_ADDRESS'
$piTarget = "${piUser}@${hubAddress}"
ssh $piTarget 'mkdir -p "$HOME/it-release-transfer"'
if ($LASTEXITCODE -ne 0) { throw 'SSH directory creation failed' }
scp "$stage\release-manifest.json" "$stage\$($m.package.fileName)" "${piTarget}:it-release-transfer/"
if ($LASTEXITCODE -ne 0) { throw 'SCP failed' }
```

Expected: both transfers complete with exit 0. Verify the SSH host fingerprint
through your trusted device record; a newly imaged device can legitimately change
keys, but do not blindly remove warnings.

**Pi Bash:**

```bash
cd "$HOME/it-release-transfer"
python3 - <<'PY'
import hashlib, json, pathlib
m = json.loads(pathlib.Path('release-manifest.json').read_text(encoding='utf-8-sig'))
name = m['package']['fileName']
assert '/' not in name and '\\' not in name and '..' not in name
p = pathlib.Path(name)
assert m['formatVersion'] == 1 and m['product'] == 'InitiativeTracker'
assert m['platform'] == 'linux-arm64' and m['healthPath'] == '/system/health'
assert p.stat().st_size == m['package']['sizeBytes']
assert hashlib.file_digest(p.open('rb'), 'sha256').hexdigest() == m['package']['sha256'].lower()
print('TRANSFER CHECK PASSED:', m['version'], name)
PY
```

Expected: `TRANSFER CHECK PASSED: 0.1.5 ...` for this artifact. Stop on any exception;
the earlier workstation archive validation is also required.

## 4. Stage the incoming package and manifest source

These commands assume the **existing approved updater is already installed**.
Check presence with `sudo test -x /usr/local/sbin/initiativetracker-install-update`
and `test -x /usr/local/bin/initiativetracker-check-update`; both should exit 0.
Do not run the installer manually as a substitute for its graphical entry point.

Use the exact package filename just verified (change version for later releases):

```bash
sudo install -d -o root -g root -m 0755 /var/cache/initiativetracker-updater/incoming
sudo install -o root -g root -m 0644 \
  InitiativeTracker-0.1.5-linux-arm64.tar.gz \
  /var/cache/initiativetracker-updater/incoming/
sudo install -o root -g root -m 0644 release-manifest.json \
  /var/cache/initiativetracker-updater/incoming/release-manifest.json
sudo sha256sum /var/cache/initiativetracker-updater/incoming/InitiativeTracker-0.1.5-linux-arm64.tar.gz
sudoedit /etc/initiativetracker/updater.conf
```

Set the existing variable without discarding the file's other settings:

```ini
MANIFEST_SOURCE=/var/cache/initiativetracker-updater/incoming/release-manifest.json
```

**This is a bare local path, never `file://...`.** Verify only that setting, without
printing the rest of production configuration:

```bash
sudo grep '^MANIFEST_SOURCE=' /etc/initiativetracker/updater.conf
```

Expected: the exact assignment above; incoming hash equals the manifest and Windows
hash. Do not place databases or private configuration in `incoming`.

## 5. Record the current release and take a consistent private backup

Schedule a pause in play. Record locally (not in Git) the current symlink, health,
version and migration metadata. The existing installer stops the app before its
own full data backup, but take an independently restorable checkpoint as well.
Never copy just an open SQLite database and assume it is consistent.

```bash
readlink -f /opt/initiativetracker/current
curl --fail --silent --show-error http://127.0.0.1:5000/system/health
curl --fail --silent --show-error http://127.0.0.1:5000/system/status
```

Expected: current versioned directory, HTTP 200 with `status=healthy`, connected
database, current migrations, ready storage. Save the previous release path.

**Maintenance window, Pi Bash:**

```bash
backup_dir="/var/backups/initiativetracker/manual-$(date -u +%Y%m%dT%H%M%SZ)"
sudo install -d -o root -g root -m 0700 "$backup_dir"
sudo systemctl stop initiativetracker.service
systemctl is-active initiativetracker.service
```

Expected: `inactive` (the last command normally exits 3). Confirm stopped before
continuing. Run the backup, and do not proceed to updating if it fails:

```bash
sudo bash -s -- "$backup_dir" <<'SH'
set -euo pipefail
umask 077
dest=$1
readlink -f /opt/initiativetracker/current > "$dest/previous-release.txt"
tar --acls --xattrs -C / -czf "$dest/data-and-config.tar.gz" \
  var/lib/initiativetracker etc/initiativetracker
systemctl cat initiativetracker.service > "$dest/app-service.txt"
if [ -d /etc/systemd/system/initiativetracker.service.d ]; then
  cp -a /etc/systemd/system/initiativetracker.service.d "$dest/"
fi
sha256sum "$dest/data-and-config.tar.gz" > "$dest/backup.sha256"
tar -tzf "$dest/data-and-config.tar.gz" >/dev/null
SH
sudo systemctl start initiativetracker.service
curl --retry 30 --retry-connrefused --retry-delay 2 --max-time 5 \
  --fail --silent --show-error http://127.0.0.1:5000/system/health
sudo stat -c '%U:%G %a %n' "$backup_dir" "$backup_dir/data-and-config.tar.gz"
```

Expected: healthy again; directory `root:root 700`, archive `root:root 600`;
backup includes the entire data directory (SQLite including any WAL/SHM, keys,
identity and trust), config and service overrides. If backup fails, restart the
old application and resolve it before continuing. Copy the private backup to
protected offline storage. Existing updater backups inherit its process umask;
`umask 077` above is a recommendation for this manual backup, **not** a claim that
the deployed updater already enforces it. Verify actual updater backup modes.

## 6. Install through the graphical updater

With a person at the touchscreen and the previous release/backup retained, reboot
from the desktop or run `sudo reboot` during the maintenance window. Reconnect
after reboot. The observed Labwc startup is:

`app healthy → graphical update check/prompt → app healthy → Chromium`.

Accept the offered approved version in that existing graphical updater. A package
with the same version as the active release may correctly produce no update; use
a separately approved newer release for an install acceptance test. Do not invent
an `--install` flag or manually overwrite `current` to make the prompt appear.

Expected: updater completes, application restarts, then the local Setup page opens
in kiosk Chromium. Cancellation should retain the working release; inspect health
and symlink rather than assuming success. The installed updater copies
`/etc/initiativetracker/appsettings.Production.json` into a new release. Preserve
that production source and the existing `network-commands.conf` service drop-in.

## 7. Verify the result

```bash
systemctl is-active initiativetracker.service
readlink -f /opt/initiativetracker/current
curl --fail --silent --show-error http://127.0.0.1:5000/system/health
curl --fail --silent --show-error http://127.0.0.1:5000/system/status
systemctl is-active initiativetracker-network-bridge.socket
systemctl list-timers --all 'initiativetracker-*'
sudo journalctl -u initiativetracker.service -b -n 60 --no-pager
```

Expected: `active`; selected `<approved-version>-linux-arm64` directory; health
version equals the intended version; zero pending migrations; no new startup
errors; bridge socket active and commissioned timers scheduled. Review logs
locally, redact before sharing. `/system/status` shows informational version but
cannot prove a package/source match by itself.

On the hub open `http://127.0.0.1:5000/setup`: QR cards and network status work.
From a phone on the same network open the advertised URL, authenticate as DM, open
`/dm/network` and verify status. Pair/reconnect a satellite; change HP and advance
a turn, and see the display update. Check existing characters, encounter and
device trust survived. Do disruptive network acceptance tests separately from play
using [the checklist](acceptance.md).

## 8. Failed release

The previously audited installer restores both old release **and its data backup**
when the new health check fails. Other error paths may not perform that rollback.
Always inspect symlink, health and logs after failure. An automatic data restore
can discard writes made since its checkpoint. Do not retry blindly or delete the
failed release/data. Follow [application and database recovery](recovery.md#application-rollback)
to distinguish a binary-only rollback from a schema-dependent restore.
