# Staged migration and rollback from HTTP-only 0.1.5

All production stages below need separate explicit approval. This PR is a draft software checkpoint, not deployment authority. Keep N8 PR #4 and N9 PR #5 unchanged and unmerged. Do not run app startup against production data as a preflight: master itself invokes EF migrations on startup.

## Stage 0: owner choices and review

Owner supplies domain/hostname, registrar/DNS control or challenge delegation, narrow API credentials, maintained backend/plugin choice, ACME account/contact/terms and desired resolver/network mode. Review package licenses/dependencies, root service sandbox, credential rotation, certificate expiry monitoring and physical clock recovery. No purchasing, accounts, DNS mutations or real issuance happened in this checkpoint. No public inbound service is required.

Capture current healthy 0.1.5 app/release identity, active origin/port, pairing, hub/device identities, trusted-agent state if later present, current session, graphical updater, selected desktop user, Labwc config, Chromium profile and kiosk `hub_url`. Make and validate encrypted backups of database, DataProtection keys, media when introduced, config and identities together. A database-only rollback can invalidate credentials or leave media references inconsistent. Do not reset/reseed anything to test HTTPS.

## Stage 1: networking/resolution approval

Configure supported local resolution with the network owner. Keep `.local`, IP HTTP and existing satellite `hub_url` unchanged. Validate chosen hostname resolves to the hub on external Wi-Fi and the private hotspot, IPv4/IPv6/multihoming, no upstream leakage, no open DNS resolver, no WAN exposure, phone/Chromium trust preconditions, client isolation, DoH/private DNS limitations and hotspot recovery. Test internet-disconnected resolution and address changes. Record unsupported clients/networks honestly. Rollback only the newly approved resolver entries; retain legacy networking.

## Stage 2: certificate backend/install approval

Approve backend/plugin install and restricted paths/issuer service separately. Stage DNS-01 with disposable CA staging issuance, then approve production issuance/account terms. Public staging certificates must fail activation. Generate each hub's private leaf key on the hub/issuer, with no fleet-shared key. Install only validated material into the persistent browser store; test private file permissions, expiry warning, DNS/API failure, renewal retry and safe export. No app DB migration belongs to this stage. Initial activation requires an enabled compatible app listener, so issuance/export validation can precede final promotion while the legacy app remains untouched.

## Stage 3: app deployment approval (no new schema in this PR)

Review the exact release hash and rollback artifact, then approve deployment/startup/migration checks independently. Set `BrowserTls:Enabled`, Host, Store and port 5445 via a reviewed drop-in; keep `Tracker:UseHttpsRedirection=false`. Keep existing HTTP Kestrel endpoint, `Tracker:PublicBaseUrl` and advertised LAN settings unchanged. This avoids silently changing QR URLs, satellite discovery/health targets or configured kiosk origins. The new public hostname is HTTPS-only and rejects HTTP, including login forms; the distinct legacy host remains available. No global redirects or new HSTS are enabled by this foundation during transition; old browser HSTS state, if present from other setups, must be assessed separately.

Promote with the approved issuer command; verify **served** chain/SAN/dates/generation at the new hostname and a normal browser with no warnings. Existing DM/player/satellite sessions at their old host remain as before. Also verify same-origin HTTPS antiforgery, websocket reconnect, pairing, authorization, app restart, graphical updater and keyboardless startup in physical validation. This checkpoint leaves bootstrap/updater/network scripts untouched.

Host-only cookies at `.local` or an IP cannot transfer automatically to a public hostname. New-origin DM/player/display clients must explicitly authenticate/join/pair according to existing rules, while old-origin sessions remain valid. Do not copy browser cookies, weaken Secure flags, alter session IDs or silently overwrite Chromium profiles. When N8 is later integrated, its distinct SecureDm authentication remains required for network administration; legacy cookies never gain that privilege.

## Stage 4: separately approved N8/N9 integration and data migration

Produce integration patches after their independent reviews: N8 gets the shared live provider instead of another certificate manager, retaining pinned agent TLS 5443 and the secure DM authority; N9 gets shared trusted-origin presentation checks while retaining media/ownership policies. Review the combined source and test real uploads/private reads/provisioning over HTTPS and their HTTP rejection. This PR's reserved route tests alone do not establish those features work together.

N9 introduces presentation tables and startup migrations. Approve a matching database migration and database/media/DataProtection backup/restore separately from app rollout. Test the combined release rollback against a restored copy before production. The combined security review must also test replay of cookies/device tokens obtained at the legacy HTTP origin. Host-only browser behavior does not prevent manual replay. Private features need secure-origin authentication/credential provenance so legacy-only credentials are not silently promoted; N8 SecureDm isolation must remain. Design the player/DM reauthentication or credential rotation stage without resetting existing legacy gameplay identities/sessions. This is an unresolved integration requirement, not an HTTP-to-HTTPS automatic session transfer. No such migration or branch merge is performed here.

## Stage 5: advertised URL and optional client cutover

Only after software and physical acceptance, separately approve changes to `Tracker:PublicBaseUrl`/LAN scheme/port, QR/discovery consumers and each satellite's configured `hub_url`. `/system/hub-info`, `TrackerUrlService`, Python satellite health checks and Chromium navigation have distinct assumptions; validate all of them in an integration patch before changing defaults. A single advertised URL change is not a complete migration. Preserve old-origin display recovery until client migration succeeds. No redirect/HSTS cutover is authorized by this document.

## Operational rollback

Certificate renewal failure alone needs no app rollback: keep the valid active certificate. For a suspect candidate, the activation tool restores the old manifest and independently verifies it is served. It reports unconfirmed recovery if the check fails. Root-owned archives preserve last-known-good material; for a manual rollback, first use a dry-run:

```sh
python3 foundation.py rollback --config /etc/initiativetracker-https/settings.json --generation OWNER_VERIFIED_GENERATION_ID
```

The tool checks the identifier, permissions, SAN, current public chain/dates/key, and at least 24 hours remaining. `--apply` is a separate approved action, creates a fresh generation from the archive and repeats TLS acknowledgement. Do not restore expired/revoked/untrusted material merely because it once worked. Do not manually loosen permissions or accept certificate warnings.

If TLS cannot recover, keep sensitive operations closed. Restore legacy gameplay/display access at its unchanged origin, use physical owner maintenance, repair clock/DNS/outbound issuance and then validate a trusted certificate. Do not send PINs, DNS credentials or private uploads over HTTP as a fallback. Existing 0.1.5 legacy login access is preserved at its old origin and retains its preexisting security limits; it is not authorization for new provisioning/media features.

For app rollback: disable only the optional BrowserTls drop-in and restore the verified 0.1.5 release using the existing updater/recovery process after approval. Retain all data/identity keys/profiles. This foundation adds no schema, but if a later combined N9 release migrated data, restore the matching database/media/keys snapshot as an independently approved stage. Disable the optional renewal timer before removing its backend; keep archived keys protected or securely retire them under an approved retention plan. Roll back only separately introduced DNS entries/network services. Never modify N8 agent pins as part of browser-certificate rollback.

## Physical acceptance still required

No spare Pi/media was assumed and neither Pi was accessed. Actual Raspberry Pi clock-at-cold-boot, systemd reload/sandbox behavior, DNS integration on its OS image, browser public trust on phones/Pi Chromium, venue networking, Wi-Fi/hotspot transitions, current sessions/pairing, Labwc/Chromium updater handoff and power-loss filesystem behavior require an approved hardware window. Do not infer those outcomes from cloud fixture tests.

## Approved secure authorization flow: additional deployment gates

The source now implements the owner-approved secure flow; it is not deployed by this checkpoint. Before activation, approve the hub-only loopback DNS override, verify the public certificate locally through the configured FQDN and validate the physical screen on the actual graphical desktop. Keep LAN/hotspot client answers private-addressed, never loopback. Then use `/secure-access` to request a browser-bound DM code and the trusted-loopback `/secure-access/physical` screen to read it. First use or lost/corrupt approval state requires the explicit recovery option, which replaces the secure epoch while preserving gameplay database identities and persistent Data Protection keys.

Players/displays use the HTTPS join/pairing flow only to identify a proposed existing identity; a fresh browser request must be approved by a securely authorized DM and completed in that same browser. Players require no physical hub access. Existing HTTP credentials are not upgraded automatically. Identity/security/visibility and character-configuration mutations now require secure authority, while ordinary gameplay continues. New pairing approval remains a sensitive DM action; existing trusted display state is retained. Test these transitions and actual private-media requests before any advertised URL or kiosk cutover.

No EF migration is added for secure grants. Nevertheless, starting the app on production still invokes the existing EF migration mechanism, and integrating N9 still needs its separately approved presentation migration. Secure state/keys live outside release directories. For an approved rollback/backup restore, **do not restore stale secure approval state**: invalidate it and perform fresh physical recovery. An old valid snapshot can contain revoked-but-unexpired grants; there is no external non-rollbackable revocation anchor. Failed state writes/revocations are unconfirmed, not evidence that a restart completed them. Corrupt/missing/interrupted state keeps secure features closed while legacy gameplay/recovery stays available.
