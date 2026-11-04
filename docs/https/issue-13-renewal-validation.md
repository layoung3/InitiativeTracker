# Issue #13 certificate renewal correction

This branch addresses [Issue #13](https://github.com/layoung3/InitiativeTracker/issues/13).
Production 0.1.6 remains untouched; its existing valid certificate and disabled
renewal timer are preserved. No Pi access, DNS/API request, deployment, app
restart, database write, router or host firewall change is part of this work.

## Evidence and limits

The starting master is `4685c5b342449be092e752c2dbf9e57417ad6117`.
Its 83 HTTPS Python tests and 195 network Python tests pass. No AGENTS.md or
repository `.agents/skills` instructions were present in the selected checkout.

A disposable Debian 13 image with the reviewed Debian Certbot 4.0.0 and
Cloudflare 4.0.0 packages successfully runs a real `certbot renew` against a
synthetic, not-yet-due lineage with networking disabled. The fixture exercises
export, certificate validation and browser-store activation, then confirms a
second renewal leaves the issuer receipt, active generation and key unchanged.
A due renewal runs the real CLI, plugin selection and lineage storage with a
fake ACME client replacing only the external certificate acquisition boundary.
All private material is generated in the fixture; synthetic trust and probe
injections are test-only and have no production setting.

**The production incident's exact cause is not reproduced or established.**
A passing isolated no-op does not prove production issuer files, public trust,
clock, permissions, network or application acknowledgement were correct during
the incident. It also does not test a real Cloudflare DNS-01 transaction.

The following defects are independently reproducible:

* The adapter reports `changed: true` and rewrites its receipt for an unchanged
  validated export. It now reports `export-unchanged`, avoids the write, and the
  orchestrator still validates the certificate and acknowledges the exact
  served generation/leaf before returning `noop-verified`.
* On master, an injected manifest-directory synchronization failure **after
  rename** bypasses rollback and leaves the new generation selected. The
  activation transaction now includes manifest publication in its rollback
  boundary, restores the previous record and confirms the previous peer.
  Immutable keys remain available when recovery is uncertain.
* Certbot's internal random renewal sleep can consume up to 480 seconds of a
  configured subprocess timeout that only reserves propagation/validation time.
  The dedicated systemd timer already randomizes scheduling; renewal argv now
  disables this redundant sleep. Not-due Certbot behavior still runs normally.

Issuer receipts are now created with mode 0600 before rename, without a
post-publication permission change. Browser bundle/manifest permissions, trust
validation, vendor Certbot masks, DNS-01-only scope, hardening and process-group
timeout/descendant cleanup remain enforced.

## Safe diagnostics

Failure JSON contains only a static `reason`, an allowlisted `phase` and a
bounded `code`. Command failures distinguish `failed`, `timeout`, `invocation`
and `output-limit` without printing argv, output, paths, credentials, account
information, parser exceptions or protected logs. Nested phases retain the
most specific failure. Useful phases include `backend-qualification`,
`backend-invocation`, `export-read`, `export-validation`, `issuer-receipt`,
`certificate-validation`, `active-store`, `noop-probe`, `bundle-export`,
`activation-write`, `activation-probe`, `rollback-write` and `rollback-probe`.
`activation` / `rollback-confirmed` means renewal failed but the previous
manifest and peer were restored. A rollback phase failure means recovery is
unconfirmed; it never reports success. Success returns `noop-verified` or
`activation-verified` only after peer acknowledgement.

## Reproducible checks

From the repository root:

```sh
python3 -m unittest discover -s https-infra/tests -v
python3 -m unittest discover -s pi-network/tests -v
python3 -m unittest discover -s scripts/installation/tests -v
python3 -m unittest discover -s scripts/tests -p 'test_*.py' -v
docker build -f https-infra/tests/Dockerfile.certbot4 -t it-renewal-fixture .
docker run --rm --network none it-renewal-fixture
docker run --rm --network none --cap-drop ALL --security-opt no-new-privileges \
  it-renewal-fixture /usr/bin/python3 -I https-infra/tests/renewal_qualification.py
```

The `Private HTTPS 443 deployment` workflow additionally exports the Debian
image into a disposable rootfs and executes the fixture under systemd using
**every Service property from the reviewed renewal unit**. Only ExecStart
changes to the fixture harness, and RootDirectory confines it to the disposable
Debian filesystem. It asserts NoNewPrivileges, an empty effective capability
set and read-only system/source paths. This requires systemd PID1 on the CI
runner; a normal Docker container is not equivalent evidence. Existing kernel
IPv4/IPv6 ingress, browser, HTTP/HTTPS, application authorization, dependency
audit, ARM64 and Windows workflows remain required for the final PR head.

## Later owner-approved production validation

After review, obtain separate approval for deploying the reviewed source and
for one renewal service run. Preserve a protected backup and record the current
active generation, certificate expiry, application health and disabled/inactive
timer state. Keep vendor Certbot service/timer masked. Check the exact deployed
source, owner-only issuer settings and unchanged hardened unit before running.

For the separately authorized service run, inspect only its bounded status and
exit result; keep Certbot logs, keys, account and credentials private. If the
certificate is still not due, require `noop-verified`, unchanged generation and
continuing HTTP/HTTPS health. If due, require `activation-verified` and the new
served leaf/generation, with the previous bundle retained. On failure, stop and
use the static phase/code to scope a protected investigation; do not retry
issuance, enable timers or weaken permissions/sandbox/trust to bypass it.

Timer enablement, real DNS-01 issuance, production changes and any hardware
validation remain separate owner decisions. This draft PR performs none of them.
