# N9 application and desktop-local branding

[Setup index](../../SetupReadme.md) · [Fresh installer](automated-installation.md) · [Single 0.1.6 release/rollback plan](../https/0.1.6-integration-release-plan.md)

Source-verified against merged master **`d9470510eba7af584d5c8f8ba05250d275979a62`** (PR #5). These are future operator instructions, not a record of Pi changes. No device access, deployment, restart, reboot or production migration was performed. A documentation merge grants none of those approvals. Keep the working 0.1.5 cards/installations intact.

## Three separate kinds of artwork

| Use | Repository source / installed location | Provisioning and limits |
| --- | --- | --- |
| Bundled application Colophon | `InitiativeTracker.Web/wwwroot/images/colophon.png` → `/opt/initiativetracker/releases/<version>-linux-arm64/wwwroot/images/colophon.png`; active path `/opt/initiativetracker/current/wwwroot/images/colophon.png` | Ships in the versioned app archive. Public `/branding/logo` uses it unless a customer logo is configured. Do not edit the active release. |
| Hub local loading dialog | Same PNG → selected desktop passwd home + `/.local/lib/initiativetracker-hub/colophon.png` | Separate desktop file. N9 launcher uses it as Zenity's icon while waiting before/after the graphical updater, then closes the dialog before Chromium. No guaranteed full-screen boot image or automatic update of this file. |
| Satellite local waiting page | Same PNG → selected desktop passwd home + `/.local/lib/initiativetracker-satellite/colophon.png` | `waiting.html` beside it references `colophon.png`. N9 `kiosk.py` shows this inert local page while health/hub-ID checks fail and terminates its separate waiting browser before opening the paired display. |

The original PNG is **300×300, 73,110 bytes**, SHA256:

```text
17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1
```

The phoenix is application branding. Default character portraits remain the generic fantasy silhouette `wwwroot/images/default-character.svg`; the neutral mountain banner remains `wwwroot/images/default-banner.svg`. Never replace those with the phoenix.

## Fresh hub and satellite provisioning

Use the [fresh installer](automated-installation.md) only on separately approved new/disposable installations, from the pinned complete source tree. Include `InitiativeTracker.Web/wwwroot/images/colophon.png` in that tree: `desktop_install()` copies it **if present**; a stripped source transfer can omit the artwork. Stop and correct the source transfer before commissioning. Application release files alone do not supply desktop components.

The installer resolves the selected desktop account from passwd, installs these files as its numeric UID/primary GID, and writes one Labwc entry:

| Desktop-local file / directory | Fresh hub | Fresh satellite |
| --- | --- | --- |
| `~/.local/lib/initiativetracker-<role>/` | **0755**: updater provisioning creates it first; later desktop mkdir preserves it | **0700** |
| `colophon.png` | 0644 | 0644 |
| `kiosk.py` | 0755 | 0755 |
| `initiativetracker-hub-launch` | 0755, same directory | Not installed |
| `waiting.html` | Not used | 0644, same directory |
| `~/.config/initiativetracker-<role>/kiosk.json` | 0600 | 0600, retains configured hub URL/ID |
| `~/.config/labwc/autostart` | 0700; quoted home launcher `--supervise` → same supervisor → no-argument launcher | 0700; `/usr/bin/python3 -I` + quoted local supervisor/config |

Do not recursively chmod/chown an existing home. Hub startup preserves **health → graphical checker/root updater → health → Chromium `/setup`** and `~/.config/initiativetracker-hub/chromium`. Satellite pairing uses `~/.local/share/initiativetracker-satellite/chromium`; waiting uses `~/.local/state/initiativetracker-satellite/waiting-chromium`. Do not clear, move or substitute either profile.

## Existing devices: compatibility gate before copying anything

An app update and the [three-script existing-hub component upgrader](../../hub-updater/EXISTING-HUB-UPGRADE.md) do **not** copy PNGs, `kiosk.py` or `waiting.html`. Never rerun a fresh installer on a working device. Read only after approval for that device; make changes only after **separate artwork-maintenance approval**, with desktop/kiosk/update activity quiescent by an independently approved local maintenance arrangement. This procedure neither stops those processes nor starts them.

The commands below require the exact supported components from the pinned merge:

| Component | Required SHA256 |
| --- | --- |
| Hub `initiativetracker-hub-launch` | `eaa45101302625ac63da42f3da1e35e19798bae7f6aff9cd2fefdfce1335f67a` |
| Supervisor `kiosk.py` | `053c1a6f0635f221fad2d0d0a3eb7b8fd916d28644b2abcee40e6140c5e7358e` |
| Satellite `waiting.html` | `7462a27f4fcf1a4f9bcdb9f5a0bf80d176eb56defe13534dc68eb187f55c3f02` |

A direct existing hub launcher needs no supervisor. A hub Labwc entry using `--supervise` also requires the approved supervisor and existing role-matched JSON. Satellite requires approved supervisor, waiting HTML and role-matched JSON. The checked Labwc command must point to this selected home/config. Symlinks, hardlinked files, special modes, ACL/xattrs or an unknown/older/intermediate component cause a stop, not a permission repair or hash bypass.

**If unsupported, stop here.** For a hub, request a separate component-upgrade review using the linked three-script procedure and its original/current-only hash policy. It preserves direct Labwc integration and does not add supervisor/support files. For a satellite or missing/modified supporting files, no equivalent bounded existing-satellite upgrader is supplied: obtain a separately reviewed, device-specific component/config backup and upgrade plan before proceeding. The historical manual satellite shell wrapper does not show N9 waiting artwork. A PNG copy cannot upgrade it. Do not copy runtime scripts from this artwork recipe, replace Labwc entries, or bypass an unknown intermediate hash.

### 1. Windows PowerShell: prepare and transfer only the approved original artwork

Use a trusted local checkout of `layoung3/InitiativeTracker`; replace SSH placeholders. A checksum verifies copy integrity; approving the pinned source is a separate owner decision. These commands do not build/start the app or apply a migration.

```powershell
$ErrorActionPreference = 'Stop'
$Pinned = 'd9470510eba7af584d5c8f8ba05250d275979a62'
$LogoSha = '17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1'
$SshUser = 'YOUR_SSH_ACCOUNT'
$PiAddress = 'APPROVED_PI_ADDRESS'
git fetch --no-tags origin master
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed' }
git cat-file -e "$Pinned^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'Pinned source absent' }
$Transfer = Join-Path ([IO.Path]::GetTempPath()) ('it-branding-' + [guid]::NewGuid())
New-Item -ItemType Directory $Transfer | Out-Null
$Archive = Join-Path $Transfer 'n9-branding-source.tar'
git archive --format=tar --output="$Archive" $Pinned InitiativeTracker.Web/wwwroot/images/colophon.png
if ($LASTEXITCODE -ne 0) { throw 'Archive failed' }
tar -xf $Archive -C $Transfer
if ($LASTEXITCODE -ne 0) { throw 'Local artwork extraction failed' }
$Logo = Join-Path $Transfer 'InitiativeTracker.Web/wwwroot/images/colophon.png'
if ((Get-FileHash $Logo -Algorithm SHA256).Hash.ToLowerInvariant() -ne $LogoSha) { throw 'Artwork mismatch' }
$ArchiveSha = (Get-FileHash $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
"Pinned source: $Pinned"
"Archive SHA256: $ArchiveSha"  # retain in the trusted transfer receipt
scp -- $Archive "${SshUser}@${PiAddress}:n9-branding-source.tar"
if ($LASTEXITCODE -ne 0) { throw 'Transfer failed' }
```

### 2. Pi SSH Bash: verify transfer and select the actual desktop account

The SSH account can differ from the desktop account. Do not assume `pi`, `dnd`, `/home/<name>` or the SSH account's home is the desktop home. In the **same interactive Bash session**, select the independently checked desktop/LightDM account, role and archive hash. Compare this selection with the already reviewed LightDM/login-session account inventory; stop if the active desktop account is uncertain. No configuration is changed:

```bash
set -euo pipefail
desktop_user='YOUR_ACTUAL_DESKTOP_ACCOUNT'
role='hub'  # or satellite, on that satellite only
archive_sha='PASTE_THE_WORKSTATION_ARCHIVE_SHA256'
transfer="$HOME/n9-branding-source.tar"
printf '%s  %s\n' "$archive_sha" "$transfer" | sha256sum --check -
getent passwd "$desktop_user"  # verify actual home/UID/GID locally; no default name
transfer_stage=$(mktemp -d "$HOME/it-branding.XXXXXXXX")
python3 -I - "$transfer" "$transfer_stage/colophon.png" <<'PY'
import hashlib, os, sys, tarfile
from pathlib import PurePosixPath
name = 'InitiativeTracker.Web/wwwroot/images/colophon.png'
allowed = {str(p) for p in PurePosixPath(name).parents if str(p) != '.'}
with tarfile.open(sys.argv[1], 'r:') as archive:
    members = archive.getmembers()
    assert len({m.name.rstrip('/') for m in members}) == len(members), 'Duplicate archive member'
    assert all((m.isdir() and m.name.rstrip('/') in allowed) or (m.isfile() and m.name == name and m.size == 73110) for m in members), 'Unexpected file/link in archive'
    data = archive.extractfile(archive.getmember(name)).read(73111)
    assert len(data) == 73110 and hashlib.sha256(data).hexdigest() == '17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1', 'Wrong artwork'
with open(sys.argv[2], 'xb') as stream: stream.write(data)
os.chmod(sys.argv[2], 0o644)
PY
source_png="$transfer_stage/colophon.png"
```

### 3. Pi SSH Bash: define the bounded inspect/apply/rollback command

Copy the entire function below into that same session. It reads passwd for the selected account and never executes a launcher, checker, supervisor or config. It changes only that role's `colophon.png`, plus its own new private backup. Directory modes are preserved. It uses no-follow file descriptors and exclusive/atomic writes; unsupported metadata stops for review. Existing asset/support files are bounded to 5 MiB; the approved source PNG must be exactly 73,110 bytes. Backups preserve original bytes, UID/GID, mode and nanosecond atime/mtime, or record absence. Inode/ctime cannot be restored by atomic replacement. Labwc, JSON, component files and browser profiles are never rewritten. Do not run during concurrent desktop maintenance.

<!-- branding-procedure:begin -->
```bash
branding_artwork() {
    sudo /usr/bin/python3 -I - "$@" <<'PY'
import fcntl, hashlib, json, os, pwd, stat, sys, time, uuid, shlex
from pathlib import Path

PIN = 'd9470510eba7af584d5c8f8ba05250d275979a62'
PNG = '17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1'
LAUNCH = 'eaa45101302625ac63da42f3da1e35e19798bae7f6aff9cd2fefdfce1335f67a'
KIOSK = '053c1a6f0635f221fad2d0d0a3eb7b8fd916d28644b2abcee40e6140c5e7358e'
WAIT = '7462a27f4fcf1a4f9bcdb9f5a0bf80d176eb56defe13534dc68eb187f55c3f02'
LIMIT = 5 * 1024 * 1024

def require(ok, why):
    if not ok: raise RuntimeError(why)

def directory(path, create=False, owner=None, private=False):
    path = Path(path)
    require(path.is_absolute() and '..' not in path.parts, 'Absolute normalized path required')
    fd = os.open('/', os.O_RDONLY | os.O_DIRECTORY)
    try:
        for part in path.parts[1:]:
            if create:
                try: os.mkdir(part, 0o700, dir_fd=fd)
                except FileExistsError: pass
            nxt = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd); fd = nxt
        info = os.fstat(fd)
        if owner is not None:
            require(info.st_uid == owner and not info.st_mode & 0o022, 'Unsafe directory ownership/write permissions')
            require(not os.listxattr(fd), 'Directory ACL/xattrs need separate review')
        if private: require(stat.S_IMODE(info.st_mode) == 0o700, 'Backup directory must be 0700')
        return fd
    except BaseException:
        os.close(fd); raise

def read_file(fd, name, missing=False):
    try: handle = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NOATIME, dir_fd=fd)
    except FileNotFoundError:
        if missing: return None, None
        raise
    try:
        info = os.fstat(handle)
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_size <= LIMIT, 'Expected bounded regular file, no hardlinks')
        require(not info.st_mode & 0o7000 and not os.listxattr(handle), 'Special modes/ACL/xattrs need separate review')
        with os.fdopen(os.dup(handle), 'rb') as stream: data = stream.read(LIMIT + 1)
        require(len(data) == info.st_size, 'File changed during read')
        return data, dict(uid=info.st_uid, gid=info.st_gid, mode=stat.S_IMODE(info.st_mode),
                          atime=info.st_atime_ns, mtime=info.st_mtime_ns, length=len(data), sha256=hashlib.sha256(data).hexdigest())
    finally: os.close(handle)

def same(a, b):
    if a is None or b is None: return a is b
    return all(a[k] == b[k] for k in ('uid','gid','mode','mtime','length','sha256'))

def atomic_file(fd, name, data, meta):
    temporary = '.branding-' + uuid.uuid4().hex
    handle = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=fd)
    try:
        with os.fdopen(handle, 'wb') as stream:
            stream.write(data); stream.flush()
            os.fchown(stream.fileno(), meta['uid'], meta['gid']); os.fchmod(stream.fileno(), meta['mode'])
            os.utime(stream.fileno(), ns=(meta['atime'], meta['mtime'])); os.fsync(stream.fileno())
        os.replace(temporary, name, src_dir_fd=fd, dst_dir_fd=fd); os.fsync(fd)
    finally:
        try: os.unlink(temporary, dir_fd=fd)
        except FileNotFoundError: pass

def guards(desktop, role, local):
    home = Path(desktop.pw_dir)
    # Read/hash only: no execution, config output, browser access or rewriting.
    expected = {'initiativetracker-hub-launch': LAUNCH} if role == 'hub' else {'kiosk.py': KIOSK, 'waiting.html': WAIT}
    fd = directory(local, owner=desktop.pw_uid)
    try:
        for name, digest in expected.items():
            _, meta = read_file(fd, name)
            require(meta['sha256'] == digest and meta['uid'] == desktop.pw_uid and meta['gid'] == desktop.pw_gid,
                    'Unsupported/older/modified component; STOP for separately approved component upgrade')
            require(meta['mode'] == (0o644 if name == 'waiting.html' else 0o755), 'Component mode mismatch; STOP')
    finally: os.close(fd)
    paths = [Path(local) / name for name in expected]
    auto = Path(desktop.pw_dir) / '.config/labwc/autostart'
    fd = directory(auto.parent, owner=desktop.pw_uid)
    try: content, _ = read_file(fd, auto.name)
    finally: os.close(fd)
    component = local / ('initiativetracker-hub-launch' if role == 'hub' else 'kiosk.py')
    def tokens_for(path):
        relative = str(path.relative_to(home))
        return {str(path), '$HOME/'+relative, '${HOME}/'+relative, '~/'+relative}
    matches = []
    for line in content.decode().splitlines():
        words = shlex.split(line, comments=True)
        if role == 'hub' and words and words[0] in tokens_for(component): matches.append(words)
        if role == 'satellite' and len(words) >= 4 and words[0:2] == ['/usr/bin/python3','-I'] and words[2] in tokens_for(component): matches.append(words)
    require(len(matches) == 1, 'Expected single supported Labwc command absent/duplicated; STOP')
    words = matches[0]
    paths.append(auto)
    config = Path(desktop.pw_dir) / f'.config/initiativetracker-{role}/kiosk.json'
    supervised = role == 'satellite' or '--supervise' in words
    if supervised:
        position = 3 if role == 'satellite' else words.index('--supervise') + 1
        require(len(words) > position and words[position] in tokens_for(config), 'Labwc uses another config/home; STOP')
        fd = directory(config.parent, owner=desktop.pw_uid)
        try: raw, _ = read_file(fd, config.name)
        finally: os.close(fd)
        require(json.loads(raw).get('role') == role, 'Desktop role/config mismatch')
        paths.append(config)
        if role == 'hub':
            fd = directory(local, owner=desktop.pw_uid)
            try:
                _, meta = read_file(fd, 'kiosk.py')
                require(meta['sha256'] == KIOSK and meta['mode'] == 0o755 and meta['uid'] == desktop.pw_uid and meta['gid'] == desktop.pw_gid,
                        'Missing/unsupported hub supervisor; STOP for component review')
            finally: os.close(fd)
            paths.append(Path(local) / 'kiosk.py')
    result = {}
    for path in paths:
        fd = directory(path.parent)
        try: _, meta = read_file(fd, path.name)
        finally: os.close(fd)
        result[str(path)] = {k: v for k, v in meta.items() if k != 'atime'}
    return result

def _run(action, desktop, role, source, backup, backup_base):
    require(action in ('inspect','apply','rollback') and role in ('hub','satellite'), 'Invalid action/role')
    home = Path(desktop.pw_dir)
    require(1000 <= desktop.pw_uid < 65534 and home.is_absolute() and str(home) != '/', 'Select an ordinary desktop account and actual home')
    home_fd = directory(home, owner=desktop.pw_uid); os.close(home_fd)
    local = home / f'.local/lib/initiativetracker-{role}'
    before_guards = guards(desktop, role, local)
    target_fd = directory(local, owner=desktop.pw_uid)
    admin = os.geteuid()
    try:
        fcntl.flock(target_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        old_data, old = read_file(target_fd, 'colophon.png', missing=True)
        identity = dict(user=desktop.pw_name, uid=desktop.pw_uid, gid=desktop.pw_gid, home=str(home), role=role)
        if action == 'rollback':
            require(Path(backup).parent == Path(backup_base), 'Choose the exact recorded backup under the fixed backup base')
            backup_fd = directory(backup, owner=admin, private=True)
            try:
                raw, receipt_meta = read_file(backup_fd, 'receipt.json')
                require(receipt_meta['uid'] == admin and receipt_meta['mode'] == 0o600, 'Unsafe receipt')
                receipt = json.loads(raw)
                require(receipt['pin'] == PIN and receipt['identity'] == identity and receipt['guards'] == before_guards, 'Wrong device/account/role or changed components; STOP')
                original = receipt['old']
                if same(old, original): print('Already at the original artwork state; no changes'); return
                require(same(old, receipt['installed']), 'Artwork changed since apply; STOP, preserve later edits')
                if original is None:
                    os.unlink('colophon.png', dir_fd=target_fd); os.fsync(target_fd)
                else:
                    data, meta = read_file(backup_fd, 'previous.png')
                    require(meta['uid'] == admin and meta['mode'] == 0o600 and len(data) == original['length'] and hashlib.sha256(data).hexdigest() == original['sha256'], 'Backup payload corrupt/unsafe')
                    atomic_file(target_fd, 'colophon.png', data, original)
                _, after = read_file(target_fd, 'colophon.png', missing=True)
                require(same(after, original), 'Restore verification failed; retain backup and inspect')
            finally: os.close(backup_fd)
        else:
            source = Path(source)
            source_fd = directory(source.parent)
            try: data, meta = read_file(source_fd, source.name)
            finally: os.close(source_fd)
            require(meta['sha256'] == PNG and len(data) == 73110, 'Approved original PNG hash/length mismatch')
            print('Source revision:', PIN); print('Target:', local / 'colophon.png')
            print('Current asset:', 'absent' if old is None else {k:v for k,v in old.items() if k != 'atime'})
            print('Install owner UID/GID/mode:', desktop.pw_uid, desktop.pw_gid, '0644')
            if action == 'inspect': print('INSPECT ONLY; no backup or asset write'); return
            base_fd = directory(backup_base, create=True, owner=admin, private=True)
            name = time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()) + '-' + uuid.uuid4().hex
            try:
                os.mkdir(name, 0o700, dir_fd=base_fd); os.fsync(base_fd)
                backup_fd = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=base_fd)
            finally: os.close(base_fd)
            backup_path = Path(backup_base) / name
            print('RETAIN THIS BACKUP:', backup_path, flush=True)
            try:
                stamp = time.time_ns()
                installed = dict(uid=desktop.pw_uid, gid=desktop.pw_gid, mode=0o644, atime=stamp, mtime=stamp, length=len(data), sha256=PNG)
                private = dict(uid=admin, gid=os.getegid(), mode=0o600, atime=stamp, mtime=stamp)
                if old is not None: atomic_file(backup_fd, 'previous.png', old_data, private)
                receipt = dict(pin=PIN, identity=identity, old=old, installed=installed, guards=before_guards)
                atomic_file(backup_fd, 'receipt.json', (json.dumps(receipt, sort_keys=True)+'\n').encode(), private)
                require(guards(desktop, role, local) == before_guards, 'Components changed during preparation; STOP')
                _, current = read_file(target_fd, 'colophon.png', missing=True)
                require(same(current, old), 'Artwork changed during preparation; STOP')
                atomic_file(target_fd, 'colophon.png', data, installed)
                _, after = read_file(target_fd, 'colophon.png')
                require(same(after, installed), 'Install verification failed; retain backup and inspect')
            finally: os.close(backup_fd)
        require(guards(desktop, role, local) == before_guards, 'Components changed; STOP and retain backup')
        print('Artwork verified; startup/config/profiles untouched. No app start, restart or reboot.')
    finally: os.close(target_fd)

def run(action, desktop, role, source, backup, backup_base):
    task_umask = os.umask(0o077)  # This child process only; never change home modes.
    try: return _run(action, desktop, role, source, backup, backup_base)
    finally: os.umask(task_umask)

if __name__ == '__main__':
    require(os.geteuid() == 0, 'Run this reviewed command through sudo')
    require(len(sys.argv) == 6, 'action desktop_user role source_png backup_or_dash')
    action, name, role, source, backup = sys.argv[1:]
    run(action, pwd.getpwnam(name), role, source, backup, '/var/backups/initiativetracker-branding')
PY
}
```
<!-- branding-procedure:end -->

### 4. Inspect, then separately approve apply

**Pi SSH Bash — inspect only** (no backup/asset write; retain the target/source/owner receipt privately):

```bash
branding_artwork inspect "$desktop_user" "$role" "$source_png" '-'
```

Expected: exact selected passwd home + `/.local/lib/initiativetracker-<role>/colophon.png`, verified pinned source, current asset hash or `absent`, and selected desktop numeric UID/GID / 0644. A compatibility or metadata refusal is a stop. Do not run the launcher/checker to test compatibility: it may invoke a pending app update/migration.

**Pi SSH Bash — only after device-specific artwork replacement approval**:

```bash
branding_artwork apply "$desktop_user" "$role" "$source_png" '-'
```

Record the exact printed `RETAIN THIS BACKUP` path. Each new operation creates `/var/backups/initiativetracker-branding/<UTC>-<random>/` root:root 0700, with root-private 0600 `receipt.json` and, if the asset existed, `previous.png`. The receipt pins revision, role, username/home/UID/GID, protected component/config hashes and original/installed asset metadata. The installed PNG is selected desktop UID:primary-GID, **0644**, with the exact original hash. Existing target-directory ownership/mode is not altered. There is no app start, migration, reload, service/desktop restart, profile change, pairing change or reboot.

If a command fails, retain its printed backup and all files, read the exact error, and stop. An interruption can leave the old or newly installed PNG; do not blindly rerun apply. The matching rollback checks both states, refuses unrelated later edits and can restore a prepared operation if its exact installed state exists. Backup payload corruption or component/config changes stop restoration for separate review; no success is inferred after a failed command.

### 5. Pi SSH Bash: exact artwork rollback after separate approval

Use the exact path printed for **this account, role and operation**; never pick “latest” across devices. Reload the same reviewed function if the SSH session ended. A later component or artwork change causes refusal rather than overwriting it.

```bash
backup='/var/backups/initiativetracker-branding/EXACT_PRINTED_UTC_RANDOM_DIRECTORY'
branding_artwork rollback "$desktop_user" "$role" '-' "$backup"
```

Expected: old PNG bytes, UID/GID/mode/atime/mtime restored, or PNG removed if originally absent. A repeated rollback is a no-op after verification. Original metadata with ACL/xattrs/hardlinks/special modes is deliberately unsupported and must be reviewed before any apply. Keep the private backup until the maintenance acceptance period ends; it contains no copied browser profiles, application DB, credentials or private config contents. It is **not an application-data rollback**.

## Customer application-logo replacement without source edits

Once an approved 0.1.6 deployment, persistent data, migration and trusted HTTPS are commissioned, obtain fresh secure DM authorization on the deployment-approved canonical application URL. Its external port/hostname and TLS termination belong to the separate HTTPS deployment guide; do not assume an internal listener port is the customer URL. Open **DM → Branding & banners** (`/appearance/application/00000000-0000-0000-0000-000000000000`), set the application name/logo visibility or upload a replacement **PNG/JPEG** logo, using **Save branding** for name/visibility and **Upload logo** for the image. Image validation, orientation, resizing and metadata stripping apply; arbitrary paths, HTML, SVG and external URLs are not accepted. The application logo is intentionally public. Restore default logo returns to the bundled Colophon artwork.

Settings/reference live in `PresentationSettings`/`PresentationAssets`; normalized generated PNGs live in the persistent `Tracker:DataDirectory/Media/` (normally `/var/lib/initiativetracker/Media`), outside versioned releases. Preserve coherent DB/media/keys/identity backups using the single release plan. Do not write a logo file directly into this private store or replace the release's static defaults. Application-logo changes do not update desktop-local PNGs, loading text or OS splash. A customer's desktop/OS brand bundle needs its own reviewed asset/hash receipt; this bounded recipe deliberately accepts only the supplied original Colophon PNG. Character silhouette and neutral banner defaults remain unchanged.

## Visual acceptance and true boot splash

After separately approved future desktop startup/reboot testing, observe: hub loading icon/text closes before the graphical updater and reappears only for the second health wait; Chromium then opens the same `/setup` profile. Satellite waiting page shows Colophon while unavailable, then closes before the existing paired display opens; verify the same display identity, encounter/HP/turn updates and profiles. Check fit/legibility/touch on the actual screen and repeat offline/reconnect recovery. Zenity icon rendering is desktop-dependent; a PNG hash does not prove physical appearance. Missing artwork may leave text/default icon; unsupported components must be upgraded separately, never “fixed” by clearing profiles or changing origins/trust.

An artwork-only copy may not become visible until an already-running waiting page/dialog is recreated or a later approved desktop session starts. This guide includes **no restart or reboot command** and no permission to trigger the updater or migration as a visual test. No physical visual acceptance is claimed.

This is **application/desktop loading artwork after Linux and the graphical session start**. It does not replace Raspberry Pi firmware branding, kernel/console output, Plymouth, initramfs or boot configuration. [True Linux boot splash research](../N9-optional-boot-splash.md) remains optional and separately approval-gated.

## Documentation-command validation

The exact embedded command is exercised by
`scripts/tests/test_n9_branding_docs.py` in disposable fake homes/backups only:
21 cases run as an ordinary UID and again as root against fake homes only, covering
both roles, direct/supervised hubs, other usernames and spaces,
existing/absent art, metadata restoration, later edits, interrupted replacement,
symlink/hardlink/ACL/xattr refusals, pinned archive extraction and local links.
The documentation CI additionally parses/executes the exact PowerShell preparation
on Windows with **fetch/SCP mocked offline**, retaining real Git archive, tar and
binary PNG hash checks. This is command-logic evidence, not Pi execution or visual
acceptance. No installed device path is used by these tests.
