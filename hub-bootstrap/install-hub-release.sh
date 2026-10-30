#!/bin/bash -p
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

if [[ "${EUID}" -ne 0 ]]; then
    echo "Run this installer with sudo."
    exit 1
fi

if [[ $# -ne 2 ]]; then
    echo "Usage:"
    echo "  sudo ./install-hub-release.sh /path/to/manifest.json /path/to/package.tar.gz"
    exit 1
fi

MANIFEST_PATH="$(readlink -f "$1")"
PACKAGE_PATH="$(readlink -f "$2")"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -f "$MANIFEST_PATH" ]]; then
    echo "Manifest not found: $MANIFEST_PATH"
    exit 1
fi

if [[ ! -f "$PACKAGE_PATH" ]]; then
    echo "Package not found: $PACKAGE_PATH"
    exit 1
fi

echo "Installing prerequisites..."
apt-get update
apt-get install -y avahi-daemon curl python3

echo "Validating release manifest..."

readarray -t MANIFEST_VALUES < <(
    /usr/bin/python3 -I - "$MANIFEST_PATH" <<'PY'
import json
import re
import sys

path = sys.argv[1]

with open(path, "r", encoding="utf-8-sig") as f:
    data = json.load(f)

errors = []

if data.get("formatVersion") != 1:
    errors.append("formatVersion must be 1")

if data.get("product") != "InitiativeTracker":
    errors.append("product must be InitiativeTracker")

version = str(data.get("version", ""))
if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version):
    errors.append("version is not valid semantic version syntax")

if data.get("platform") != "linux-arm64":
    errors.append("platform must be linux-arm64")

if data.get("healthPath") != "/system/health":
    errors.append("healthPath must be /system/health")

package = data.get("package") or {}
file_name = str(package.get("fileName", ""))

if not file_name or "/" in file_name or "\\" in file_name or ".." in file_name:
    errors.append("package fileName is invalid")

try:
    size = int(package.get("sizeBytes", 0))
except (TypeError, ValueError):
    size = 0

sha256 = str(package.get("sha256", "")).lower()

if size <= 0:
    errors.append("package sizeBytes must be greater than zero")

if not re.fullmatch(r"[0-9a-f]{64}", sha256):
    errors.append("package sha256 must contain 64 hexadecimal characters")

if errors:
    for error in errors:
        print(f"ERROR:{error}", file=sys.stderr)
    sys.exit(2)

print(version)
print(file_name)
print(size)
print(sha256)
PY
)

VERSION="${MANIFEST_VALUES[0]}"
EXPECTED_FILE_NAME="${MANIFEST_VALUES[1]}"
EXPECTED_SIZE="${MANIFEST_VALUES[2]}"
EXPECTED_SHA256="${MANIFEST_VALUES[3]}"

ACTUAL_FILE_NAME="$(basename "$PACKAGE_PATH")"

if [[ "$ACTUAL_FILE_NAME" != "$EXPECTED_FILE_NAME" ]]; then
    echo "Package filename mismatch."
    echo "Expected: $EXPECTED_FILE_NAME"
    echo "Actual:   $ACTUAL_FILE_NAME"
    exit 1
fi

ACTUAL_SIZE="$(stat -c '%s' "$PACKAGE_PATH")"

if [[ "$ACTUAL_SIZE" != "$EXPECTED_SIZE" ]]; then
    echo "Package size mismatch."
    echo "Expected: $EXPECTED_SIZE"
    echo "Actual:   $ACTUAL_SIZE"
    exit 1
fi

ACTUAL_SHA256="$(sha256sum "$PACKAGE_PATH" | awk '{print $1}')"

if [[ "${ACTUAL_SHA256,,}" != "${EXPECTED_SHA256,,}" ]]; then
    echo "Package SHA-256 mismatch."
    echo "Expected: $EXPECTED_SHA256"
    echo "Actual:   $ACTUAL_SHA256"
    exit 1
fi

if [[ "$(uname -m)" != "aarch64" ]]; then
    echo "This release requires a 64-bit ARM Linux system (aarch64)."
    echo "Detected architecture: $(uname -m)"
    exit 1
fi

echo "Inspecting archive paths..."

while IFS= read -r entry; do
    normalized="${entry#./}"

    if [[ "$normalized" == /* ]] ||
       [[ "$normalized" =~ ^[A-Za-z]: ]] ||
       [[ "/$normalized/" == *"/../"* ]]; then
        echo "Unsafe archive path detected: $entry"
        exit 1
    fi
done < <(tar -tzf "$PACKAGE_PATH")

if ! tar -tzf "$PACKAGE_PATH" |
     sed 's#^\./##' |
     grep -Fx "InitiativeTracker.Web" >/dev/null; then
    echo "InitiativeTracker.Web was not found at the archive root."
    exit 1
fi

echo "Release validation passed."

APP_ROOT="/opt/initiativetracker"
RELEASES_DIR="$APP_ROOT/releases"
DATA_DIR="/var/lib/initiativetracker"
SERVICE_USER="initiativetracker"
RELEASE_DIR="$RELEASES_DIR/${VERSION}-linux-arm64"

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
    useradd \
        --system \
        --home "$DATA_DIR" \
        --create-home \
        --shell /usr/sbin/nologin \
        "$SERVICE_USER"
fi

install -d -m 0755 "$APP_ROOT"
install -d -m 0755 "$RELEASES_DIR"
install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$DATA_DIR"

if [[ -e "$RELEASE_DIR" ]]; then
    echo "Release already exists: $RELEASE_DIR"
    echo "Remove it manually only if you intentionally want to reinstall this version."
    exit 1
fi

install -d -m 0755 "$RELEASE_DIR"

cleanup_failed_release() {
    if [[ ! -L "$APP_ROOT/current" ]]; then
        rm -rf "$RELEASE_DIR"
    fi
}

trap cleanup_failed_release ERR

tar -xzf "$PACKAGE_PATH" \
    -C "$RELEASE_DIR" \
    --no-same-owner

install \
    -m 0644 \
    "$SCRIPT_DIR/appsettings.Production.json" \
    "$RELEASE_DIR/appsettings.Production.json"

chown -R root:root "$RELEASE_DIR"
chmod 0755 "$RELEASE_DIR/InitiativeTracker.Web"

install \
    -m 0755 \
    "$SCRIPT_DIR/initiativetracker-sync-hostname" \
    /usr/local/sbin/initiativetracker-sync-hostname

install \
    -m 0644 \
    "$SCRIPT_DIR/initiativetracker.service" \
    /etc/systemd/system/initiativetracker.service

install \
    -m 0644 \
    "$SCRIPT_DIR/initiativetracker-hub-hostname.service" \
    /etc/systemd/system/initiativetracker-hub-hostname.service

ln -sfn "$RELEASE_DIR" "$APP_ROOT/current"

systemctl daemon-reload
systemctl enable avahi-daemon.service
systemctl enable initiativetracker.service
systemctl enable initiativetracker-hub-hostname.service

systemctl restart initiativetracker.service

echo "Waiting for InitiativeTracker health check..."

HEALTHY=0

# Fixed retry budget; the iteration value itself is intentionally unused.
# shellcheck disable=SC2034
for attempt in {1..60}; do
    if curl \
        --fail \
        --silent \
        --show-error \
        --connect-timeout 2 \
        --max-time 4 \
        http://127.0.0.1:5000/system/health \
        >/tmp/initiativetracker-health.json 2>/dev/null; then

        HEALTHY=1
        break
    fi

    sleep 2
done

if [[ "$HEALTHY" -ne 1 ]]; then
    echo
    echo "InitiativeTracker failed its initial health check."
    echo
    systemctl --no-pager --full status initiativetracker.service || true
    echo
    journalctl -u initiativetracker.service -n 80 --no-pager || true
    exit 1
fi

echo
echo "Health check passed:"
cat /tmp/initiativetracker-health.json
echo

systemctl restart initiativetracker-hub-hostname.service

echo
echo "InitiativeTracker hub installation completed."
echo
echo "Release:"
echo "  $VERSION"
echo
echo "Application:"
echo "  /opt/initiativetracker/current -> $RELEASE_DIR"
echo
echo "Persistent data:"
echo "  /var/lib/initiativetracker"
echo
echo "Hub info:"
curl --fail --silent http://127.0.0.1:5000/system/hub-info
echo
echo
echo "System hostname:"
hostname
echo
echo "Use the mdnsHostName shown above for future SSH/browser connections."
