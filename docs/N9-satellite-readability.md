# Satellite display readability for 0.1.6

The `/display` page now uses a compact application layout at widths below 768 CSS
pixels, and on short landscape viewports up to 1100 wide and 650 high. The second
rule includes assumed 7-inch panels at 800×480 and 1024×600. These are test
viewports, not a claim about the installed panel's resolution, scaling or usable
browser area. No kiosk command, zoom, resolution, startup file or Pi was changed.

## What changes

- Compact names are 24 CSS px, initiative values 28 px, and round/turn values
  32 px. The header is 64 px rather than the 100 px desktop header. Fullscreen
  and appearance controls keep 44×44 CSS px touch targets.
- Initiative values already supplied by the public encounter projection now
  appear in the existing turn-indicator column. The current row has a chevron,
  accent rail, stronger border and an accessible current-turn marker.
- Names wrap at spaces and, when necessary, inside an unbroken name. Text is
  never reduced to fit. Exhaustion follows the name; conditions remain beneath
  it. The established HP column and narrow-phone placement remain separate.
- Compact badges use the display's foreground and surface colors for contrast
  across all eight themes. Portraits remain visible at 40×40 CSS px in compact
  mode; the existing larger-screen portrait size is retained.
- The connection state has its own compact footer, so it cannot cover list text.
  Only the bounded list scrolls. Shared HTTPS authorization navigation consumes
  space inside the viewport rather than pushing the display beyond its bottom.
- Initial Razor rows include the same initiative, exhaustion and condition
  information as live rows. SignalR, pairing, public visibility projections,
  portrait reuse, theme authority and gameplay state are unchanged.

Desktop pages retain their header, spacing, typography, HP placement, themes and
one-line name ellipsis. Initiative values and the chevron are the intentional
additions to that existing design.

## Repeatable verification

Use a fresh disposable loopback application, never a working hub. The browser
suite creates pairing requests, changes themes and replaces the encounter.
Run it separately **after** the existing shared-host browser suite:

```bash
# With .NET 10, Node, Playwright 1.58.2 and its Chromium available:
dotnet test -c Release
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build -c Release \
  --no-launch-profile --project InitiativeTracker.Web \
  --Kestrel:Endpoints:LanHttp:Url=http://127.0.0.1:5189 \
  --Tracker:DataDirectory=/tmp/satellite-readability-disposable \
  --Logging:LogLevel:Default=Warning
```

In a second terminal after `/system/health` responds, with Playwright on
`NODE_PATH` if installed outside the repository:

```bash
N9_APP_URL=http://127.0.0.1:5189 node --test scripts/tests/*.test.cjs
N9_APP_URL=http://127.0.0.1:5189 \
  SATELLITE_SCREENSHOTS=/tmp/satellite-display-evidence \
  node --test scripts/tests/satellite-ui.integration.cjs
git rev-parse HEAD > /tmp/satellite-display-evidence/source-commit.txt
```

Stop only this disposable app process when finished. Choose a new data directory
for another cumulative run; the existing media tests expect a fresh encounter.
Without `N9_APP_URL`, the six synthetic layout cases run and the real application
case explicitly skips. CI supplies the URL, so that case must pass there.

The matrix is 800×480, 1024×600, 480×800, 600×1024, 1280×800 and 1920×1080.
Assertions cover document/list overflow; each wrapped text line's bounds; text
size and primary contrast; name/portrait/initiative/HP/condition separation;
empty, 3-, 8- and 24-entry orders; long spaced names and 72-character unbroken
names; condition/exhaustion badges; hidden NPC/HP projections; initial no-JS
markup; active rows deep in the order; touch scrolling and controls; portrait
node retention; synchronized themes; and pairing revocation. Existing tests also
exercise 40-turn scrolling with normal and reduced motion.

For before/after screenshots, export `initiative-display.css` and
`initiative-display.js` from verified starting master
`3b596aa63657dce7b0fda3e2f242e6e8a32be444` into a local directory and set
`SATELLITE_BASELINE_DIRECTORY` to it. The suite replays those two assets against
exactly the same disposable encounter. This is a rendering comparison, not an
execution or deployment of an old application. Desktop comparisons also assert
unchanged header bounds and name font sizes. The normal CI suite tests the
candidate alone, without relying on historical assets.

## Visual evidence

The N9 workflow uploads all generated screenshots, JSON measurements and the
exact tested `source-commit.txt` to the `n9-integrated-evidence` artifact. Its
`satellite-display-evidence` directory contains `actual-*` paired-display images,
`fixture-*` crowded-layout images, per-size metrics and the actual-view receipt.
The artifact is attached to the workflow run for the PR head commit.
Representative reviewed screenshots are also committed in
[evidence/satellite-readability](evidence/satellite-readability/README.md).

The paired fixture includes a short player-style name, a long name with private
HP, a 72-character unbroken name, a condition badge, temporary HP and 21 skeletons.
Counts of fully visible rows depend on content; wrapping deliberately trades
some row density for complete readable names. The loopback-only **Hub Setup**
button visible in actual fixture screenshots is existing local-hub recovery UI;
it is not shown on a remote satellite.

## Physical acceptance still required

On a separately approved physical 7-inch satellite, verify the real CSS viewport
and device pixel ratio, the chosen portrait/landscape orientation, text at the
normal table viewing distance, panel brightness/glare and theme contrast,
portrait clarity, touch scrolling and the two 44 px controls, fullscreen and
appearance-panel behavior, and active-turn recognition during rapid real updates.
No browser zoom or resolution change is required by this work.

Exceptionally long names and many condition badges produce taller rows; an
unbounded name or condition list can exceed one screen. The active row scrolls
into the list region, but physical acceptance should use the table's actual
names and conditions. Larger displays retain their existing ellipsis behavior.
This work does not approve a release, installation, kiosk change or deployment.
