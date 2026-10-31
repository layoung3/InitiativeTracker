# Optional Colophon boot splash investigation

This is a proposal, not an installer. N9 does not edit boot files, initramfs, Plymouth, firmware, LightDM or Labwc configuration on either working Pi. Application loading screens and local kiosk waiting screens start after the graphical session; they cannot replace earlier Linux or firmware branding.

## Separate boot stages

| Stage | Candidate | Limits |
| --- | --- | --- |
| Firmware/EEPROM diagnostics and display initialization | Preserve current firmware and diagnostics | Not all output is replaceable. A Linux theme does not replace firmware diagnostics or guarantee a seamless screen. |
| Early Linux splash | Raspberry Pi OS `rpi-splash-screen-support`, where supported by the exact installed kernel/OS | Current Pi documentation describes `configure-splash`, kernel command-line changes and initramfs contents. Requires an uncompressed, 24-bit, no-alpha TGA, at most 1920×1080 and 224 colors. Artwork is centered/clipped rather than scaled. Verify kernel/package availability on the disposable card first. |
| OS startup | Distribution-integrated Plymouth theme containing the supplied artwork | Depends on DRM/KMS, monitor, initramfs and display handoff. Preserve password/error prompts and access to diagnostics. Escape can reveal boot messages. |
| Desktop/app startup | N9 local waiting dialog/page, then existing updater, then Chromium | Does not modify firmware or Linux boot branding. Preserve selected desktop home and persistent Chromium profiles. |

Sources checked 2026-10-31: [Raspberry Pi boot/splash configuration](https://www.raspberrypi.com/documentation/computers/configuration.html#configure-splash-screens), [Plymouth architecture](https://wiki.freedesktop.org/www/Software/Plymouth/). These describe capabilities; they do not establish compatibility with the owner's existing cards.

## Approval required before any OS-level trial

Prepare a specific change manifest for the selected Pi model, OS/kernel, boot-media layout, graphics stack and monitor. Include exact package versions, before/after boot files and initramfs hashes, selected Plymouth theme, artwork provenance/conversion, diagnostics access and a verified rollback. Do not use the supplied PNG directly where TGA is required, and do not replace it with another phoenix. Raster conversion is a later approved asset-production step. Do not add package installs or boot edits to ordinary app releases.

The owner currently has only two working Pis and no spare cards. A later trial needs **two disposable microSD cards**, a reader, private image/backup storage, local recovery access and explicit installation/card-swap approval. No extra Pis are required, and no purchase is required now. Inspect boot-order/storage configuration first; a card swap is not isolation if the Pi still boots from or writes another device.

1. Record baseline 0.1.5 health, pairing, encounter synchronization and desktop/updater behavior. Shut down and remove power fully.
2. Label and store each original card. Keep original media disconnected. Boot only the disposable installation after its boot-media isolation has been checked. Do not update EEPROM/firmware or write attached production storage.
3. Establish normal desktop/updater/kiosk behavior before adding a splash. Apply only the separately approved OS manifest. Test cold boot, no network, delayed service, failed service, failed update/rollback, monitor absence/reconnect, rotation/resolution, console/error/password visibility and touch behavior. Keep logs and distinguish real observations from simulations.
4. Test removal of the theme and restore exact boot files/initramfs on the disposable card; verify ordinary diagnostic startup and graphical updater sequencing again.
5. Shut down, remove power, restore the original card and original boot-media arrangement. Power on and check unchanged 0.1.5 health, pairing and encounters. A card restore cannot undo EEPROM or other external state changes, which is why those changes are excluded.

No current physical splash compatibility, flicker-free boot or firmware-logo replacement is claimed.
