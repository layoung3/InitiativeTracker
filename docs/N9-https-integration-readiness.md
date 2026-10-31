# N9 integration for 0.1.6

PR #6 was owner-merged into master at `1459dd6fe4855d41da92e261df6c3456b71d0e9b`, containing reviewed HTTPS source `d84d9a1c26e925f1cc9f6a11cf389123cdf14449`. The existing N9 PR #5 now merges that master normally and applies its prepared authorization and commit-boundary adapters to actual production source. Final exact-head CI/package receipts are recorded in PR #5. Historical patches under `integration/` and the frozen composite runner are retained as review provenance, not as the current runtime or a substitute for integrated-head tests.

PR #5 remains draft/unmerged; no merge, tag, publication or deployment is authorized by this integration. N8 PR #4 stays separate at `93e93d37ffa5dfdc809434751f6b641087d7913c`. No live migration, certificate/DNS change, installed trust change or Pi access occurs here.

## Applied security boundaries

Private media and appearance settings in Production require the enabled shared trusted origin and fresh SecureFeature grant matching the ordinary identity. Missing/disabled shared policy fails closed, including another HTTPS listener with replayed legacy cookies. HTTPS transport, player recovery and old display pairing do not confer private-feature authority. Secure display tickets remain read-only. UI explains fresh secure approval. Real Chromium form submission exposed the earlier `no-referrer` policy producing `Origin: null`; protected pages now use `Referrer-Policy: same-origin`, suppressing cross-origin referrers while preserving strict Origin/CSRF validation. Null/missing/wrong origins remain denied.

The shared validated request snapshot is revalidated during preparation and at final commit. Replacement, removal and branding use `SecureAccessService.CommitAuthorized`, sharing revoke/cancel's gate. Either the final transaction commits before revocation completes, or completed revocation prevents callback admission. Expiry is checked immediately before admission: an already admitted bounded transaction may finish after expiry without extending its grant.

Decode, quota/file scans, candidate write/flush/rename, cleanup and notifications remain outside the shared gate. No write transaction waits for admission. Inside the callback a fresh SQLite transaction checks current active character/profile ownership and the expected image reference, then saves and commits. EF command and SQLite default/busy timeouts are one second. The 250 ms shared limit bounds acquisition only, not absolute slow-storage/fsync latency. Serialization supports **one application writer process**, not distributed/multiple app writers.

Rejected mutations do not prune prior or unrelated referenced assets. Old files are deleted only after confirmed commit. Cleanup checks candidate references in a fresh context; an ambiguous post-commit error preserves referenced candidates and previous files and returns unavailable. Independent maintenance can reclaim later orphans.

Owner/DM authorization, antiforgery, provenance, current encounter visibility/HP projection, bounded PNG/JPEG processing, approved SkiaSharp 4.154.0, original Colophon artwork, neutral portrait/banner and persistent private storage remain intact. Reassigned-character art with mismatched original ownership remains quarantined/404, including for DM, until a separately reviewed resolution policy exists.

## Resolved integration conflicts

| Path | Resolution |
| --- | --- |
| `.github/workflows/installer-tests.yml` | Exact PR head, `persist-credentials: false`, union of installer/network/HTTPS/N9 path filters and all shell/container checks. |
| `hub-updater/runtime-manifest.json` | N9 launcher hash plus merged shared rollback-safe installer hash and component-set receipt; every current component hash verified. |
| `scripts/installation/tests/test_updater_container.py` | Both private Media bytes/modes/restore checks and secure-grant fencing-before-restart/unsafe-path checks retained. |

Program keeps native self-test exit before database startup, BrowserTls configuration before build, persistent Data Protection, media limiter/storage services, SecureAccess after legacy authentication configuration, transport guard before routing and secure guard after authentication/before authorization. Setup retains branding and the non-secret trusted physical-screen link. Existing graphical updater, selected desktop home, Labwc/profile and satellite startup behavior remain preserved.

## Actual combined-source validation

CI now tests the current checkout directly. `InitiativeTracker.Tests` includes native/media/privacy tests, shared TLS/grant/durability tests, integration routes and deterministic commit boundaries. The 29 boundary cases cover actual mutation and secure revoke/cancel POSTs, both operation orderings, expiry before/after admission, reassignment/current-reference races, real SQLite contention, rollback failure and ambiguous post-commit responses.

The migration rehearsal builds a synthetic stopped 0.1.5-schema checkpoint, lets actual first startup apply `20261031050702_AddPresentationAssets`, verifies identities/HP/pairing/keys, persists media/settings across a second startup, and restores the coherent old checkpoint to empty disposable storage without `Down`. Actual updater-helper regressions independently verify restored secure grants are fenced before restart.

Browser checks include ordinary Development layout/gameplay plus the actual Production HTTPS branch. Chromium uses normal certificate verification with a newly generated root in a disposable child-process home/NSS database; an otherwise identical browser without that root must reject it. Existing user/OS trust, public DNS and devices are untouched. There are no ignore-certificate-errors flags, request interception, mocked SignalR or browser-warning bypasses. The flow uses physical DM proof, explicit player/display browser approvals, real PNG uploads, private reads, malformed-image rejection, live paired-display portrait/banner refresh, revocation and responsive pages. This establishes isolated synthetic-trust software behavior, not public production/browser/phone trust.

The current self-contained Linux ARM64 publish executes the actual native image decode/resize/encode self-test on an ARM64 runner and retains required third-party notices. Release archive/manifest validation is performed against this integrated source. No approved release artifact, tag or deployment follows automatically from CI.

## Deployment inputs and remaining gates

Use the [single consolidated 0.1.6 deployment/rollback plan](https/0.1.6-integration-release-plan.md). It now records applied integration and preserves separate owner approvals for public hostname/DNS/issuer decisions, certificate provisioning/promotion, guarded updater components, app deployment **and automatic first-start DB migration**, persistent data, and home-local Colophon startup assets.

`Tracker:DataDirectory` must be absolute and outside versioned releases. Match stopped DB/WAL/SHM, private `Media/`, keys and identities as one backup/restore checkpoint. Never casually run migration `Down` (it drops presentation tables); restored secure grants must be fenced/discarded through reviewed recovery before startup. Current valid browser certificates/issuer state have separate rollback boundaries.

N9's source version is 0.1.6. After independent review and owner-approved PR #5 merge, build/validate a fresh immutable package from the approved SHA and obtain separate tag/publication/transfer approval. Corrected updater and selected-user startup assets are separately installed components; an app archive does not install them. True Pi/Linux boot splash remains optional and separately gated.

Remaining infrastructure/physical acceptance: owner-selected domain/per-hub hostname, DNS provider/delegation/scoped credentials/backend and CA terms, actual local DNS/public browser/phone trust and clocks, real Pi desktop/cold boot/touch/offline/storage behavior. Optional later trials use the existing two Pis with disposable cards/reader/private backup storage and full powered-off restoration of original cards. No additional Pis are required. HTTP-only 0.1.5 gameplay/default/public branding remains available; private N9 features require trusted configured HTTPS and fresh approvals. Never enable Development on a Pi or bypass certificate checks as a workaround.
