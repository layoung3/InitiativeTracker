# N8 device coordination and threat model

Status: opt-in prototype implementation; no hardware validation or deployment. See [installation and validation](N8-installation-and-validation.md) for exact infrastructure gates and acceptance requirements.

## Trust and transport

The existing display cookie remains encounter/display authorization only. Network agents enroll separately with a client certificate. A dedicated optional HTTPS listener uses TLS 1.2/1.3; agents pin its complete SHA-256 certificate fingerprint obtained from the physical hub screen. Discovery is an address hint only. Enrollment is approved at the hub DM screen after matching the full client certificate fingerprint shown on the physical satellite. No trust on first use, password envelope cipher, remote reset, or plaintext transport fallback is used. Unknown certificates may reach enrollment but cannot read plans. Every plan/status/action rechecks current enrollment. The HTTPS listener exposes only the coordination API, never browser pages or cookies.

Browser Wi-Fi secret entry requires an explicitly configured publicly trusted HTTPS endpoint. HTTP pages, including local pages, contain no Wi-Fi password inputs, and middleware rejects secret-bearing handlers before model binding. Agent enrollment approval requires the physical hub DM screen and exact certificate comparison. The public browser HTTPS provider is replaceable; no product domain or production certificate is configured. No trust is claimed for legacy public hub GUID discovery or the legacy HTTP display channel.

## Transition contract

Protocol version 1 advertises network.prepare, network.commit, network.recover and network.reconnect capabilities for satellite displays and future wireless bases. Enrollment has a generation ID; revocation removes trust and all outstanding authorization. A persistent monotonic revision and UUID identify each transition. Prepare has a fixed expiry; commit persists one fixed activation time and recovery deadline before notification. No DM cancel exists after commit. Offline/unready device names are shown; commit requires explicit IDs to be left behind. A device's successful target reconnection is distinct from the DM's hub confirmation. Both are required to finalize that device.

The hub keeps its existing independent root recovery controllers and timers. Root scheduled switching calls those controllers with the remaining bounded recovery interval. Device recovery is independent of the hub and web process. Satellite intent is written and fsynced before readiness/commit acknowledgments and before network effects. Repeated or stale messages cannot change the deadline. Restart resumes persisted intent. Corrupt journals fail closed rather than guessing a network; repairing corrupted protected storage requires administrator recovery.

## OS and ownership boundaries

The .NET satellite runs as an unprivileged service account. A small root Python broker accepts only bounded JSON operations on a peer-credential checked Unix socket: stage WPA profiles, inspect the active UUID, activate an owned profile, and forget owned profiles. No shell, arbitrary process, password argv or caller-selected path is accepted. A separate independent timer executes durable rollback even while the agent is stopped. Private state files/directories use 0600/0700 and atomic replacement with file and directory fsync. Local recovery uses loopback controls and an optional publicly trusted HTTPS phone endpoint with a unique secured AP, separate owner proof, one-time screen codes, random CSRF tokens, strict Host/Origin checks, bounded sessions and fixed AP leases; remote outages never authorize reassignment. Existing browser pairing and encounter synchronization remain separate.

## Recovery policy

Reachability means an authenticated pinned TLS request, not DNS, ping, SSID or a matching public GUID. Short failures preserve the current profile. At least 120 seconds of consecutive failed probes and at least six failed probes are required before an automatic recovery attempt. Attempts have cooldown/backoff, alternate the known current profile and paired private hotspot, verify the pinned identity after joining, and restore the previous network on failure. A pending coordinated transition takes precedence over ordinary recovery. A local touch recovery action remains available when the old hub is unreachable.

## Sources

- [ASP.NET Core certificate authentication](https://learn.microsoft.com/aspnet/core/security/authentication/certauth)
- [SignalR client configuration](https://learn.microsoft.com/aspnet/core/signalr/configuration)
- [NetworkManager keyfile format](https://www.networkmanager.dev/docs/api/latest/nm-settings-keyfile.html)
