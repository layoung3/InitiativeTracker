#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
cat > "$TMP/nmcli" <<'MOCK'
#!/usr/bin/env bash
case "$*" in
  '--terse --get-values NAME connection show') printf '%s\n' 'Home WiFi' 'InitiativeTracker-Recovery';;
  '--terse --get-values GENERAL.CONNECTION device show wlan0') echo "${MOCK_ACTIVE:-Home WiFi}";;
  '--terse --get-values GENERAL.STATE device show wlan0') echo '100 (connected)';;
  '--terse --get-values RUNNING general') echo 'running';;
  *) echo "Unexpected call: $*" >&2; exit 90;;
esac
MOCK
chmod +x "$TMP/nmcli"
export IT_NMCLI="$TMP/nmcli"
output="$("$ROOT/initiativetracker-network-supervisor" plan)"
grep -q 'RECOMMENDATION=KEEP_ACTIVE_KNOWN_NETWORK' <<< "$output"
grep -q 'ACTION=NONE' <<< "$output"
output="$(MOCK_ACTIVE=InitiativeTracker-Recovery "$ROOT/initiativetracker-network-supervisor" plan)"
grep -q 'RECOMMENDATION=KEEP_RECOVERY_HOTSPOT' <<< "$output"
output="$(MOCK_ACTIVE=-- "$ROOT/initiativetracker-network-supervisor" plan)"
grep -q 'RECOMMENDATION=CANDIDATE_RECOVERY_HOTSPOT' <<< "$output"
# A profile missing from the list must block any recovery plans.
sed -i "s/'Home WiFi' 'InitiativeTracker-Recovery'/'Home WiFi'/" "$TMP/nmcli"
output="$("$ROOT/initiativetracker-network-supervisor" plan)"
grep -q 'RECOMMENDATION=BLOCKED_RECOVERY_PROFILE_MISSING' <<< "$output"
if "$ROOT/initiativetracker-network-supervisor" switch >/dev/null 2>&1; then echo 'unsafe mode accepted' >&2; exit 1; fi
echo 'PASS: 5 dry-run supervisor tests'
