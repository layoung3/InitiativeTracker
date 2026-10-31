# Shared trusted HTTPS checkpoint

This is an independent master-based checkpoint, disabled by default. It does not merge N8 or N9, install anything, run a production migration, change a Pi, or issue a certificate. Base: `d584b7963ca6bb7916f21d9ee94082f9f2821f34`. Reviewed source contracts: N8 `93e93d37ffa5dfdc809434751f6b641087d7913c`, N9 `1e86dc520f9aa5e228b5639f1e31e9f7f19e0caa`.

## Architecture and implementation

One dedicated, owner-selected public DNS hostname identifies one hub. A maintained ACME client and reviewed DNS provider plugin prove control with DNS-01. Their private state and scoped credentials belong to a separate issuer process; the web app can read only its leaf certificate/key bundle. Issuance uses outbound connections, without port forwarding, public listeners or public A/AAAA records. DNS-01 challenge records and private gameplay name resolution have different purposes.

`https-infra/foundation.py` implements bounded certificate validation, dry-run, renewal adapter invocation, serialized activation, immutable generations, atomic manifest replacement, served TLS acknowledgement, rollback and DNS preflight/template generation. It delegates ACME and DNS APIs, rather than implementing either protocol or cryptography. It uses Python's standard library and the distribution OpenSSL executable. Existing .NET TLS/X509 libraries perform application-side validation independently. No new NuGet/Python package is added.

`BrowserTls:Enabled` adds direct Kestrel TLS on 5445, while retaining configured HTTP endpoints. No reverse proxy or forwarded-header authority is involved. Unknown SNI names fail the TLS handshake. TLS requests also require the configured Host/port and a real TLS feature; sensitive operations cannot use an alternative origin. The new listener binds on local interfaces, so its eventual deployment also needs reviewed local firewall rules: no WAN ingress, router forwarding, UPnP or cloud tunnel. It is not made internet-facing by this checkpoint.

The issuer validates a complete chain using installed public roots, server purpose, dates, a single exact DNS SAN, matching private key and at least 24 hours remaining before promotion. No wildcard, CN fallback, private CA option or disabled TLS validation exists in production configuration. A PFX bundle is written to a generated directory, then an `active.json` manifest is atomically replaced. Both processes restrict path types, links, permissions and input sizes. The app accepts only generated identifiers, never paths supplied in requests. The store is outside releases and outside the web root.

Kestrel polls the manifest every two seconds. Valid replacement certificates are used for new TLS handshakes without restarting the app, desktop, updater or sockets. The issuer independently connects to loopback using the configured hostname as SNI and normal certificate validation, then checks the app's generation acknowledgement. That avoids depending on LAN DNS for local activation while still validating the hostname. Failed issuance leaves the manifest alone; failed activation restores the prior manifest and verifies the old serving generation. Unconfirmed recovery is reported as a failure. The app retains its last valid in-memory generation if files are corrupt or unavailable and rechecks trust/dates for each new handshake and sensitive request. An expired generation is never a usable HTTP fallback.

Old certificate handles are retained for in-flight connections, with a bound of 16 generations per process. A seventeenth generation is rejected until a separately scheduled app restart. Archived keys require controlled retention and protected backups; there is deliberately no automatic deletion of material that may be in use. `/system/https-status` exposes only readiness, generation ID and leaf expiry, never credentials or paths; readiness is true only on the trusted TLS origin. Monitor the served TLS connection and its actual expiry, with alerts appropriate to the certificate's issued lifetime. No assumed fixed certificate lifetime.

## Threat model and security review

Protect browser credentials/private media against LAN interception, malicious DNS/Host headers, accidental HTTP submission, malformed certificate inputs, stale addresses and credential/key disclosure through releases/logging. Trust the OS public-root store, privileged issuer/operator, installation ownership, the hub application process and physical administration. Compromise of root or the web process's memory is outside this boundary; the app necessarily holds its serving private key. Do not use the browser key as an agent identity.

| Boundary | Control and remaining limit |
|---|---|
| DNS API credentials | Separate root-owned issuer state, 0600 credential file, no token in argv, environment or web app. Choose record scope if supported, otherwise a delegated challenge-only zone. Zone scope is broader than record scope. No provider request is made here. |
| Private leaf key | Per-hub, generated by maintained backend, restricted PFX permissions, root-owned store and read-only app group. Unencrypted PFX relies on OS permissions; encrypt backup volumes/media. No shared fleet wildcard key. |
| External commands | Fixed OpenSSL executable and reviewed private renewal adapter; no shell interpolation. Bounded inputs/timeouts, captured output, static error messages and sanitized environment. Adapter/backend must also redact provider logs; issuer logs stay private. |
| Host/rebinding | Exact SAN, SNI, HTTP Host and actual local port; no forwarded headers. Rebinding can redirect traffic but cannot create a trusted certificate or authenticated session. Existing antiforgery, ownership and DM checks remain mandatory. |
| Offline trust | Chain downloads disabled; uses installed intermediate/root material. Revocation is deliberately not an online runtime dependency. Offline clients cannot reliably learn revocation; browsers apply their own policies. Root-store updates still need planned maintenance. |
| Private address resolution | Only explicitly selected assigned RFC1918/ULA addresses, no wildcard addresses, loopback, link-local, public DNS writes or auto-selected first interface. Reject stale A/AAAA mixtures. Tool never changes DHCP/DNS. |
| Failed renewal/reload | Valid last-known-good remains available. Incorrect/expired/future/untrusted certificates fail closed. Rollback revalidates current trust and dates. Persistent file failures may need physical maintenance. |
| Application sessions | HTTP gameplay stays at its existing origin during migration. HTTP cannot acquire new media/network privileges. Host-only cookies cannot migrate to another host; old sessions/keys/database must remain intact. |

There are no new application/database schema migrations in this checkpoint. Existing master still calls `Database.Migrate()` at startup, so **starting a new app binary on the production database remains a separately approved deployment/migration action**, even when no new migration file was added.

## N8/N9 integration contracts, not completed integration

The frozen branches were source-read, not merged or executed together. Tests of reserved route guards are transport contracts and are not evidence of N8 provisioning or N9 uploads running on this branch.

N9 mutations are at `/appearance` (Upload/Remove/Settings), private GETs at `/media`. When this foundation is enabled, the transport guard returns 426 on sensitive HTTP before model binding/auth; all N9 ownership, DM authorization, antiforgery, rate limits, persistent storage and decoder limits must remain. Public defaults and normal combat/display routes stay available at the legacy origin. A later reviewed integration patch must replace N9's transport-only `Request.IsHttps` presentation check with the shared trusted-request policy so UI guidance matches hostname/port/readiness checks. No development environment bypass should be used for production.

N8's `DmAuthenticationPolicy` checks actual Kestrel TLS, local BrowserTls port and matching Host port, and uses a separately authenticated SecureDm cookie. Its current `PublicCertificateHttps` loads a PFX once. A later integration patch must implement its `IProvisioningHttps` over this shared live certificate source and remove its duplicate browser listener/file loader. Keep the secure DM scheme, physical approval, session claims, network authority, size/rate limits and pinned-agent enrollment unchanged. The pinned agent listener on 5443 and its coordination key/trust files must never point at this store. Browser renewal must not re-enroll agents or rotate their pins. Reserved credential provisioning handlers are transport-guarded here; the actual N8 policy is still required.

## Practical setup and future customer devices

For one private hub: owner domain/subdomain, one public leaf certificate, direct TLS on 5445, a maintained DNS plugin and narrow credential or delegated `_acme-challenge` zone. Prefer hub-hotspot operation for venues without a cooperative router. On a managed home LAN, use router/resolver local overrides plus stable DHCP reservations and verified client resolution. Keep existing display-only satellites on their current gameplay origin until individually approved; they do not need server certificates just to display the hub.

For customers: provision an independent hostname, leaf key, issuance scope and lifecycle per device. Define who owns the domain, ACME account/terms, DNS provider, renewals, revocation and support on transfer/end of service. A central challenge service is possible later, but must authorize each device only for its own challenge record; do not ship a shared DNS token or wildcard private key. Device-agent physical pinning remains a separate identity system. Disconnected devices cannot renew forever: publish expiry status and require periodic connectivity or controlled certificate delivery.

## Primary sources reviewed

- [Let's Encrypt challenge types](https://letsencrypt.org/docs/challenge-types/): DNS-01 for private services, scoped credentials and challenge delegation.
- [Certbot renewal and deploy hooks](https://eff-certbot.readthedocs.io/en/stable/using.html#renewing-certificates): maintained renewal policy; successful process exit does not by itself prove a new certificate was issued.
- [Kestrel endpoints and dynamic TLS callbacks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0): direct dynamic certificate selection.
- [dnsmasq manual](https://dnsmasq.org/docs/dnsmasq-man.html): exact host records and local resolver behavior. NetworkManager's private dnsmasq instance must be configured through its supported integration, not a competing daemon.
