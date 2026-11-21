# Physical hub HTTPS kiosk and one-tap approval — draft acceptance plan

**Development branch:** `feature/hub-touchscreen-recovery` · PR #15. This is source-only work, not an instruction to change a running Pi. Keep production 0.1.6 and its working HTTPS certificate, application data and updater intact until separately authorized deployment.

## Why HTTPS is mandatory

The physical approval overlay and its decision endpoints are restricted to trusted browser HTTPS at the configured public hostname, using actual loopback connection provenance. It is not available on the existing `http://127.0.0.1:5000/setup` kiosk. A superficial HTTPS link, forwarded header, local HTTP DM PIN or browser certificate bypass must never substitute for physical proof.

## Explicit opt-in

The fresh-install hub configuration stored at `~/.config/initiativetracker-hub/kiosk.json` may include the optional **`hub_https_origin`** setting, for example `"https://hub-<device-id>.colophon.stream"`. Do not place keys, API credentials, account tokens, or a query/path in this field. The installer does not enable this setting by default, and this PR must not alter existing production configuration.

The launcher runs the reviewed Python kiosk selector **after** its existing application readiness checks and update-checker phase. HTTPS Setup is selected only if:
- the configured origin is a canonical HTTPS hostname with no URL userinfo, path, query, fragment, or custom port;
- all current system DNS answers for that hostname are loopback addresses, with no LAN/remote answers;
- the hub's `/system/health` responds over that origin with system certificate validation and no proxy or certificate-validation bypass; and
- the response identifies healthy InitiativeTracker.

If any check fails, the launcher retains the previous local HTTP Setup URL. That HTTP fallback does **not** grant secure authority, show a secure overlay, present physical enrollment credentials, or bypass the existing secure origin/provenance guard. Browser profile and updater-before-browser order are preserved. The supervisor likewise checks the exact HTTPS target, and on sustained HTTPS loss can reopen the local HTTP Setup screen after retaining the existing profile; it does not rerun the graphical updater or grant privileged access.

### New-code coverage

The Python startup tests exercise canonical URL filtering, mixed/remote DNS rejection, certificate/health failures, opt-in launcher ordering, persistent Chromium profile, sustained TLS failure and transient outage handling. Physical touchscreen and real Chromium error-page behavior remain hardware acceptance gates.

## Approval flow scope

1. A DM requests secure authorization from their own trusted HTTPS browser and sees a short browser reference. Existing physical-code fallback and DM recovery remain available.
2. A player first joins through the normal HTTPS join flow, or a display pairs through the normal DM-approved display flow. Only an identity already valid for the current game session may request additional secure enrollment.
3. The **physical** canonical HTTPS hub shows each pending DM/player/display request and the exact browser reference, request purpose, role, profile/display ID, and session. The local operator makes an explicit Approve or Deny decision with touch-sized controls; the overlay appears only on physically verified HTTPS pages. A securely authorized DM may still approve players and displays remotely by the original protected flow.
4. The requesting browser alone completes its own pending enrollment via same-origin antiforgery-protected POST. It cannot transfer approval to another browser. Approvals expire after five minutes, are single-use, and are invalidated by cancellation, identity changes and restart. A physical tap never grants network-administration or satellite-agent privileges.

### Opt-in remembered browsers

After fresh DM, player or display approval, the user can explicitly choose **Remember this browser for 30 days** and label it. The existing `__Host-` browser cookie is 256-bit random, Secure, HttpOnly, SameSite=Strict, host-only and now has a 30-day maximum age; **only its SHA-256 hash** is held in Data Protection-encrypted, fsync-protected durable state. No raw browser token is stored on the server, and a browser is not remembered by default.

The remembered credential is a sensitive bearer cookie rather than an unforgeable hardware device identity; someone who copies it could replay it during its validity. Use it only on devices the owner controls. It is bound to the exact trusted HTTPS origin, hub game-session identity, role and immutable credential fingerprint. A revoked player credential, untrusted/deleted display, changed DM PIN, session change, secure recovery epoch, or explicit Forget/Revoke prevents restoration.

A returning browser opens `/secure-access` to restore a **new 12-hour secure grant**, without repeating physical approval, only while the absolute 30-day trust is valid. The original browser cookie alone has no authority over HTTP and cannot authorize new roles. Restored sessions cannot extend their own 30-day trust; extending the time requires a fresh physical or DM authorization. A DM may revoke remembered browsers; individuals may revoke their own. Cancel/revoke, relevant identity changes and DM recovery invalidate their trust. No database migration is added: preexisting Format=1 secure state reads an absent remembered-browser list as empty.

**Not implemented here:** touchscreen boot branding, full remembered-device hardware attestation, or an overlay on legacy HTTP pages. Real physical touch, browser profile and boot/recovery acceptance remain required.

## Review and production gates

1. Restore GitHub Actions ability to run full jobs and publish essential evidence. Do not interpret jobs that fail before any steps as passing tests; avoid removing stored artifacts without an explicit retention decision.
2. Pass .NET Linux/Windows regressions, exact-origin/physical HTTPS browser tests, secure enrollment/replay/expiry tests, launcher/updater integrity checks and the Debian integration tests on **one exact final commit**.
3. Confirm the runtime launcher manifest hash matches the changed launcher; review any separate updater component provenance, deployment manifest and rollback protection, and use approved versioned deployment procedures. A launcher change must never be installed as an unverified loose file.
4. On a disposable environment, verify public CA chain, FQDN loopback resolution on the hub, canonical port 443, socket provenance, HTTP compatibility and authentic Chrome touch behavior. Test automatic recovery after deliberate local service outage and brief glitches, including a TLS-only outage.
5. Request explicit owner approval for physical Pi deployment, any desktop autostart/supervisor transition, optional `hub_https_origin`, certificate/DNS or configuration changes, and a rollback procedure. Keep application data, updater, satellite and network settings unchanged unless separately approved.
6. Verify same browser profile, normal startup/pairing/play, fullscreen recovery, live pending requests, exact-browser approval, deny/replay/expiry, and stable HTTP/HTTPS health after deployment.

## CI history and current gate

The prior CI startup/artifact failure was resolved when the owner temporarily made the repository public. The fully tested prior checkpoint `18951c85f6ac5e0ce2d63e4ba200de5d69a4ba9a` passed all six workflows. This newer player/display and remembered-browser implementation requires **fresh exact-head** .NET and real-browser test evidence; previous green runs do not certify new commits. No physical Pi testing is claimed.

## Phase 2 — disposable player/display user-journey acceptance

Phase 1 passed on exact commit `290da36f5b1904a4e0d1f08218f04f661efc79ca` across all six hosted workflows. Phase 2 covers the entire player QR/manual join and display quick-connect/pair flow using real browser sessions and separate identities, including a queue of simultaneous player and display requests. These tests are disposable; no real Pi or production network changes are authorized.

The Setup display QR retains its legacy quick-view `/display/connect?token=…` behavior. A quick-connected display has no DM-approved device identity and must *not* receive private-media authority. The secure-access page and display appearance panel provide a **Request DM pairing** route; `/display/pair` permits an unpaired quick-connected Display cookie to create a request. The existing DM pairing decision creates a trusted device ID; only then may the browser request physical/DM secure approval. Already trusted paired devices retain their existing redirect to the display.

Acceptance includes: different player browsers receive distinct references; the physical operator may deny one while retaining others; approval redeems only the correct browser and role; legacy quick-connect alone cannot authorize private media; exact player/display identity is visible in the hub overlay; remembered 30-day trust, expiration, cancellation, credential/identity revocation, HTTP denial and manual-code fallback are covered by the existing secure access tests. Camera QR scanning, physical touch and kiosk boot remain separate physical acceptance gates.
