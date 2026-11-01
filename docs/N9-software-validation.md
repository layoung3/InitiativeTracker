# N9 software validation and acceptance boundaries

For 0.1.6, use the [actual shared HTTPS integration findings](N9-https-integration-readiness.md) and [single consolidated release plan](https/0.1.6-integration-release-plan.md). Merged master `1459dd6f` and the prepared policy/commit adapters are now integrated into the existing N9 branch. The original standalone receipts below are historical feature evidence. Current combined-head tests, trusted-browser/live-display checks and exact CI receipts belong to PR #5. No deployment or PR #5 merge is authorized.

N9 is delivered through **draft PR #5**, branch `n9-branding-customization`, based directly on verified remote master `d584b7963ca6bb7916f21d9ee94082f9f2821f34`. N8 PR #4 remains separate, draft/unmerged at `93e93d37ffa5dfdc809434751f6b641087d7913c`. No N8 merge/cherry-pick, Pi access, live migration, trust installation, reboot, merge or deployment occurred. Parent independent review and final exact-head CI evidence belong in the PR description before handoff; neither is replaced by the earlier checkpoint's passing runs.

## Implemented behavior

- The unchanged owner-supplied Colophon PNG appears on Setup, pairing/encounter waiting and optional application entry branding; fresh desktop recipes copy it into local hub/satellite startup assets. [Provenance and checksum](N9-assets.md).
- A separate neutral fantasy-adventurer silhouette and mountain banner supply missing/default media. The phoenix is never a default character portrait.
- Players manage their own private profile portrait and character portrait/banner. DM manages any profile/character plus one installation-wide campaign banner and public application logo/name. This is explicitly **one installation**, not multi-campaign management.
- Real PNG/JPEG decoding, full raster validation, orientation, resizing and fresh PNG encoding; EXIF/GPS/source metadata discarded. Private generated storage survives versioned app updates. Quotas, cleanup and failure-preserving replacement are enforced. [Security review](N9-asset-security-review.md).
- Personal/campaign reads recheck current authentication and the existing public encounter projection. Private images never enter public static storage; application logo is intentionally public. SignalR refresh contains no character/profile identifiers, and updates images without reloading character forms. Display turn updates reuse portrait elements.
- Production personal-image reads and all image/settings changes require the enabled shared trusted HTTPS origin and fresh matching SecureFeature authorization. Development fixtures may use loopback HTTP. Production writes require explicit persistent `Tracker:DataDirectory` outside the application directory. N9 does not install certificates or weaken TLS/identity checks.
- Hub startup retains health → graphical checker/updater → health → persistent Chromium. Waiting dialogs close before checker/browser and on signals/failure. Satellite waiting uses a separate profile and is reaped before the paired display opens. Existing profiles, Labwc integration and network controllers are preserved.

## Automated evidence

The .NET Release suite includes original combat/network/privacy tests plus native PNG/JPEG, eight EXIF orientations, metadata stripping, CRC/truncation/animated/unsupported input rejection, decompression budgets, CSRF/ownership/stale/revoked access, hidden/off-encounter reads and old URLs, replacement faults, quota/persistence/orphan cleanup, symlink rejection and production HTTPS policy. TestServer transport tests do not prove actual TLS certificate trust.

Browser suites cover existing combat scrolling/visibility/exhaustion behavior, responsive branding/default media, slow pairing-response serialization and actual Kestrel pages. The live flow creates a synthetic player/character, obtains DM display approval, uploads and replaces portraits, verifies changed pixels on the connected display without losing pairing, and changes banners. Actual Setup/Join/Pair, editor, character sheet, DM and display layouts are checked at 390/844/1280 pixels. Screenshots from disposable fixtures are inspected internally, not published as personal-data artifacts.

Installer/startup discovery includes **55 tests**: 35 run on the ordinary host and 20 privileged/root/sudo tests require a disposable container. Persistent dialog processes verify teardown ordering and signal cleanup; supervisor tests cover paired-profile handoff and shutdown while waiting. Container updater tests back up and restore private `Media/` data together with database/identity/key material. Existing shell checks remain required.

N9 CI runs full Release tests, direct/transitive NuGet audit, pinned Playwright audit/browser suites, existing ARM64 release Build/Test scripts, and a separate **`ubuntu-24.04-arm` native execution job**. That job verifies AArch64, publishes native assets/notices, and runs `--media-self-test` on the published executable: real PNG/JPEG decode, resize and PNG re-encode without server startup or database/network access. A passing ARM64 job is Linux ARM64 software evidence, not proof of Pi OS/desktop compatibility.

Final local validation passed 112 .NET Release tests, 195 network tests and 24 Chromium tests (14 fixtures plus 10 actual-page/integration tests). Use the final PR's exact-head CI links to corroborate these results and native ARM64 execution. Existing nullable/stackalloc warnings outside N9 remain. Local Docker APT DNS failed, so privileged container evidence comes from GitHub CI; no local privileged-container pass is claimed.

## Remaining acceptance limits

Real Pi/monitor/touch, Zenity artwork rendering, Labwc/Chromium handoff, cold boot and offline/network recovery remain physical/full-desktop integration checks. No spare cards or Pis are currently owned; no purchase is required now. A later approved trial can use the existing two Pis with two disposable microSD cards, reader/private backup storage and full powered-off restoration of original cards. No additional Pis are required.

Trusted HTTPS issuance/configuration and desktop-component installation are separate deployment dependencies. App releases do not replace home-local startup scripts/assets or alter firmware/Plymouth. Optional true OS splash remains an [approval-gated investigation](N9-optional-boot-splash.md). See the [release/rollback guide](N9-release-rollback.md) for later approved trials.
