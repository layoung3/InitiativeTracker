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

The remembered credential is a sensitive bearer cookie rather than an unforgeable hardware device identity; someone who copies it could replay it during its validity. Use it only on devices the owner controls. It is bound to the exact trusted HTTPS origin, hub game-session identity, role and immutable credential fingerprint. A revoked player credential, untrusted/deleted display, changed DM PIN, game-session change, secure recovery epoch, or explicit **Forget remembered browser** prevents restoration. **Revoke session** is a separate action: it ends the active grant without deleting the 30-day remembered approval.

A returning browser follows the normal sign-in flow through `/secure-access/wait` and can restore a **new 12-hour secure grant** without repeating physical approval while its original 30-day trust is valid. After **Revoke session**, the browser must first complete a new normal login (DM PIN, player join/recovery, or approved display pairing); the previously revoked login cookie cannot silently restore authority. The preserved remembered approval does **not** restart its 30-day clock. **Forget remembered browser** removes that approval and revokes matching active grants, so a new physical/DM approval is required. The original browser cookie alone has no authority over HTTP and cannot authorize new roles. Restored sessions cannot extend their own 30-day trust; extending it requires fresh explicit approval. Cancellation, relevant identity changes and DM physical recovery also invalidate trust. No database migration is added: preexisting Format=1 secure state reads an absent remembered-browser list as empty.

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

The Setup display QR retains its existing `/display/connect?token=…` entry URL, but now redirects a valid invitation to `/display/pair` instead of issuing an unpaired Display cookie. The older quick-view login cannot work against the current fail-closed cookie validator (which rightly requires a trusted registered device). This correction does **not** weaken display authentication: the DM must approve pairing before a valid Display identity is issued, and the browser must then separately obtain physical/DM secure approval for private media. Invalid display tokens return 401. Existing trusted paired displays continue to open the display normally. The former unpaired quick-view behavior is not preserved.

Acceptance includes: different player browsers receive distinct references; the physical operator may deny one while retaining others; approval redeems only the correct browser and role; a valid QR invitation alone cannot authorize private media; exact player/display identity is visible in the hub overlay; remembered 30-day trust, expiration, cancellation, credential/identity revocation, HTTP denial and manual-code fallback are covered by the existing secure access tests. Camera QR scanning, physical touch and kiosk boot remain separate physical acceptance gates.

## Automatic approval after normal sign-in (Phase 2.5 development)

Under a **canonically trusted HTTPS** origin, a successful DM PIN login, player join/profile recovery, or approved display pairing leads to the branded `/secure-access/wait` screen. It checks for a matching current secure grant or eligible remembered-browser trust first. Otherwise, its Razor antiforgery form automatically submits a same-origin POST to create exactly one browser-bound, five-minute request; the existing physical HTTPS hub overlay (or securely authorized DM approval for members) must then approve the exact identity and reference. The requesting browser completes its own request and continues to `/dm`, `/player` or `/display`.

A new player's recovery key is still shown before continuing. A denial or expiry does **not** start a replacement request automatically: the waiting page stops and offers an explicit retry. Secure approvals must not be created by a GET. The standalone technical `/secure-access` screen is retained as an advanced device/recovery management fallback, but is no longer a normal part of sign-in.

Browser TLS validation is performed by the browser before the app can run. An untrusted/expired certificate cannot be fixed or overridden by physical approval. Existing legacy HTTP gameplay routes intentionally retain their compatibility behavior during staged migration: they do not provide automatic secure authorization or a bypass. User-facing QR/login links must be routed to the canonically trusted HTTPS origin during production cutover; that is separate from this local source-only acceptance change.

The owner-requested Phase 2.5 local acceptance still requires running the newly qualified head in a disposable Ubuntu/WSL staging checkout; this does not modify the running Raspberry Pi or production configuration.
