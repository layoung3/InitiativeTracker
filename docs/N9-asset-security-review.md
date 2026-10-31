# N9 asset handling and security review

Scope: supplied public product artwork, uploaded profile/character portraits and banners, installation-wide campaign/branding. This records software review and automated evidence; parent independent review is required before final handoff. It does not authorize merge/deployment or claim exhaustive correctness.

## Data flow and limits

Razor authorization and antiforgery protect forms. One-permit, zero-queue admission covers editor requests before multipart model binding; the store independently admits one upload/decode/mutation and returns busy rather than creating an unbounded decode queue. Character/profile ownership and session/credential validity are checked before reading/decoding and again before persistence. Original filenames, MIME claims, URLs and paths never select storage or codecs.

| Resource | Bound |
| --- | --- |
| Request / file | 6 MiB request, multipart ceiling 5 MiB plus 64 KiB; actual file bytes limited to **5 MiB** while reading |
| Input raster | PNG/JPEG only, **4096 per side**, **12,000,000 pixels** |
| Decode | One admitted pipeline; dimensions checked before full pixel allocation; complete decoder success required |
| Output | Fresh PNG; portrait/logo fit within **512×512**, banner **1600×400**, no upscaling; at most **3 MiB** |
| Persistent media | **128 MiB / 512 assets** per installation; **20 MiB / 64 assets** per profile; replacement credits actual old file bytes for physical quota |

PNG chunk bounds/CRC are checked, APNG controls rejected, JPEG framing/decoder format checked; GIF/SVG/WebP/HTML are not accepted. Skia decodes into a fresh sRGB bitmap, applies all eight EXIF orientations, resizes and re-encodes. Source EXIF/GPS/XMP/text/ICC payloads and originals are not persisted. Generated encoder color information is not copied source metadata. Framework request buffering can temporarily spool originals; multipart/admission limits apply and these buffers are not retained application assets.

Approved dependencies: SkiaSharp and matching Linux native assets **4.154.0**. [Dependency/license/native audit](N9-dependency-proposal.md) records versions, notices and advisory scope. Native decoder vulnerabilities remain a residual risk: this is an in-process decoder with bounded input/pixels/concurrency, not OS sandboxing or a guaranteed CPU deadline. Keep it patched.

## Authorization and privacy

DM manages active characters/profiles and installation branding. Players manage only their own profile and characters; displays cannot mutate. Profile portraits remain private to owner/DM even when a character is publicly visible.

Other-player/display GET reuses `GetDisplayState()` membership, including existing active/hidden and assigned-player rules. A hidden character with an assigned player remains visible exactly as baseline specifies; otherwise hidden/off-encounter/deleted character images return 404. HP visibility is unchanged. Session changes, credential revocation and display trust are rechecked on reads and across upload awaits. Old/query-version URLs do not bypass checks. Unauthorized and nonexistent subject IDs both return 404 after authentication; this is status uniformity, not a constant-time guarantee.

Personal/campaign reads use PNG, `nosniff`, `private, no-store`, without original filenames or filesystem paths. Missing authorized assets fall back to neutral defaults. Revocation cannot erase pixels already viewed or captured. Application logo is intentionally public on entry pages and `/branding/logo`; UI labels this before upload. The campaign banner is shared with joined players/paired displays, not anonymous visitors. No arbitrary URL fetching or HTML/CSS branding setting is accepted. Names are bounded/encoded; client refresh uses `textContent` and fixed local routes.

Production private reads and appearance mutations reject HTTP (426); Development is an explicit software-fixture exception. N9 does not include unmerged N8 agent/certificate infrastructure. Future commissioning must supply trusted HTTPS and protect the existing authentication origin. No trust bypass, warning suppression or HTTP fallback is added.

## Persistence and recovery

Production writes require configured DataDirectory outside the application directory. Assets live in its `Media/` directory (0700 on creation), as server-generated GUID filenames with 0600 files and no executable bits. Nothing uploaded enters `wwwroot`, a versioned release, shell command or template. Symlink media directories are refused; symlink reads fall back instead of following arbitrary files. Host compromise or a local actor able to replace data directories is outside the remote-upload boundary.

A normalized candidate is privately written/flushed and atomically renamed before committing its SQLite reference. Save failures remove candidates and preserve the old reference/file. Successful replacement deletes old files after commit. Interrupted candidates/old files are pruned on startup/hourly maintenance and before mutations using recognized generated filenames; deleted subjects lose their rows/files. Unknown administrator files are preserved and count toward quota. No cross-filesystem power-loss atomicity claim is made: whole-data backups must keep database, media and keys together while the app is stopped.

SignalR broadcasts `MediaUpdated` without IDs, filenames or image bytes. Clients refresh existing authorized URLs; turn updates reuse portrait elements. Existing combat state, pairing and HP projection remain separate.

## Test matrix

Native tests cover PNG/JPEG, eight orientations and structured EXIF/GPS stripping, CRC/corrupt/truncated/animated/unsupported inputs, dimension/pixel/byte budgets and malformed corpus. HTTP tests cover CSRF, cross-player changes/reads, private profile access, DM scope, stale/revoked identity, visibility/HP rules, hide/remove/deletion with old URLs, missing files/defaults, symlinks, quotas, recreation/orphans and injected SQLite commit failure. A gated stream proves bounded admission and revoked authority during an await cannot commit. Browser tests verify actual paired-display pixel replacement and responsive rendering. Privileged updater fixtures restore private media with whole-data rollback. ARM64 execution and final exact-head results must be verified in the final PR evidence.
