# InitiativeTracker Hub Bootstrap - Release Package Install

This bootstrap installer installs the first InitiativeTracker release onto a clean ARM64 Linux hub.

Start with [SetupReadme.md](../SetupReadme.md) and the complete
[fresh hub guide](../docs/setup/fresh-hub.md) for OS/native dependencies, release
transfer/validation, desktop/updater prerequisites, network provisioning and
acceptance. This script alone does **not** produce a keyboardless hub. It does
not install the graphical updater, kiosk launcher, networking helpers, socket,
timers or gates, and it is not rollback-aware. Never use it for upgrades; follow
[the canonical graphical release procedure](../docs/setup/hub-release.md).
The approved release is self-contained; a separate system .NET runtime is optional.

Examples below use the inspected 0.1.5 version; use your actual approved artifact.
The deployment workflow's Stage step names the staged manifest
`release-manifest.json`; substitute that path if using staged output.

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
    ~/InitiativeTracker-0.1.5-linux-arm64.manifest.json \
    ~/InitiativeTracker-0.1.5-linux-arm64.tar.gz
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
