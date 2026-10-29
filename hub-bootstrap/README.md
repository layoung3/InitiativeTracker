# InitiativeTracker Hub Bootstrap - Release Package Install

This bootstrap installer installs the first InitiativeTracker release onto a clean ARM64 Linux hub.

It consumes the same:
- release manifest
- linux-arm64 `.tar.gz`

that future updates will use.

It is intended for the FIRST hub installation. Future upgrades will use the rollback-aware updater.

## Files in this folder

- `install-hub-release.sh`
- `appsettings.Production.json`
- `initiativetracker.service`
- `initiativetracker-hub-hostname.service`
- `initiativetracker-sync-hostname`

## Copy from Windows to the hub

Copy this entire folder plus your generated release manifest/package.

Then on the hub:

```bash
chmod +x ~/hub-bootstrap/install-hub-release.sh
chmod +x ~/hub-bootstrap/initiativetracker-sync-hostname

sudo ~/hub-bootstrap/install-hub-release.sh \
    ~/InitiativeTracker-0.1.0-linux-arm64.manifest.json \
    ~/InitiativeTracker-0.1.0-linux-arm64.tar.gz
```

The installer validates:
- manifest format
- product
- semantic version
- linux-arm64 platform
- filename
- file size
- SHA-256
- archive paths
- expected executable
- host architecture

Then it:
- creates `/opt/initiativetracker/releases/<version>-linux-arm64`
- keeps persistent data under `/var/lib/initiativetracker`
- installs the systemd services
- starts the app
- waits for `/system/health`
- synchronizes the Linux hostname to the persisted InitiativeTracker hub identity
