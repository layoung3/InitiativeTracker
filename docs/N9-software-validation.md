# N9 development checkpoint

N9 is **incomplete and not release-ready**. Branch `n9-branding-customization` starts directly from verified remote master `d584b7963ca6bb7916f21d9ee94082f9f2821f34`. PR #4 was read-only verified draft/unmerged with head `93e93d37ffa5dfdc809434751f6b641087d7913c`; no N8 commit was merged or cherry-picked. Neither Pi was accessed.

## Completed independent work

- Bounded/encoded configurable application name, optional entry-page branding, Setup and pairing brand component, branded empty encounter template.
- Neutral cloaked-adventurer portrait and quiet mountain banner, distinct from the product logo, on player list/sheet.
- Automatic unavailable/retry/expiry feedback on pairing. No pairing approval rule changed.
- Local hub startup feedback while preserving health → graphical checker/updater → health → persistent Chromium, including alternate home and failure exits.
- Satellite local inert waiting page with independent Chromium profile, bounded process cleanup and existing health/identity probe. No certificate bypass, HTTP fallback or network-controller changes.
- [Optional boot splash investigation](N9-optional-boot-splash.md), [release/rollback guide](N9-release-rollback.md), [decoder proposal](N9-dependency-proposal.md).

## Current local results

| Evidence | Result | Scope/limits |
| --- | --- | --- |
| .NET Release suite | **81 passed** | Actual Razor/SQLite/authentication tests, including encoded configured branding and optional entry branding. Existing nullable/stackalloc warnings remain. |
| Hub network suite | **195 passed** | Existing software fixtures; no radio access. |
| Installer/updater/startup host discovery | **50 discovered: 30 passed, 20 skipped** | Includes 9 new startup/process tests. Existing 20 real-sudo/root tests require disposable container. |
| Chromium suites | **12 passed** | Eight existing combat/UI tests plus three new responsive branding/default-media fixtures and automatic pairing-error recovery. These are synthetic fixtures, not a claim that uploads exist. |
| Actual Kestrel page checks | **9 viewport/page checks passed** | Setup, Join, Pair at 390×800, 844×800, 1280×800. Screenshots inspected internally from a disposable database. Current artwork is absent; these establish text-branding layout only. |
| Shell parse / git whitespace | Passed | `bash -n` launcher and `git diff --check`. Full ShellCheck remains CI. |

Docker build fetched the pinned base image but failed during APT because `deb.debian.org` could not resolve inside the build. No privileged-container pass is claimed locally. The existing installer workflow must provide that evidence on the exact final head. A new N9 workflow runs Release .NET, network and Chromium tests plus the existing ARM64 release Build/Test scripts. CI links/results must be added after the final approved feature head exists.

The SDK was installed under `/tmp` solely for development. Application fixtures use disposable `/tmp` data. No production database migration, service/desktop change, boot edit, trust installation, merge or deployment occurred.

## Explicit outstanding implementation blockers

1. **Artwork bytes unavailable.** Library ID `libfile_bb802a9ca6cc8191b96b8dbca3fc208c`, filename `colophon image.png`, reported PNG/73,110 bytes/300×300. Consumer-local materialization and the bounded explicit-destination retry both returned `library file transfer failed: download failed`. No image bytes have been inspected or committed. Do not claim Colophon artwork integration; current component intentionally falls back to text. Do not substitute another phoenix.
2. **Decoder dependency approval pending.** Proposed `SkiaSharp` + `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.153.1 (MIT). No references added. The task explicitly gates major dependencies. Approval request and exact tradeoffs are in the linked proposal.
3. **Secure upload/storage/read authorization not implemented yet.** Pending dependency approval, implement actual PNG/JPEG decode/re-encode, orientation/metadata handling, byte/pixel/concurrency limits, generated safe storage under DataDirectory, quota/cleanup/atomic replacement, CSRF and ownership checks. No client-only/MIME-only substitute is acceptable.
4. **Custom profile/character portraits and character/campaign banners still pending.** The repository has no campaign collection; use an explicitly documented one-installation campaign presentation setting rather than silently claiming multi-campaign support. Default media rendering is only the independent first step.
5. **Media security and realtime regression matrix still pending.** Owner/DM private access; nonowners/Display only current visible encounter assets using the exact existing public projection. Recheck reads after hide/remove/revoke/session reset, avoid profile enumeration, and test malformed/oversized/animated images, failure-preserving replacements, storage/restart, and live refresh.

## Physical/infrastructure limits

All actual Wi-Fi/boot/desktop/touch behavior remains unverified on hardware. Native Zenity image rendering and Labwc handoff require a suitable disposable full-desktop test or later approved cards. No extra Pis are required; the owner currently has no spare cards. A later two-card trial uses full powered-off restoration of the original cards, as documented. OS splash remains optional and separately approval-gated.
