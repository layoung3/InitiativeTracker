# N8 prototype installation and hardware acceptance

No part of this procedure has been executed on the working hub or satellite. Installation, security configuration, certificate issuance and deployment require separate owner approval. The owner currently has no spare Pis or storage; no physical trial can run now. An optional later trial uses the existing Pis with separate disposable cards and synthetic Wi-Fi credentials after approval. The source branch does not enable coordination in existing deployments.

The [software evidence and remaining physical cases](N8-software-validation-status.md) distinguish real TLS/socket/NM boundaries from simulated radio behavior and list known infrastructure limitations. The current optional later checkpoint is [authentication migration and the minimum core hardware trial](N8-authentication-and-core-hardware-trial.md). It compares existing Pis with separately approved disposable-card swaps against additional hardware, specifies rollback and separates phone/public-infrastructure acceptance. Fresh N8 administration now requires trusted browser HTTPS even on loopback; legacy HTTP gameplay cookies cannot authorize it. Do not use historical loopback-PIN/bootstrap assumptions.

## Capability and infrastructure status

| Capability | Local prototype | Additional requirement |
|---|---|---|
| Version 1 prepare/readiness/commit/reconnect/finalize | Implemented and automated tests | Approved installation of agent/broker/timer and isolated radio acceptance |
| Hub independent rollback and satellite independent rollback | Implemented; existing hub controllers retained | Real power-loss, boot and failed-Wi-Fi tests |
| Individual agent enrollment over pinned TLS | Implemented; real TLS/SignalR integration fixture | Physical fingerprint comparison and DM approval |
| Fresh N8 DM authentication | Separate secure DM cookie and independent physical network grant; HTTP PIN POST refused before model binding | Approved browser HTTPS/bootstrap; explicitly isolated lab trust is sufficient without a purchased domain |
| Saved-network switching without destination password entry | Implemented | Existing saved WPA-Personal profile and live-switch gates |
| Two-minute reachability debounce and private-hotspot attempt | Implemented | Actual radio and mDNS tests, including cooldown and display restoration |
| Touchscreen local setup/reassignment using an existing network | Implemented | Touchscreen keyboard/accessibility acceptance and existing display approval |
| New WPA2-Personal Wi-Fi password entry, including hub browser forms | Blocked until configured trusted HTTPS is available | Configured trusted certificate/browser and routing; explicitly isolated synthetic lab trust is sufficient for tests; production uses publicly trusted certificates |
| Phone setup, unique AP, owner proof and one-time screen code | Implemented behind trusted HTTPS preflight | Approved product/setup domain and individual certificate lifecycle; phone/browser/radio acceptance |
| Headless provisioning | Same phone service and stable unique WPA access; owner-held secret replaces screen-code requirement | Initial owner recovery bundle must be exported and kept securely; no shared default password |
| Future illuminated bases | Protocol/capabilities only | Firmware intentionally excluded |

The application cannot create browser trust without certificate/domain infrastructure. It does not ask customers to install a private CA, ignore certificate warnings or use HTTP for passwords. No domain has been purchased, no production certificate issued, and no shared private key is included. An absent, expired, wrong-host or untrusted certificate disables password entry and the phone listener. Existing offline encounter/display operation and durable rollback do not depend on a public CA or Internet connection during play.

## Source and package preparation

Build with a patched .NET 10 SDK/runtime (10.0.13 or later; tests use 10.0.14). The satellite SignalR client is 10.0.14. Publish the existing hub through the existing release pipeline. Publish the additional agent separately:

```sh
dotnet build InitiativeTracker.slnx --configuration Release
dotnet test InitiativeTracker.slnx --configuration Release --no-build
dotnet publish InitiativeTracker.Satellite/InitiativeTracker.Satellite.csproj --configuration Release --runtime linux-arm64 --self-contained true --output /tmp/n8-satellite-publish
python3 -m unittest discover -s pi-network/tests
python3 -m unittest discover -s pi-satellite/tests
```

The agent uses Linux, NetworkManager, an AP-capable wlan0 radio, `dnsmasq-base` and `nftables` for NetworkManager shared IPv4, Chromium and the existing desktop account. Verify AP capability and required packages on the disposable satellite before enabling phone setup; the original display-only installer does not add these N8 agent prerequisites. Keep the desktop's persistent Chromium directory at `~/.local/share/initiativetracker-satellite/chromium`; do not delete its cookies or profile. Do not run the fresh-device installer on an existing working Pi to install N8.

## Approved disposable satellite commissioning

Review the exact artifacts before applying any command as root. Back up the existing kiosk configuration and desktop autostart. Installation needs a system account `initiativetracker-satellite` without interactive login and these root-managed files:

| Artifact | Destination / ownership |
|---|---|
| Published satellite directory | Versioned `/opt/initiativetracker-satellite/<version>`, root-owned, read/execute to agent; `current` reviewed symlink |
| `pi-satellite/initiativetracker-satellite-network` | `/usr/local/libexec/initiativetracker/initiativetracker-satellite-network`, root:root 0755 |
| `pi-satellite/systemd/*.service`, `*.socket`, `*.timer` | `/etc/systemd/system/`, root:root 0644 |
| `pi-satellite/systemd/initiativetracker-satellite.conf` | `/etc/tmpfiles.d/initiativetracker-satellite.conf`, root:root 0644 |
| `pi-satellite/initiativetracker-satellite-kiosk` | Root-owned 0755 launcher, invoked in the real desktop user's Labwc session with ` &` |
| Satellite environment | `/etc/initiativetracker-satellite/agent.env`, root:agent 0640, containing explicit configuration only |

Apply tmpfiles, reload systemd, then enable the socket, independent recovery timer and agent service on the disposable device. The root broker accepts only the agent UID using SO_PEERCRED. No sudo rule, shell execution API or desktop-wide NetworkManager privilege is needed. Check timer/socket health before pairing; do not operate without independent recovery. Preserve the existing hub's hardened updater and installed root broker conventions.

The agent's protected files are `/var/lib/initiativetracker-satellite/agent.json`, `agent.pfx` and `owner-recovery.json`. Root network state is `/var/lib/initiativetracker-satellite-network/`. At initial commissioning export an owner-held recovery bundle containing: device ID, satellite certificate SHA-256, unique owner credential, unique setup AP SSID/password, configured HTTPS setup URL and expiry/renewal instructions. The WPA material is generated by the broker's first bounded setup action, stored in root-only `provisioning.json`, and retained across AP windows so a headless owner can reconnect without a screen. Export only during physically authorized commissioning; do not commit or log the bundle. Keep owner authorization separate from AP access. Do not expose `agent.pfx` or hub private keys in the bundle.

For a touchscreen, the managed display URL includes `?satellite=1`, which shows a local **Satellite Setup** button. Setup has an on-screen input keyboard. It can open a bounded AP window, request pairing on an already reachable network, or explicitly forget/reassign the old association. Password controls require the configured trusted HTTPS page. The local page's non-secret setup controls remain available when the old hub is unreachable. A missing/corrupt durable journal fails closed; root filesystem repair is outside the phone flow.

## Hub configuration

N8 is opt-in with `Coordination:Enabled=true`. The agent-only TLS port defaults to 5443 and its certificate/key lives under the configured hub data directory's `coordination/hub.pfx`. Record its full SHA-256 on the physical hub DM Network screen. Agent connections require this exact physical pin and an independently enrolled client certificate. At agent approval, select its corresponding existing display registration; this is an inventory link only and grants no trust from the browser cookie. Existing paired displays without a linked approved agent are included as unready devices, so the DM must explicitly leave them behind or postpone. An agent approved before its display is paired can be linked later from the physical hub screen using the same full certificate comparison. That certificate is **not** a customer-browser certificate. The listener exposes coordination routes only; it does not serve DM cookies/pages.

Install the additional root helper `pi-network/initiativetracker-device-coordination` alongside the reviewed broker/controllers; install the revised socket broker, Wi-Fi provisioner and controller files as one backed-up reviewed component change. Keep the existing recovery/return timers, gate files and service account permissions. Both timers must be active before coordinated scheduling is accepted. The new helper can read only selected WPA-Personal profiles and the hub recovery profile; it exports credentials only to the existing UID-checked local socket. It never puts passwords on argv or logs NetworkManager output. N8 does not silently update the live original updater or invoke its upgrade helper.

Saved-profile coordination preserves WPA2-PSK or WPA3-SAE key management. New-network entry uses WPA2-Personal with safe generated IDs and exact GLib escaping; enterprise/EAP/captive portals remain outside this prototype. For every N8 change, including physical loopback, freshly sign in over trusted browser HTTPS and consume the physical Setup-screen network code. Its separate Secure/HttpOnly fifteen-minute grant is bound to the secure login and game session; captured legacy cookies cannot enter the administration path. N8-disabled local compatibility is unchanged. Prepare from `/dm/network`; preparation expires after two minutes and named offline/unready exclusions must be explicit. Commit activates after ten seconds with a fixed 180-second interval from activation and no post-commit cancel. Wi-Fi A → B preserves the real previous UUID through the existing independent controller, with private-hotspot fallback. Source IDs, gates and timers are checked before readiness. Confirm hub connectivity, then inspect each satellite's separate reconnect/finalize state; missing commit acknowledgement is not success.

## Replaceable production HTTPS provider

`IProvisioningHttps` isolates browser trust readiness from device coordination. The current implementation loads an individually issued PKCS#12 certificate. Configuration:

| Component | Keys |
|---|---|
| Hub browser provisioning | `BrowserTls:Host`, `BrowserTls:Certificate`, optional `BrowserTls:Password`, `BrowserTls:Port` (5445 default) |
| Satellite phone provisioning | `Satellite:PhoneHost`, `Satellite:PhoneCertificate`, optional `Satellite:PhoneCertificatePassword` (5444 default) |
| Headless satellite | `Satellite:Headless=true`; owner credential required, screen code omitted |

Supply an exact DNS SAN, server-auth certificate chain rooted in an OS public trust root, a private key unique to that device and validity covering expected offline use. Readiness uses the OS trust store, hostname, key and dates; customers do not install private trust roots. Browser validation remains mandatory. Private keys/configuration are protected by root/agent-only filesystem access. Use separate agent pins and browser certificates so browser renewal does not replace paired device identity.

For the approved disposable lab only, explicitly installing a synthetic lab root into server OS and isolated browser stores can satisfy the same default-store checks without purchasing a domain. It is not a customer trust-install requirement or a validation bypass. The minimum physical-hub checkpoint can use exact localhost SAN/host. All actual trust/certificate creation remains approval-gated; missing browser trust blocks fresh DM administration even though agent pins themselves need no domain.

A future domain operator must implement issuance/renewal and delivery (for example, individual DNS-validated names without shipping domain-wide DNS credentials to devices). This checkpoint deliberately does not purchase a domain or implement a CA account/certificate distribution backend. Renew before expiry while online, stage new material atomically and restart the service after confirming the chain; interrupted renewal must leave the old certificate usable. Offline operation cannot make an expired browser certificate valid. Expiration disables new password entry/phone sessions; touchscreen non-secret controls, saved profiles, pairing trust and independent recovery remain. The implementation does not provide automatic browser-certificate renewal yet.

For phone access with no Internet, approved configuration must make the selected per-device setup hostname resolve to the satellite AP address `10.85.0.1`. NetworkManager's shared AP supplies DHCP; add the chosen exact hostname to its local shared dnsmasq configuration (review `/etc/NetworkManager/dnsmasq-shared.d/`) and confirm actual DHCP DNS on Android/iOS. The application does not guess a domain or overwrite DNS configuration. Verify phone private DNS/DoH behavior and local-network permission prompts. Keep device clocks within the short prepare window; clock skew fails readiness closed, and reboot or a backwards/forwards clock change during an armed switch must exercise conservative rollback. The eventual DNS/certificate infrastructure is an explicit unconfigured production dependency.

## Phone flow and single-radio limits

The AP uses a stable, per-device random WPA password and a ten-minute durable lease. A displayed WPA QR intentionally grants access to this local setup network; it is not an ownership or pairing key. Screen-equipped devices display a separate one-time code; the phone also needs the independent owner credential. Headless devices require the full owner-held recovery credential. Unpaired devices with configured phone HTTPS offer bounded ten-minute AP windows with a fifteen-minute retry interval, so an owner-held recovery bundle can be used without a button or keyboard. Codes/sessions expire, failed authorization is bounded, and ordinary hub loss never clears association or makes an ownerless claim endpoint available.

After consecutive authenticated hub failures for at least 120 seconds, automatic recovery first retries the last finalized profile when disconnected, then tries the paired private hotspot under its own rollback deadline. It uses bounded cooldown/backoff. Continued failures may offer the secured temporary phone AP when phone HTTPS is configured. Since wlan0 is one radio, this pauses the failed network connection; the old pairing is retained. Unauthorized traffic cannot extend the AP lease. Each lease records its own candidate profile before activation. Expiry disconnects both the setup AP and any unapproved candidate, including when no previous connection exists or restoration fails; failed restoration and teardown remain durable for independent timer retries. Candidate IDs are unique to a lease, so later setup cannot overwrite a previously approved profile. After a bounded window closes, automatic recovery resumes its preferred-network/private-hotspot cycle rather than repeatedly offering only the AP. A touchscreen owner can deliberately open setup before changing networks. No keyboard or dedicated physical button is required.

In an authorized session, the owner supplies the new hub's physical TLS fingerprint, origin, satellite name and destination Wi-Fi credentials, then explicitly authorizes reassignment. The phone disconnects while the satellite leaves its AP and joins the destination. The agent persists the exact destination intent, requests approval over pinned HTTPS and completes automatically only after the destination DM approves the satellite certificate. Each owner-authorized request has a new durable intent and needs fresh destination approval, even when returning to a previously approved hub. Approval is bound to that intent and stale approvals cannot be reused. The old binding remains until the authenticated new intent, generation and recovery profile are durable. A protected pending-binding record precedes root mutation; an exact committed root receipt reconciles a lost reply/agent save after restart, even after the former approval deadline. An expired uncommitted intent cannot bind. The original AP lease bounds candidate authorization; cleanup/reset progress is durable and idempotent. Deploy matching agent and broker together. Cancellation, missing approval, failed joins or lease expiry restore the prior network independently. Display pairing with a different hub still uses the existing DM display-approval flow; its browser cookies are retained per origin.

## Hardware acceptance — owner approval required before execution

Use disposable Trixie ARM64 Desktop cards, the existing model hub/satellite and synthetic credentials first. Record commit/runtime/OS, exact touchscreen SKU, interface/UUIDs, certificate fingerprints, non-secret journal states and observed timings. Never capture passwords or key files.

1. Confirm existing encounter synchronization, pairing cookies and display rendering before N8. Install the opt-in agents/components and verify both independent recovery timers without changing the working devices.
2. Compare both physical TLS fingerprints, enroll the satellite, separately approve the existing display, and reboot both. Verify pairing and encounter data persist.
3. Prepare private hotspot → new destination, show ready state, commit, join destination with DM phone, confirm hub, then verify satellite reconnection/finalization. Repeat external → private hotspot and external Wi-Fi A → saved Wi-Fi B, including independent rollback to A. Verify mDNS plus pinned `10.84.0.1` fallback.
4. Repeat with one satellite powered off, refusal to prepare, partial commit delivery, duplicate/out-of-order messages, wrong certificate, changed hub GUID and revoked enrollment. Verify named leave-behind disclosure and no plaintext password controls/routes.
5. Use wrong destination credentials, remove router power and let confirmation expire. Kill/restart web and agent at each phase; stop the agent entirely after commit. Reboot or remove power immediately before/after journal replacement and network activation. Verify each root timer restores independently, no indefinite deadline extension and no repeating finalized switch.
6. Exercise 15-second and 90-second hub outages: no automatic network change. Exercise confirmed two-minute loss and repeated failed recovery: bounded private-hotspot attempt, cooldown, no continuously cycling networks, retained pairing and Chromium restoration on authenticated reconnection.
7. Touch **Satellite Setup**, cancel, forget and reassign to another disposable hub. Exercise the on-screen keyboard and separate destination DM/display approvals. Verify old plans/generation no longer act after replacement.
8. With supplied publicly trusted individual certificates and configured offline DNS, test current iOS Safari and Android Chrome: WPA QR, separate owner credential/code, consumed/expired code, five failed proofs, CSRF/Origin/Host rejection, expired cert and headless recovery bundle. Verify no warning bypass and no password input when TLS is absent/expired.
9. During phone reassignment, lose Wi-Fi, reject DM approval and restart the satellite. Stop the agent after joining an unapproved candidate. Verify the fixed lease disconnects that candidate with no previous connection, and also when the old router is unavailable; restart the timer and verify durable restoration retries. Exercise multiple expired AP windows, then restore the hub and verify pinned reconnection resumes. Verify the old association survives until approved commit and the previous network returns when available. Confirm phone disconnection is expected and final completion does not need the phone to stay connected.
10. Restore the disposable setup using reviewed backups. Do not merge, deploy to or change either working Raspberry Pi until the owner approves the results and exact component plan.

## Automated evidence

Release build and self-contained `linux-arm64` publish of both hub and satellite pass; ARM64 binaries are not executed on a Pi. Automated suites comprise 137 .NET tests, 196 hub network tests, 51 satellite/controller/socket tests, 3 additional real-NetworkManager container tests, 8 Chromium regressions and 41 disposable installer/updater tests plus shell checks. The three NM cases intentionally skip in ordinary host discovery and run in the dedicated network-none container CI job. Exact-head CI is recorded on PR #4; see the [evidence matrix](N8-software-validation-status.md) for test boundaries, corrected reassignment defects and remaining physical/infrastructure work. Synthetic browser trust is confined to fixtures; no real trust store or Pi was changed. Existing nullable/stack-allocation warnings remain outside N8 changes.
