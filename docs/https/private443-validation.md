# Private 443 validation and commissioning boundary

CI checks the exact draft PR head, with read-only repository permissions. It does
not install anything on an owner device, issue a certificate, mutate public DNS,
publish ports, migrate production data or create a release/tag. Use PR #8's final
head/workflow receipts; earlier green checkpoints do not certify later changes.

Functional checkpoint `9fc19636b66b6db75100a8e176b18295504e0d30` passed
[private 443/kernel/Chromium](https://github.com/layoung3/InitiativeTracker/actions/runs/41279081042),
[shared HTTPS](https://github.com/layoung3/InitiativeTracker/actions/runs/41279081002),
[N9/ARM64/media](https://github.com/layoung3/InitiativeTracker/actions/runs/41279081084)
and [installer](https://github.com/layoung3/InitiativeTracker/actions/runs/41279081082).
That four-workflow receipt predates the merged branding documentation. The current
reconciliation includes master `0a7e1a7ac86480b012fcfc46702d1b2d396a3bff` and
also runs the branding-command workflow: ordinary/root fake-home tests and Windows
PowerShell transfer mocks. Final reconciled-head receipts for all five workflows
are recorded on the PR after CI completes; no pinned artwork hash is replaced.

| Evidence | What it exercises |
|---|---|
| 256 .NET tests | Existing combat, pairing, ownership/media and secure authorization regressions plus 12 ingress cases: exact Linux family/length, private destinations, immutable capture, missing metadata, Host/port/TLS mismatch and probe route/method separation. |
| 83 Python tests | Existing certificate validation/atomic activation/renewal/rollback plus naming collisions, credential-file security, native backend profiles, staging isolation, fatal package masks, resolver peer/schema checks and inactive AP profile contracts. |
| Actual Debian backend container, network disabled at runtime | Coherent Certbot 4/Cloudflare package versions, actual CLI/parser flags, additive ambient defaults and exact vendor-file/hash protection. No CA or provider API execution. |
| Actual dnsmasq container, network disabled | Exact A/AAAA answers with the same name mapped to 127/::1 in its local hosts file. No loopback leak; absent IPv6 stays absent; subdomains do not become wildcard answers. |
| Actual isolated kernel/Kestrel containers | IPv4/IPv6 PREROUTING and loopback OUTPUT; original-destination metadata from real sockets; remote physical GET/POST forbidden; direct backend firewall rejection; exact status-only exception; old-origin replay; cookies/CSRF, N9 settings/upload/revocation and persistent media; WebSocket Origin; rotation/fingerprint/rollback/restart. App UID 65000 has no added capabilities. |
| Address-transition fixture | Actual home→fixed hotspot→home IP removal/addition, unchanged nftables and app process, continued secure grant/media use and denial of unassigned origins. NetworkManager metadata is a contract fixture; this does not simulate a physical radio, DHCP clients or NM itself. |
| Canonical-origin Chromium fixture | Existing N9 trusted-browser tests adapted only for hostname/443/test issuer: untrusted profile rejection, trusted no-warning process profile, 390/844/1280px layouts, actual physical/DM/player/display enrollment, character/campaign art, replaceable logo, live combat display and revocation. Normal cookie/CSRF/Origin behavior is exercised at the no-port origin. |
| Cumulative existing workflows | Direct/transitive NuGet and npm audits, responsive UI, installer/network/startup regressions, Linux ARM64 publishing/native image execution, package/manifest checks and legacy direct-5445 Kestrel rotation tests. |

The kernel test uses an internal Docker network, no host networking or published
ports. Only the fixture hub container gets NET_ADMIN, for its own namespace;
Kestrel drops to UID 65000. A separate client has no added capabilities. The
browser shares only that disposable hub's network namespace to model the physical
screen, with capabilities dropped and a separate unprivileged user. It does not
grant a remote browser physical authority.

The Chromium tooling reuses Playwright 1.58.2 and its official digest-pinned
[test-only container](https://playwright.dev/docs/docker). The default headless
test launcher does not exercise Chromium's OS sandbox; no production browser
flags/security settings are changed. The synthetic CA is trusted only in the
app process and a temporary browser NSS profile. A separate profile must reject
it. No OS trust store, `ignoreHTTPSErrors`, certificate-warning bypass or public
CA issuance is used. Private Playwright call logs are suppressed because they
can contain synthetic proof values; only static case names/counts and enumerated
browser error codes are emitted. No codes/cookies/keys are released as artifacts.

## Remaining owner and physical acceptance

Before any live stage, independently review the final source and staged commands.
Record the actual stored full GUID/inventory, approved profile UUID/interface,
current LAN and fixed hotspot address, client support matrix, CA contact/terms,
actual Cloudflare token scope and expiry/renewal responsibility. The token stays
with the owner. No domain/provider choice remains: those are confirmed as
`colophon.stream`/Cloudflare; their actual authority/configuration still needs
separately approved verification.

Actual Debian 13 ARM64/Pi kernel nftables/original-destination behavior, systemd
mask/service restrictions, package maintainer effects, NM shared DNS effective
configuration, Wi-Fi/AP transitions, phone resolver policy, XB8 client mapping,
real public-root trust, offline cold-start clocks, power loss, Labwc/updater/kiosk
and graphical recovery remain unexecuted on hardware. The generic nftables service
must not be restarted/flushed accidentally during a package stage. The source
configuration is disabled by default; all installation/API/app/DB/network/
certificate/startup stages require their own approval.

Existing N8 pinned agent identity and enrollment remain frozen in PR #4 and
require a later reviewed integration. Browser/display grants cannot administer
that agent or network credentials here. No combined N8 hardware/software test is
claimed. Restore/fallback must retain whole-data consistency and fence stale
secure grants before app restart or origin rollback, as in the deployment guide.
