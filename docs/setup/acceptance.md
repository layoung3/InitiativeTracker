# Installation and recovery acceptance checklist

[Setup index](../../SetupReadme.md) · [Evidence/gaps](evidence-and-gaps.md)

Run on the intended hardware after commissioning, in a test session with local
recovery access. Disruptive network, reboot, update and restore cases belong in
a maintenance window, not during play. An unchecked box is **not verified**.
This task did not execute these hardware tests. The user's reported 0.1.5 deployment
and previous successful external-Wi-Fi reboot are evidence, not a complete
fresh-device acceptance result.

Keep a private record: device/model, storage, PSU, screen revision, OS image/date,
Debian base, reviewed commit, package/manifest hashes, installed OS-component
hashes, test date, operator, actual timing and result. Share only sanitized pass/fail
notes. Do not record passwords, PINs, pairing tokens, QR payloads or database contents.

## Hub and display

- [ ] **ARM64 desktop OS:** `uname -m` → `aarch64`;
  `dpkg --print-architecture` → `arm64`; `systemctl get-default` → `graphical.target`;
  `systemctl is-active lightdm` → `active`. Record OS version/image.
- [ ] **Power/storage:** `vcgencmd get_throttled` has no active undervoltage;
  `df -h /var/lib/initiativetracker /opt/initiativetracker` has room for new/old
  release plus full backups. Verify expected PSU and screen power before load tests.
- [ ] **Service/data:** `systemctl is-enabled initiativetracker.service` → `enabled`;
  `systemctl is-active initiativetracker.service` → `active`;
  `readlink -f /opt/initiativetracker/current` → intended versioned directory.
- [ ] **Health:** `curl -fsS http://127.0.0.1:5000/system/health` → HTTP 200,
  `status=healthy`, database connected/migrationsCurrent true, storage ready.
  `/system/status` has intended version and zero pending migrations.
- [ ] **Persistent identity:** capture `/system/hub-info`, restart app during the
  test window, re-read it → same GUID and hostname. Confirm DB and
  `DataProtection-Keys` remain under `/var/lib/initiativetracker`.
- [ ] **mDNS:** `systemctl is-active avahi-daemon` → active; from a second Linux
  client `avahi-resolve-host-name HUB_NAME.local` → hub's current address.
  Browser/SSH work using the advertised name and original Linux user.
- [ ] **Cold boot without keyboard:** shut down cleanly, remove commissioning
  keyboard, power up → autologin, health wait, graphical checker, health wait,
  Chromium Setup in that sequence. Record elapsed time and any manual action.
  Required approved updater/launcher components must already be installed.
- [ ] **1280×800/touch:** desktop display settings (or `wlr-randr` in the local
  graphical session) show intended mode/orientation. Touch all corners, scroll,
  tap buttons and verify fullscreen. No obscured controls or unexpected blanking.
- [ ] **Setup QR:** at local `http://127.0.0.1:5000/setup`, scan player, display,
  DM and private-Wi-Fi QR flows on a real phone as applicable. Expected correct hub,
  usable login/display/network routes; no unavailable hotspot QR once bridge is
  commissioned. Do not save QR screenshots.
- [ ] **Setup privacy:** from a different machine `curl -o /dev/null -s -w '%{http_code}\n' http://HUB_ADDRESS:5000/setup`
  → `404`. Local Setup still loads.
- [ ] **DM access:** from the chosen network open advertised URL/port, authenticate
  using the private local Setup instructions → DM controls and `/dm/network` work.
- [ ] **Browser crash recovery gap:** the audited hub launcher has no Chromium
  restart loop. After a separately reviewed recovery policy is installed, close
  only kiosk Chromium while updater idle → working Setup returns with retained
  profile. Until then mark this requirement missing; the app's Restart setting
  does not supply it.

## Hub network cases

For each cold boot record `nmcli -f NAME,UUID,TYPE,DEVICE connection show --active`,
boot service journal timestamps and reachability from a client. Do not infer
success from a network icon. Use the exact libexec status commands in
[networking](networking.md#6-supervised-changes-and-acceptance) for final controller
state. Keep an approved way back to the original profiles/configuration.

- [ ] **Installed plumbing:** bridge socket active, root:initiativetracker 0660;
  recovery/return/observer/boot timers scheduled by `systemctl list-timers --all 'initiativetracker-*'`.
  Preparation service enabled; gate files root:root 0600 with documented values;
  recovery keyfile and secret root:root 0600. No secret contents printed.
- [ ] **External network available:** power up with a saved, autoconnect-enabled
  external AP available → selected external UUID, client can reach hub health/DM,
  recovery AP does not take over. Confirm recovery profile autoconnect is `no`.
- [ ] **No external network available:** in an isolated test environment make
  saved APs unavailable without deleting profiles, then cold boot → two separated
  scans, minimum discovery period and eventual private AP activation; phone joins
  and reaches `http://10.84.0.1:5000/system/health` and DM. Record actual time;
  bounded candidate attempts/timer/radio readiness can extend the minimum 60 s.
  **Still requires physical verification; not covered by prior online reboot.**
- [ ] **Visible unusable external candidate:** controlled test AP advertises an
  eligible saved SSID but cannot complete connection → bounded attempt and later
  fallback after NM returns disconnected; no permanent block merely from visibility.
  Preserve real network credentials; do not corrupt production profiles to simulate it.
- [ ] **Late radio/scan error:** controlled fresh-device test of delayed readiness
  → controller holds safely and later proceeds when evidence is valid. Capture
  sanitized reasons. No forced interruption of association or blind hotspot switch.
- [ ] **Priority and provisioning:** two controlled eligible saved networks choose
  intended preference; newly web-provisioned profile remains autoconnect=false
  until deliberately configured. Verify UUID/autoconnect/priority with `nmcli`.
- [ ] **Manual AP retention:** start from a deliberately selected working private
  AP, bring an external network into range → hub does not automatically abandon
  its active AP; clients/game remain available there.
- [ ] **DM external → AP confirmation:** request through `/dm/network`, join target,
  reopen hub and confirm within three minutes → `completed`, private AP remains
  after the deadline, encounter data survives.
- [ ] **DM AP → external confirmation:** select saved UUID, reconnect and confirm
  → `confirmed`, intended external profile remains after the deadline.
- [ ] **Unconfirmed transitions both directions:** repeat separately and deliberately
  withhold confirmation → recovery/return timer restores previous network or AP;
  status `recovered`/`rolled_back` when successful. Time deadline + timer/association
  allowance; if rollback fails, retain local access, logs and journals for diagnosis.
- [ ] **Pending/lock safety:** during supervised transition, inspect boot journal
  → holds for pending state or `controller_lock_busy`, no competing network
  action. Do not manufacture corruption/remove locks on a live hub to test this.

## Satellite and live game

- [ ] **Initial reachability:** from satellite fetch intended hub's `/system/health`
  and `/system/hub-info` → healthy and matching recorded GUID.
- [ ] **Persistent profile:** one kiosk launcher uses its dedicated persistent
  Chromium profile; `/display/pair` shows a code matched/approved in `/dm/devices`
  → managed paired display, no token in autostart/config.
- [ ] **Keyboardless satellite boot:** clean reboot → desktop autologin and full
  display with the same trust/profile, no keyboard action or pairing prompt.
- [ ] **Synchronization:** DM changes HP, visibility, conditions/exhaustion,
  initiative and turns → satellite reflects current public encounter, correct
  privacy and visible header. Test a long encounter and scrolling.
- [ ] **Temporary loss:** interrupt only satellite network, then restore its same
  network → loaded app reconnects/resynchronizes. Test initial hub-unavailable
  startup, Chromium exit and error-page/renderer recovery separately. Record any
  manual reload required; the template does not guarantee renderer recovery.
- [ ] **Network selection limitation:** demonstrate manually selecting both saved
  external Wi-Fi and hub hotspot. Do not mark automatic Wi-Fi following as passed:
  it is not implemented. If the hub moves, satellite can remain on external Wi-Fi.

## Versioned update and restore

- [ ] **Trusted artifact:** follow [canonical procedure](hub-release.md), using the
  already-built approved artifact when available → validator passes, staged pair
  hashes/size match after SCP, bare local manifest path set.
- [ ] **Private consistent backup:** app stopped during whole-data checkpoint,
  keys/config/overrides retained, private directory/archive modes checked, checksum
  and archive readability pass. Prove restoration on an isolated test instance
  or agreed maintenance recovery; mere file existence is insufficient.
- [ ] **Graphical install:** use a separately approved **newer** test release →
  prompt accepts it, `current` selects new versioned directory, health version
  matches, previous release remains, networking components/gates retain configuration.
  Reinstalling 0.1.5 over the same version is not this test.
- [ ] **Persistence:** existing test characters, encounter, hub identity, keys and
  paired satellite still work after update. Verify health/storage and compare
  intended data via UI, without exporting production records.
- [ ] **Binary rollback:** with schema-compatible releases and private checkpoint,
  restore previous symlink via [recovery procedure](recovery.md#application-rollback)
  → healthy previous version, retained data and working clients.
- [ ] **Data restore path:** on disposable/isolated test data, demonstrate matching
  backup+release restoration → healthy compatible schema/identity. Document data
  lost since checkpoint and whether clients need re-pairing. Never simulate a
  broken production DB just to test automatic rollback.

Sign off only with actual results. Mark unsupported cases as limitations and
failed/unavailable cases as blockers. A successful source test suite does not
replace Wi-Fi radio, touchscreen, cold-boot, updater or restore acceptance.
