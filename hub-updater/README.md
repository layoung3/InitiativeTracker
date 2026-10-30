# Recovered graphical updater

`original/` preserves the three owner-supplied OS scripts byte-for-byte. None was
executed on the development host. `provenance.json` records exact hashes, sizes,
installed paths and the verified handoff origin. Library transport failed; the
parent supplied the complete source JSON and all reconstructed hashes matched.
These files contain no production config, credentials, databases or browser data.

`runtime/` is derived from those originals and installed by the new fresh-device
recipe in `provision.py`. The recipe is newly proposed, with narrow sudo tests;
the existing live Pi's effective sudo configuration remains separate evidence.
Review original-versus-runtime diffs directly. Architectural contracts remain:
Labwc/health/Zenity checker/zero-argument installer/health/Chromium, root-trusted
bare local `.manifest.json`, versioned releases, persistent data, whole-data
backup/rollback, and the existing Build/Test/Stage release format.

Targeted changes:

- Installer fixes PATH/umask to a readable release baseline, checks root-trusted
  config/staging ancestry and local manifest confinement, creates backup root
  0700 and archive under a narrowly scoped umask 077, then explicit 0600.
- All derived updater/checker Python snippets use `/usr/bin/python3 -I`, excluding
  the caller's CWD, user site and Python environment overrides from root imports.
  The privileged installer uses `/bin/bash -p`, ignoring inherited Bash startup
  files/functions. Real-sudo disposable regressions reproduce the old import
  flaw with a harmless UID marker and verify the hardened full update rejects
  hostile modules and deliberately retained Python/shell environment overrides.
- Failure cleanup restarts the old app after pre-activation errors, attempts
  rollback after post-activation errors, refuses data moves if stop fails, builds
  a complete restore tree before moving failed data, and refuses backup/data/link
  collisions. Failed trees are retained. This is not crash-consistent recovery
  after power loss/SIGKILL, nor a guarantee against storage/rollback failures.
- Checker verifies config owner/type/mode, reports install failure with nonzero
  status and truthful text. A failed update never by itself proves rollback.
- Hub launcher bounds each health wait and shows a local error on failure. The
  Labwc-owned supervisor restarts an exited launcher/Chromium while retaining the
  original persistent profile and exact update-before-browser ordering.

Production appsettings must remain non-secret because the original contract
copies release config at 0644. Wi-Fi credentials are private independent files.
No HTTPS update channel, package signing infrastructure, remote management,
renderer watchdog or satellite network following is introduced.

Run disposable tests and complete the physical
[installation/update/rollback checklist](../docs/setup/automated-installation.md)
before approving the draft for use on a new Pi.
