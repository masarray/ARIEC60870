# Responsive Layout Policy

ARIEC60870 desktop UI must feel like the same mature engineering software family as the desktop product direction: compact, calm, mature, workspace-first, and readable under real polling load.

## Layout principles

- Keep the main workspace dominant. The setup sidebar must be useful but not consume too much horizontal space.
- Prefer fixed-size summary components for high-frequency values. Do not let live protocol messages resize the page.
- Top header cards must use fixed heights and text trimming. Long status text belongs in Session Notes or Diagnostics, not in an Auto-sized header.
- Large tables must scroll instead of stretching the layout.
- Every large DataGrid must use virtualization, recycling, and explicit horizontal/vertical scrollbars.
- Raw frame trace is an expert layer. Operator Evidence, Value Viewer, and Event Log must remain the primary user-facing views.

## Session State rule

The header Session State card must show stable session phase only:

```text
Idle / Running / Stopping / Stopped / Completed / Faulted / Attention
```

Do not update the header on every incoming frame. Per-frame master states such as `NormalClass2Polling`, `Class1EventDrain`, `ResetFcb`, `GiFollowUp`, or `ReadWaiting` belong inside Operator Evidence and Frame Trace. Updating the header with those fast-changing states causes WPF Auto layout jitter and makes the app look unstable.

## Data density

ARIEC60870 is an engineering tool. It must show many rows without becoming cramped or noisy:

- data grid row height should stay compact but readable
- avoid giant typography inside the app
- use restrained blue accent only for navigation/action states
- keep shadows soft and subtle
- avoid glowing, oversized, or SaaS-template visuals

## Performance

Layout and rendering must not become the bottleneck during polling:

- batch UI updates
- keep visible collections bounded
- keep Value Viewer as a snapshot
- keep Event Log edge/state-change oriented
- keep raw trace in a rolling visible buffer
- keep full counters independent from visible-row retention

## Current v1.2.3 fixes

- Reduced outer margins and sidebar width.
- Stabilized top Session State card with fixed height and trimmed text.
- Stopped per-frame header state mutation.
- Added explicit scrollbars to all main DataGrids.
- Tightened mature engineering product-style card, tab, row hover, and typography styling.

## v1.2.24 cockpit polish and interaction audit

- Setup ComboBox controls must use a WPF-safe template with a real dropdown toggle bound to `IsDropDownOpen`; the full field should be clickable.
- Header should remain a slim shell with compact chips and short buffer text. Do not place long, high-frequency buffer diagnostics in the visible header.
- Segmented navigation should use one precise sliding pill and avoid duplicate selection animations from both click and tab selection handlers.
- Rail controls should keep icon + caption affordance, safe hover margins, and restrained shadows. Avoid scale-heavy hover animations that make polling views feel laggy.
- Typography remains Aptos-only with ClearType rendering and normal weight by default.

## v1.2.25 product UI theme and custom icon port

- WPF shell should use the desktop product font stack: `Aptos, Segoe UI Variable Text, Segoe UI, Calibri`, with Aptos as the primary face.
- Primary accent follows the approved blue accent family (`#2563EB` / `#1D4ED8`) for navigation pills and primary actions.
- Rail icons may use local WPF `Geometry` resources derived from Lucide-style outline icons, provided attribution remains in `NOTICE` and `THIRD_PARTY_NOTICES.md`.
- Do not replace the approved command rail icon set with heavier, custom, or visually noisy geometry unless the full command rail is re-audited.

## v1.2.26 command toggle, status history, and scrollbar usability

- Connect and Disconnect are mutually exclusive rail states: Connect uses the blue primary rail style and Disconnect uses a red danger rail style.
- Primary rail hover must keep icon and caption readable; if hover background becomes light, foreground must switch to dark ink.
- Long session details belong in a bottom status history panel with a circle-chevron collapse/expand control, not in the compact header session chip.
- Scrollbar thumbs must keep a practical minimum size under high row counts so they remain clickable handles instead of shrinking into tiny dots.
- Export actions should use icon + caption buttons with enough fixed width for the caption; do not let command text clip in compact navbar areas.
- Left command rail spans the full shell height and must not be shortened by the bottom status history panel.
- Status history should be a compact read-only grid with time/status/detail columns, not a plain text wall.
- Connect and Disconnect remain separate rail buttons with interlocked enabled states; do not hide one in a way that shifts command layout.
- The app logo should use the approved stacked `IEC / 103` local image asset, packaged as a WPF resource, instead of text-only branding in the rail header.


## v1.2.5 layout/diagnostics note

- Keep operator views readable first; raw hex stays in Frame Trace and Inspector.
- Wide protocol columns, horizontal scroll, ellipsis, and tooltip are preferred over visually clipped text.
- Serial close/dispose driver exceptions must be converted into Diagnostics evidence, not allowed to interrupt Stop/Close workflow.

## v1.2.27 icon and logo regression guardrail

- Keep command rail icons on the Lucide-style monoline resource set unless a replacement is reviewed at actual 18–22 px rail size.
- Do not swap the app logo to a large poster-like image; the rail logo must remain compact, readable, and calm inside a 68 px card.
- Custom protocol geometries are allowed for diagrams or empty states, not for the primary command rail unless they are visually simpler than the current Lucide set.

## Software Icon / Rail Logo Identity Lock
- Current approved software identity is a compact `IEC / 103` stacked mark.
- Approved palette: near-black/ink, white typography, and restrained application blue accent (`#2563EB` / `#1D4ED8`).
- Do not replace the rail logo with decorative protocol symbols or dense custom drawings.
- Do not replace clean Lucide-style command icons with custom geometry icons unless the replacement is visually reviewed at 68 px rail size and 32 px taskbar size.
- The WPF window logo should use `Assets/Icons/ariec60870-icon.png`; the executable icon should use `Assets\Icons\ariec60870-app.ico`.

