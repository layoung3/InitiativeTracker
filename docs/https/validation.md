# Validation and evidence boundaries

Run from the repository root:

```sh
python3 -m unittest discover -s https-infra/tests -v
python3 -m unittest discover -s pi-network/tests
node --test scripts/tests/n7-ui.test.cjs
dotnet test -c Release
dotnet publish InitiativeTracker.Web -c Release -o /tmp/it-https-publish
python3 https-infra/tests/published_tls_smoke.py --publish /tmp/it-https-publish
```

`Shared HTTPS foundation` CI repeats those checks, audits direct/transitive NuGet packages and publishes a Linux ARM64 application. It checks publish output contains no PFX/key/PEM material and preserves test/audit reports. It uses only read permission on repository content. No production secrets or ACME/DNS access are needed.

The Python suite also executes a real fake-backend process against the Certbot CLI contract: initial issuance, every supported provider argument shape, propagation/terms/scope/version gates, renewal/no-op export, failure cleanup, confined lineage resolution, matching archive numbers, validation failure retention, forbidden hooks, additive ambient config rejection, TERM-resistant descendant termination without signalling another process group, and noisy output bounds. The default-file regression follows pinned Certbot 5.9.0 parser defaults; development also confirmed the behavior using an uninstalled ConfigArgParse 1.7.1 source fixture (no runtime package installation). This is not a real Certbot/plugin installation or DNS API test. A deterministic old-peer/new-generation regression verifies that an incorrect TLS peer fingerprint cannot acknowledge activation. The Python unit suite validates exact hostname policy, private-address choices, assigned-address/stale-AAAA detection, offline resolver behavior, real OpenSSL key/chain/purpose checks against a disposable injected test CA, rejection under ordinary public trust, malformed inputs, permissions/links, dry-run, atomic activation, renewal failure and confirmed/unconfirmed rollback. The .NET suite independently validates trust, dates, wrong host/SAN/purpose, clocks, private keys, transport guards and spoofed proxy headers; existing application tests exercise combat, player privacy and pairing behavior on master.

`published_tls_smoke.py` actually launches the published app with disposable database/data and process-only synthetic trust. It verifies no-certificate TLS rejection, real Kestrel serving and rotation, normal public-root rejection of synthetic certificates, wrong SNI, invalid candidate retention, actual reload/rollback, more than 16 accepted generations without a restart, HTTP sensitive rejection and continuing legacy health. It makes no trust-store, DNS, firewall or Pi changes. Environment test trust is confined to that process and has no production configuration counterpart. Its CA is not evidence of acceptance by public browsers or a real CA. ARM64 publish is a build check; this checkpoint does not claim ARM64 execution or physical-Pi validation.

No public issuance, DNS propagation/plugin API, real domain, real browser public trust, true internet-disconnected venue, systemd/PID1, router DHCP, NetworkManager DNS, cold-boot clock, desktop/kiosk/updater handoff, physical recovery or N8/N9 combined deployment has been validated. See the migration guide for required owner configuration and hardware stages. This branch does not contain those frozen branches' private-media or agent implementations.

The approved secure authorization suite now exercises physical HTTPS-only proof, browser/origin/purpose/session binding, CSRF, cookie attributes, legacy cookie/PIN denial, explicit player/display approval, role/profile mixing, expiry, attempt/queue bounds, atomic redemption, cancellation/revocation races, immutable identity checks, real legacy mutation handlers, durable revocation/restart and fenced persistence failure. The published TLS test includes real Kestrel authorization handlers and restart survival. The frozen-N9 overlay runs its actual handlers and storage/ownership policies with the candidate auth modules; its results are stored separately in CI. It changes no PR heads or production data.

`SecureGrantDurabilityTests` verifies actual Linux directory synchronization and ordered store/ancestor, fence-create, rename and fence-delete boundaries. Deterministic failures at each stage return unavailable and require recovery; actual protected revocation handlers additionally verify restart fencing after each commit-stage failure. Unsupported directory durability rejects existing secure state. These tests establish syscall/error handling and ordering, not physical power-cut behavior or dishonest storage-controller resistance.

The updater regression deliberately restores a revoked ticket’s earlier protected state and keys, executes the actual reviewed updater fence helper, and verifies private endpoint denial before/after restart plus fresh physical recovery. Disposable updater lifecycle tests verify fencing occurs before the old app restarts, retains database/media/keys/identity, and refuses an unsafe secure directory. This is automatic rollback evidence, distinct from the general stale-manual-backup limitation.
