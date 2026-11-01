# Reviewed satellite screenshots

These Chromium captures use the actual paired `/display` route on a disposable
loopback app. The before images replay the display CSS and JavaScript from master
`3b596aa63657dce7b0fda3e2f242e6e8a32be444` against the same encounter. No device was
accessed. The loopback Hub Setup button is existing fixture-only local recovery UI.

| Viewport | Before | Candidate |
| --- | --- | --- |
| 800×480 landscape | [Before](before-800x480-order.png) | [After](actual-800x480-order.png) |
| 1024×600 landscape | — | [After](actual-1024x600-order.png) |
| 480×800 portrait | — | [After](actual-480x800-order.png) |
| 600×1024 portrait | — | [After](actual-600x1024-order.png) |
| 1280×800 desktop | [Before](before-1280x800-order.png) | [After](actual-1280x800-order.png) |
| 1920×1080 desktop | — | [After](actual-1920x1080-order.png) |

Additional captures: [empty encounter](actual-800x480-empty.png),
[active turn 20](actual-800x480-active-20.png),
[light theme](actual-800x480-light.png),
[portrait names and exhaustion/condition badges](fixture-480x800-long-names.png),
and [initial server rendering without JavaScript](actual-800x480-initial-no-js.png).

Visual inspection checked complete wrapped names, readable initiative and round/
turn values, distinct active-row rail/border/chevron, separate HP and badges,
retained portraits, compact status footer, and unchanged desktop typography and
header. Exceptionally long names grow taller instead of becoming tiny or clipped.
The desktop screenshots retain the preexisting ellipsis behavior.

[Actual-view measurements](actual-receipt.json) record fully visible row counts
for this particular mixed 24-entry encounter; they are not a minimum count for
arbitrary content. The CI `n9-integrated-evidence` artifact contains the full
matrix, assertions, metrics and exact head `source-commit.txt` from its workflow
run. See [the validation procedure and physical limits](../../N9-satellite-readability.md).
