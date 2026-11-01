# Updater archive integrity investigation

The reported failure was reproduced from exact source commit
`7f940bbb7ccf04cc12035143ed9d733f265b6879`, the current `master` when this
investigation began. No Raspberry Pi, production files, certificates or services
were accessed. All inspection/upgrade/rollback exercises use disposable fixtures.

## Cause and exact reproduction

The six stored Git blobs match `provenance.json` and `runtime-manifest.json`
exactly. They contain LF line endings, no CRLF and no UTF-8 BOM. At this commit,
the only applicable attribute is `* text=auto`; there are no updater-specific
`eol`, `filter`, `export-subst` or `export-ignore` attributes. Git history has not
invalidated the recorded original-script hashes.

`git archive` applies checkout line-ending conversion. With `core.autocrlf=true`
(common on Windows), all six exported scripts become CRLF. This is an **export
conversion**, not normalization of the committed source or a bad manifest. A
Linux export with `core.autocrlf=false` and `core.eol=lf` matches every pin. These
commands reproduce both results on Linux too; no Windows text pipeline is needed:

```bash
source_sha=7f940bbb7ccf04cc12035143ed9d733f265b6879
git -c core.autocrlf=true archive --format=tar --output=old-crlf.tar "$source_sha" hub-updater
git -c core.autocrlf=false -c core.eol=lf archive --format=tar --output=old-lf.tar "$source_sha" hub-updater
```

Use the corrected checkout's `python3 -I hub-updater/verify-archive.py` with either
tar path to compare entries without extraction: `old-crlf.tar` fails and
`old-lf.tar` passes. The archive verifier did not exist in the historical commit.
The existing-hub inspector's `candidates()` also rejects the extracted CRLF
originals with `Original source provenance mismatch`, before inspecting installed
targets. The same extracted LF bundle passes in a fake existing-hub root.

The table records **LF Git blob/expected manifest hash** and the **actual CRLF
archive hash**. Bytes are LF → CRLF; the final column is the number of converted
LF characters (and resulting CRLF pairs). Every CRLF export equals exactly
`blob.replace(b'\n', b'\r\n')`; there are no other byte changes or BOMs.

| Variant / script | Expected SHA-256 (stored LF bytes) | Actual SHA-256 (`core.autocrlf=true`) | Bytes | Lines |
| --- | --- | --- | --- | --- |
| original / installer | `17bfda06c20f3d8b932d7f7f1bf341ed17107e32c602586f9680e463d9542c56` | `b182cf9058c0b1dcf5a0d71574cd9bfaee39df3b46a163247ebe738a0b9bcc52` | 13993 → 14628 | 635 |
| original / checker | `16e57846ace608c5e81d394107612d7d34100e82c06ef23e39b3aa409d607d4d` | `d0ae2094d63f3da50a569b8f2d0ae804d1e461c1b90d1ad045f9eee19e084cf4` | 7214 → 7554 | 340 |
| original / launcher | `4bd743792f68b6041fb80e5c83cb377231bce743d5194115db146bbcb8b2c44a` | `7a0238177671f8512a83fad1c7483be985d2d3fabd1e3be6e63104f78bc9c01d` | 974 → 1022 | 48 |
| runtime / installer | `c9f1dd83f896726aaf7107b78056393bad47e8bb2c8d38e565e7a60ff685d302` | `e838b8c594363bcf59f1fa232db368a0aa085fc2695dbedfddfbed97180bc59a` | 18517 → 19218 | 701 |
| runtime / checker | `1e1727161780b6caf381facd63de9de5d3d2ed5370a6a1ba1b53cb3509cfb15c` | `040da997537cc7f689d8bc1c03961d2907a0be3f2bc9dd8739d7b6af87a813fd` | 7504 → 7851 | 347 |
| runtime / launcher | `eaa45101302625ac63da42f3da1e35e19798bae7f6aff9cd2fefdfce1335f67a` | `9834575df5b7792b1763e7cc5781489f05ddaf25e2fd367af75231301b7295c2` | 2870 → 2963 | 93 |

Here installer/checker/launcher abbreviate `initiativetracker-install-update`,
`initiativetracker-check-update` and `initiativetracker-hub-launch`. Matching
Windows/Linux **tar SHA-256** proves transfer integrity only: both machines can
receive the same incorrectly converted tar. No actual failed owner archive or
workstation Git configuration was supplied to this investigation, so attribution
of that specific archive to this reproduced conversion remains conditional on its
entry hashes matching the table. No further live-hub evidence is needed.

## Correction and acceptance coverage

`.gitattributes` now pins `hub-updater/** text eol=lf`. The documented export also
uses command-local `core.autocrlf=false` and `core.eol=lf` with binary `--output`.
Both defenses are tested. **All six source files and both manifests are unchanged**;
no checks are disabled, no alternate hashes accepted, and no normalized-hash
comparison is used. The verifier reads raw archive bytes. It does not execute
scripts, write/extract the bundle, or replace source approval with self-reported
hashes. Approve the exact source commit and record the tar hash independently.

Automated tests create tiny temporary Git repositories and cover:

- Clean checkouts and real tar exports under all six combinations of
  `core.autocrlf=false/true/input` and `core.eol=lf/crlf`, comparing every archived
  entry with its Git blob and both manifests. Windows CI runs real Git/PowerShell;
  Linux CI runs Git/Bash and the extracted inspector.
- Historical `text=auto` behavior reproducing all six CRLF mismatches; corrected
  LF pins rejecting changes to each of the six scripts or either manifest.
- Exact documented Bash and PowerShell export/verification commands, including
  binary output with native-command exit-code checks on Windows.
- Extracted six-script Bash syntax checks and extracted inspector candidate
  validation, followed by full inspection in a disposable fake 0.1.5 root/home.
- Component-only apply/no-op/rollback in that fake root, current-runtime receipts,
  original bytes/owners/modes, and unchanged configuration/autostart/app/data.
  Existing partial failure, rollback-fence and real-sudo regressions remain active.

The archived scripts are non-executable source files (Git's tar defaults may
produce 0664/0775 file/directory modes). The documented staging step copies them
to root-controlled 0644 files/0755 directories before privileged use. They are
syntax-checked with Bash and copied to installed 0755 only by the separately approved
component installer; this change does not make a tar extraction deploy anything.
No archive test executes the original scripts against a real system.

Follow [the corrected workstation and future Pi verification procedure](EXISTING-HUB-UPGRADE.md#2-prepare-a-sealed-reviewed-bundle).
Generating/verifying an archive does not authorize transfer, component replacement,
application release deployment or PR merge. The healthy 0.1.5 hub and already
issued production certificate require no changes for this investigation.
