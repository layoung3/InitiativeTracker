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

1. A DM requests secure authorization from their own trusted HTTPS browser and sees a short browser reference.
2. The **physical** HTTPS hub screen exposes the exact request and a five-minute decision. It can approve or deny using touch-sized controls. A modal overlays shared-layout hub pages; the separate physical proof page remains an accessible fallback.
3. The remote browser waits, then completes only its own browser-bound, same-origin, CSRF-protected secure session. A manual code fallback remains available.
4. Physical denial, expiry, cancellation and duplicate completion fail closed; an approval tap does not itself give the physical screen a remote browser session.

**Not implemented here:** one-tap player/display approvals from a verified DM grant, durable multi-week remembered devices/revocation, touchscreen boot branding, or a guarantee that the overlay works on every legacy HTTP page. Do not claim those capabilities complete.

## Review and production gates

1. Restore GitHub Actions ability to run full jobs and publish essential evidence. Do not interpret jobs that fail before any steps as passing tests; avoid removing stored artifacts without an explicit retention decision.
2. Pass .NET Linux/Windows regressions, exact-origin/physical HTTPS browser tests, secure enrollment/replay/expiry tests, launcher/updater integrity checks and the Debian integration tests on **one exact final commit**.
3. Confirm the runtime launcher manifest hash matches the changed launcher; review any separate updater component provenance, deployment manifest and rollback protection, and use approved versioned deployment procedures. A launcher change must never be installed as an unverified loose file.
4. On a disposable environment, verify public CA chain, FQDN loopback resolution on the hub, canonical port 443, socket provenance, HTTP compatibility and authentic Chrome touch behavior. Test automatic recovery after deliberate local service outage and brief glitches, including a TLS-only outage.
5. Request explicit owner approval for physical Pi deployment, any desktop autostart/supervisor transition, optional `hub_https_origin`, certificate/DNS or configuration changes, and a rollback procedure. Keep application data, updater, satellite and network settings unchanged unless separately approved.
6. Verify same browser profile, normal startup/pairing/play, fullscreen recovery, live pending requests, exact-browser approval, deny/replay/expiry, and stable HTTP/HTTPS health after deployment.

## Current CI blocker

In several PR #15 runs on 2026-11-21, earlier tests reached successful test summaries but `actions/upload-artifact` failed because the GitHub Actions artifact storage quota was exceeded. Later workflows report failed jobs with zero steps within seconds, so no meaningful exact-head validation can be inferred from their conclusions. Do not merge on that basis.
