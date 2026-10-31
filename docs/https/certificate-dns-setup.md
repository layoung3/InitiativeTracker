# Certificate and local DNS setup (review before installation)

Nothing in this guide has been applied to the 0.1.5 hub. Examples are placeholders and deliberately fail until the owner supplies a domain. No domain, provider or certificate account has been chosen on the owner's behalf.

## Owner prerequisites and dependency decision

Obtain or use a domain you control, select a dedicated hostname below it, and choose authoritative DNS with an automation API or delegate `_acme-challenge.<hostname>` to a dedicated zone. Confirm registrar renewal, CA terms/account contact, DNS API scope, plugin support for the exact delegation method, propagation behavior, MFA and credential rotation. Public certificates expose hostnames in Certificate Transparency; do not put personal/player names in them.

The implemented optional adapter targets maintained **Certbot 5.9.0 plus one owner-selected DNS plugin**. Current official metadata (2026-10-31) reports Apache-2.0 and Python >=3.10: [release metadata](https://pypi.org/project/certbot/5.9.0/), [license](https://github.com/certbot/certbot/blob/main/LICENSE.txt). Neither is installed, vendored nor added as a dependency here. Backend/plugin choice, versions, source, license notices and their dependency/security audit require review before installation. Certbot's Apache-2.0 terms and the selected plugin's actual license must be preserved with the deployed tool; do not assume every third-party plugin has the same license. An unsupported provider means selecting another supported DNS/delegation service, not implementing a new ACME protocol in this app. OpenSSL, Python and .NET remain platform components whose distribution notices and updates must be maintained.

Prefer record-scoped API access. If unavailable, use a dedicated challenge-only zone with its own restricted token rather than a token for all records of the gameplay/organization domain. Test DNS-01 in the CA staging environment first. Staging certificates are intentionally untrusted and must not pass production activation. Approve real issuance separately. No public A/AAAA record, incoming port 80/443, port forwarding, WAN publication, tunnel or private CA installation is required. Issuance/renewal does require outbound access to the selected CA, DNS provider and public DNS infrastructure.

## Files and process boundaries

After independently approved OS package and filesystem setup:

- `/var/lib/initiativetracker-issuer`: root-only 0700, backend account state, credential files 0600 and restricted logs. Separate from application data and releases. App user must not be a member of any issuer group.
- `/var/lib/initiativetracker-browser-tls`: root:`initiativetracker-tls`, mode **2750** so generations inherit the read-only group. App user has supplementary membership in this dedicated group, without write access. Generations 2750, `bundle.pfx` and `active.json` 0640. No world access or symlinked parents. Parent directories must not be group/world writable.
- `/etc/initiativetracker-https/settings.json`: root-owned 0600, based on `https-infra/templates/settings.example.json`. No secrets in this JSON. Code and renewal adapter under `/usr/local` must be root-owned and unwritable by app/group.

Templates are review artifacts, not installer inputs. The renewal service template is privileged and intentionally cannot choose/install its own DNS plugin. It confines writable paths and exposes no new socket. Review distribution paths, service sandbox compatibility and outgoing DNS/API needs in a disposable system before approval. Its root group should be the TLS read group, so exported bundle/manifest ownership is correct. No automatic timer or backend installation is performed by this PR.

The built-in `certbot_adapter.py` executes the selected reviewed binary with explicit `certonly` or `renew`, DNS-01 only, a single exact hostname, private config/work/log paths, file credentials, provider propagation delay and no installer/HTTP challenge. Initial issuance requires `terms_approved`; every execution requires `scope_reviewed`, an explicit HTTPS ACME directory/contact and the reviewed executable version. Defaults deliberately contain invalid owner placeholders and false approval flags. No provider/domain/account is selected for the owner. The optional shell wrapper now invokes this real adapter; it is not an exit-78 stub.

Supported provider interfaces are the Certbot project's file-credential plugins: cloudflare, digitalocean, dnsimple, dnsmadeeasy, gehirn, google, linode, luadns, nsone, ovh, rfc2136 and sakuracloud. Configuration selects one `dns_plugin`; the adapter passes `--authenticator dns-<plugin>`, `--dns-<plugin>-credentials <private-file>` and `--dns-<plugin>-propagation-seconds <delay>`. [Official plugin list](https://eff-certbot.readthedocs.io/en/stable/using.html#dns-plugins), [file/propagation interface example](https://certbot-dns-cloudflare.readthedocs.io/en/stable/). This is not an endorsement or assumption of a provider account. Route53/environment credential flows and arbitrary third-party plugins are intentionally unsupported until separately reviewed. Delegation compatibility and actual token API scope must be checked with the chosen plugin; a boolean records owner review but cannot prove provider-side permissions.

Set `renewal_timeout_seconds` to include propagation plus at least 60 seconds (maximum 3600); propagation is bounded to 10–1800 seconds. Backend output is discarded; version output is limited to 4 KiB. Timeout terminates its process group. Certbot/plugins handle challenge creation, propagation and normal cleanup; after timeout/provider failure inspect any stale challenge records privately before retrying. The adapter rejects unmanaged renewal hooks, avoids ambient cli.ini and directory hooks, and verifies the configured Certbot version before any issuance/renewal.

After success, the adapter resolves only Certbot's selected live lineage links into the same hostname's archive, requires matching numbered chain/key generations, validates ownership/type/permissions/size, and copies regular 0600 files into an immutable private snapshot. Public-chain/SAN/EKU/key/date validation must pass before a receipt is published. Unchanged chain/key material reuses a revalidated snapshot. Failure leaves the previous receipt and active browser certificate untouched. `foundation.py renew --apply` calls this adapter, reads both export paths from one receipt, then runs the existing atomic activation/served-fingerprint/rollback path. Issuance/export and browser certificate installation remain separate stages.

The source adapter and fake-backend tests do **not** install Certbot or a plugin. Installing Certbot 5.9.0 and one selected plugin, resolving/pinning/auditing their transitive packages, preserving notices and enabling the privileged service need separate approval. Core metadata includes acme, ConfigArgParse, configobj, cryptography, distro, josepy, parsedatetime and pyrfc3339; this is not a resolved dependency lock or vulnerability audit of the uninstalled backend. Review the final provider-specific lock and ARM64 package availability before installation. Backend API/DNS/public issuance acceptance still needs separately approved live testing.

Review dry-run using the separate private config:

```sh
python3 foundation.py issue --config /etc/initiativetracker-https/settings.json
python3 foundation.py activate --config /etc/initiativetracker-https/settings.json
python3 foundation.py renew --config /etc/initiativetracker-https/settings.json
```

`issue` without `--apply` validates planned settings without executing the backend, installing packages or using the network. After separate initial-issuance approval, `issue --apply` obtains and validates an export only; it does not activate browser TLS. A CA staging certificate is expected to fail public export validation and can never be promoted; staging DNS/plugin success must be checked in private backend logs. Use a separate staging issuer directory/account configuration so it cannot replace production state. `activate` validates certificate inputs and store permissions without changing them. `renew` without `--apply` does not invoke the backend or access the network. Only a separately approved invocation with `--apply` writes/promotes or runs renewal. Certificate installation is a distinct approval stage; these commands are documentation, not permission to run on either Pi.

Enable the optional app TLS drop-in only in an approved app stage. Browser TLS uses **5445**, not privileged 443, avoiding an added bind capability/reverse proxy. Local health/activation uses loopback with the public hostname as SNI and normal OS chain validation. Browser URL is `https://<hostname>:5445`; connecting by IP or `.local` will not match this certificate. The app's listener may start with no certificate and reject TLS while retaining legacy HTTP; activation can then succeed after first issuance.

## Local resolver strategies

Public ACME TXT records validate ownership; they do not make phones find a private address. There is no universal transparent offline HTTPS name-resolution solution on arbitrary venue Wi-Fi.

**Managed external Wi-Fi:** the router/DHCP/resolver administrator must support a private exact-host override and tell clients to use that resolver. Prefer an approved DHCP reservation for the hub; update overrides when the assigned address changes. Confirm no client isolation/firewall blocks hub access. The hub cannot change another router's DHCP or clients' DNS itself. External Wi-Fi with no resolver cooperation is an unsupported offline mode: use the hub hotspot or an explicitly managed client resolver, after separate setup approval.

**Hub private hotspot:** integrate the exact host override into the hotspot's existing DNS service and advertise that resolver through its DHCP configuration. Do not start a second wildcard-listening dnsmasq, replace NetworkManager config, or let an untrusted network query it as an open resolver. Bind only the approved hotspot interface/address and firewall queries to its subnet. NetworkManager shared-mode DNS integration differs across OS versions; validate the supported configuration mechanism on the actual image first. This PR renders only a record snippet and never reloads networking.

```sh
python3 foundation.py dns-template --config /etc/initiativetracker-https/settings.json --address 192.168.4.1
python3 foundation.py preflight --config /etc/initiativetracker-https/settings.json --address 192.168.4.1
```

Addresses must be explicit and currently assigned to the hub. Output contains an exact `host-record` and `local` rule, not a public DNS mutation. Select only reachable RFC1918 IPv4 and/or stable ULA IPv6 addresses for the intended clients; no global IPv6, scoped link-local or automatic interface selection. Do not advertise an unreachable address from another interface. Preflight requires the complete resolver answer set to match selected addresses, including AAAA: stale IPv6 can break clients even when A is correct. Refresh the reviewed local record after address/interface changes, then repeat client checks. An unused public/global address assigned to another interface does not invalidate an explicitly selected private address.

The tool's preflight runs on the hub; it does not prove a phone uses that same resolver. Test resolution, reachability and browser trust from each target network/client, including Pi Chromium, with internet unavailable. Private DNS/DoH, VPNs, cached DNS and DNS-rebinding protection can bypass/refuse local answers. Do not globally disable client protections or disable router rebinding protection. Use a narrow administrator-reviewed resolver exception if appropriate, or mark the client/network unsupported and use the managed hotspot path. A local resolver cannot override a client's chosen remote DoH provider. Never work around failures by accepting an IP certificate mismatch.

## Offline and failure behavior

| Condition | Behavior and owner action |
|---|---|
| Internet absent, valid cert, correct clocks, working local resolver | Hub/browser gameplay can continue locally; no online revocation lookup is required by the app. Existing displays retain their current HTTP origin. Issuance/renewal retries later. |
| Public DNS/API outage | No new issuance/renewal; last valid serving certificate remains. Existing local resolver overrides remain usable. Do not publish gameplay private addresses in public DNS as an unapproved workaround. |
| Local DNS outage or client bypass | New HTTPS name connections fail; cache may help temporarily but is not a guarantee. Restore the supported resolver or approved hotspot/client path. Existing legacy-origin gameplay/recovery remains available if networking permits. |
| Renewal fails | Do not replace valid active material. Alert owner, inspect private issuer logs without sharing secrets, restore outbound/provider access and renew before actual expiry. |
| Certificate expires or becomes untrusted | Trusted HTTPS and sensitive media/provisioning fail closed; no warning bypass, private-CA prompt or HTTP secret fallback. Existing legacy HTTP gameplay/display routes remain available. Renew and validate through an approved maintenance path. |
| Hub/client clock wrong | Not-yet-valid/expired checks fail. Establish correct time before TLS; verify client as well as hub clocks. Pi without a reliable RTC needs a separately reviewed clock/RTC or trusted time-restoration plan for cold starts without internet. Never disable certificate time checks. Clock corrections can also affect legacy cookie expiry; restore accurate time first and reauthenticate under existing rules without resetting pairing or identity. |
| Bad candidate/reload | Valid last-known-good stays serving. If acknowledgement or rollback cannot be confirmed, treat service state as uncertain and verify served TLS before proceeding. |

Offline runtime is bounded by actual issued lifetime, intermediate/root trust, clocks, address reachability and client resolver policy. No certificate design can promise indefinite publicly trusted offline operation. Read `/system/https-status` through trusted TLS and inspect the served certificate expiry; alert with enough time for operator repair, based on actual remaining lifetime. Keep a documented physical owner maintenance path and encrypted backups before travel.
