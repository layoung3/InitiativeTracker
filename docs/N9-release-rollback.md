# N9 release and rollback guide

Status: development checkpoint, **not approved for release, merge or installation**. N8 PR #4 remains separate, draft and unmerged at `93e93d37ffa5dfdc809434751f6b641087d7913c`. N9 starts from master `d584b7963ca6bb7916f21d9ee94082f9f2821f34`; it does not include N8 TLS/network-agent changes.

## Presentation configuration

`Branding:ApplicationName` accepts a trimmed name of at most 80 characters, rejects control characters and falls back to `Initiative Tracker`. Razor encodes the name as ordinary text. `Branding:ShowApplicationLogo` controls optional branding on application entry pages; Setup and waiting screens retain product branding. No setting accepts HTML, CSS, a filesystem path or a remote image URL. The Colophon artwork is independent of the default neutral adventurer portrait and mountain banner.

The original supplied raster must be retained unchanged at `InitiativeTracker.Web/wwwroot/images/colophon.png`. At this development checkpoint transfer of its bytes is blocked; missing artwork results in text branding. Do not claim the supplied artwork is integrated until its actual bytes have been inspected and verified. No substitute phoenix is allowed.

## Before a release can be approved

- Complete approved image-decoder dependency integration, secure portrait/banner upload/storage/authorization, media cleanup and associated test matrix. These features are pending at this checkpoint; the default-image UI alone is not N9 completion.
- Inspect real artwork and actual rendered pages at phone, tablet/landscape and desktop/display sizes. Record software checks and physical observations separately.
- Run .NET Release tests, Chromium suites, network/installer/updater tests, shell checks, self-contained ARM64 publish and existing Build/Test release-manifest tools on the final source head. Confirm any native decoder and licenses are included. Record exact-head GitHub CI, not an earlier success.
- Review runtime changes separately from the application archive. App releases do not silently replace the desktop launcher/supervisor or alter OS boot configuration. Confirm current updater health, backup and rollback behavior in disposable fixtures.
- Choose a new release version; do not overwrite 0.1.5 archives or existing immutable release directories. No release tag/publishing or deployment is authorized by this guide.

## Later approved installation

No step here is to run on the owner's current cards now. No spare cards are presently available. Later validation can use the existing two Pis with separate disposable microSD cards, a reader and private image/backup storage; two additional Pis are unnecessary. Shut down, remove power fully, label/store original cards and verify boot-media isolation before using disposable installations. Restore original cards and boot-media arrangement only while fully powered off; then verify unchanged 0.1.5 health, pairing and encounters. Do not alter EEPROM or attached production storage.

For a later approved application update, use the existing versioned release archive/manifest and graphical updater. Keep `Tracker:DataDirectory`, data-protection keys, player/device identity and browser profiles intact. Back up the **entire data directory**, not only SQLite. New uploaded media must live under that directory and outside `wwwroot`, so database and media restore together. A live database/file copy is not a verified backup.

For a later separately approved desktop-component update, preserve the chosen user's actual passwd home. The launcher is under `~/.local/lib/initiativetracker-hub/initiativetracker-hub-launch`, not `/usr/local/bin`. Back up exact launcher/supervisor/waiting artwork and metadata before a reviewed replacement. The existing component utility recognizes exact hashes; if it refuses an installed version, stop and review rather than widening hash acceptance or overwriting it. New N9 candidate hashes do not themselves authorize a component update.

The fresh installer copies the waiting page and available Colophon image to the selected role's home-local directory. Existing installations require a separately reviewed copy/restore plan for these files; an app archive does not install them. Preserve current Labwc autostart, LightDM, display mode, Chromium profile paths and graphical checker. Hub ordering remains health → checker/update → health → Chromium. A startup dialog is closed before the checker and before Chromium, including failure and signal exits. The satellite's local waiting browser uses an independent profile and is terminated before the persistent paired display is opened. No certificate bypass or HTTP fallback is added.

## Rollback

Before an approved application update, preserve the old release link and private whole-data backup with matching version/manifest evidence. Let the graphical updater perform its existing health-gated rollback; check its result rather than assuming every failure proves recovery. If automatic recovery fails, stop and inspect locally before moving data or invoking another update. Restore database and media from the same backup, retain failed state for diagnosis, and verify session/identity/pairing/encounters after recovery.

Desktop components/artwork have a separate backup and rollback; restoring an app link alone cannot restore them. Restore only the exact backed-up files and permissions after checking for later user changes. Restart the desktop only with explicit approval. OS splash experiments have a separate [optional investigation and recovery plan](N9-optional-boot-splash.md); card swaps cannot undo firmware changes.
