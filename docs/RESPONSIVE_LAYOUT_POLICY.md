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

## 2026-10-29 Trace/Events observation usability (UX-01)

- Every physical Trace row must visibly show host-local TX/RX time at millisecond precision (`HH:mm:ss.fff`) next to a strong direction icon. This is a Windows/adapter receive or transmit observation time, **not** a calibrated wire-edge timestamp or proof of RTU response latency.
- Decode the selected frame into a stable, virtualized **Properties** grid: label/value columns, subtle alternating row background, normal readable font, Type ID plus engineering name, COT and P/N/TEST flags, CA/IOA, link address/function, quality, RTU time, integrity and multi-object details. Hover must never mutate the inspector's selected-frame data or continually rewrite its layout.
- Main Values / Events / Trace / Findings / Report navigation buttons are primary observation workspaces, not miniature metadata chips; target 14 pt-equivalent WPF font and >=40 px-height touch/click targets.
- Auto-follow is a **per-workspace operator decision**. Trace header toggle shares the established bounded Trace Hold/Resume state with the left rail. Wheel or manual selection disables Trace follow immediately; incoming data still flows to authoritative bounded stores. Events header toggle is independent; wheel in Events turns auto-follow off, freezes **only** its visible SOE grid while its bounded event store keeps advancing, and one explicit Resume rebuilds from the newest current snapshot. Never falsely claim that a held historical viewport is live.
- Use the approved solid up/down arrow geometry in individual Trace rows, and the down-into-lines follow icon in both Events and Trace headers. Make toggled state visibly different without a noisy animation or full-panel color change.
- Manual GUI FAT must inspect Trace and Events at 100%, 125% and 150% Windows DPI, scroll-wheel behavior under live traffic, stable inspector without hover flicker, narrow window/wrapping, multi-select clipboard and all existing raw capture/replay paths. CI compile success is not a visual FAT certificate.

## 2026-10-29 Premium readability revision

- Default UI typography uses **embedded, verified, SIL-OFL Inter** from a bundled WPF resource; only original raw HEX and literal byte dumps use the existing monospaced stack. Download/verification is an explicit version-pinned build step, not a runtime font dependency. License stays accessible in packaged output.
- Trace arrow icons reduced from 24 to 16 WPF px; TX/RX and host timestamp are the same Inter size and baseline, with consistent weight. Never use a monospaced font for time labels just because they contain digits.
- The Follow button on Trace and Events is a small **resume-only** affordance: it is invisible while auto-follow is ON and visible after wheel/selection Hold. Trace and Events continue accumulating separately in bounded source stores.
- Data surfaces are soft mist/slate blue-gray instead of pervasive pure white, keeping high legibility, restrained accent blue/green, and low contrast fatigue. Avoid saturated full-panel backgrounds, oversized toolbar icons or decorative animations.
- Properties inspector omits repetitive routine COT/quality bit rows and duplicate TI numbers; the raw typed bits remain available through tooltips and original capture.
- Event Log foreground shows one semantically explained Value field, removes Before/After and repeated reason from the grid, but keeps original before/after/reason evidence for report and export. Newest visible SOE is at bottom for standard follow-to-end behavior.



## R3-D — Compact GI workspace and Event evidence actions (2026-10-30)
The primary navigation includes a short **GI** segment adjacent to Controls inside its existing horizontally scrollable segmented bar, never an Auto-sized frame-status header. GI conversation and stage DataGrids use virtualization and recycling; they refresh from the shared 75ms UI flush **only while selected and dirty**. Event top toolbar provides small secondary **Open Frame** / **Open Case** actions, without a new side dock. Narrow windows may scroll the navigation segments horizontally. Source navigation temporarily holds only Trace display, not transport, data receipt, or raw journal. Test manually at Windows DPI 100/125/150%, smaller windows, selection persistence and long capture; compilation alone does not prove visual DPI correctness.

## OBS-P0 Stage A — stable selection, not Stage B visual redesign
Tab changes do not invalidate source-qualified Trace selection or session-history/archive scope; only explicit Resume, new session, Clear do. Keyed case updates preserve selected CaseId/phase and minimize DataGrid replacements; counter labels reflect the bound current view after source changes. Stage B, tracked in #68, will consolidate search and command dock design. The present milestone intentionally does not change XAML geometry or protocol engines.

## OBS-P0 Stage B — compact observation-first workspace (2026-10-31)
Primary workflow copies the **message-list then inspect-selected-message** principle of mature IEC field tools, not any third-party software implementation or visual assets. Trace provides exactly one visible search surface at a time: in-window Quick Find (type/COT/address/HEX + service filter) or verified portable `.ariec` scan (paged, cancelable, integrity status). The engine/storage backends and exact physical frame references remain separate. Operator can hide/rescale Trace inspector without discarding decoded selected evidence. A deliberate 'Commands' navigation action is the only way to reveal the command dock; on first migration from the pre-OBS default, do not permanently reserve command space on Values/Events/GI/Trace. Preserve user-explicit preferences saved by OBS. Passive RX-only offers no command control. No new per-frame timers, animations, network operations, vendor IP, decoder or transport state. Primary grid columns emphasize editable/user-mapped signal, value/quality, time and evidence meaning. Full GI coverage uncertainty, command ambiguity and original frame IDs remain in selected detail and evidence export. Stage C must still visually verify all common resolutions and DPI 100/125/150.

## OBS-P0 Stage C automated acceptance harness (2026-10-31)
`--observation-ui-acceptance --observation-out <path>` renders the actual WPF MainWindow (not a web/image mock) and captures 1540×960, 1366×768, 1024×576 and nominal 911×512 **logical DIP** viewports, plus Values/Events/Controls/GI at 1366×768. It writes measured client/grid/inspector/command widths and actual Windows runner DPI scale to `metrics.json` alongside lossless PNG images. Distinguish **nominal small-DIP viewport** from Windows DPI 150%: writing a screenshot at higher bitmap DPI does not emulate OS scaling or human perception. CI validates control visibility, 0px passive command dock, minimum usable Trace, one active search surface, stability of selected historical source and exact frame identity, and no live Trace Reset during synthetic 1,400-frame rolling update. No code may claim actual 100/125/150% OS scaling acceptance without an independent display session set to those scalings. Screenshot aesthetics, COT/quality truncation, high contrast and Windows font rendering require human review; actual 1200baud 2/4 COM timing and long-duration soak are still hardware acceptance steps.

## OBS-P0 follow-up — truthful CI screenshot dimensions and SOE tab activation (2026-10-31)

CI bitmap names encode a **requested** logical window size, but Windows hosted-runner work-area constraints can prevent that requested Window.Width/Height from becoming its actual WPF dimensions. The acceptance JSON must record actual **window** and **client** sizes, an explicit `requestedWindowSizeApplied` flag per screenshot, and a top-level list of constrained names. A screenshot requested at 1540×960 but actually rendered near a 1024-wide desktop is **not** evidence of 1540×960 layout correctness. Do not mark physical Windows DPI acceptance, independent visual review, or 1200-baud hardware FAT as passed from these CI images.

When returning to Events with auto-follow enabled, schedule one scroll to the latest **bound** event after the tab is loaded; an offscreen ScrollIntoView call is not guaranteed to realize a row. Never force auto-follow while the operator manually holds/scans Events. Keep the Event counter tied to its currently bound WPF rows, not the proposed filtered snapshot before reconciliation. Acceptance must compare the event source, bound item count, and the actual realized final DataGridRow when an event exists. Empty Events/Values/GI/Controls screenshots without representative traffic remain layout evidence only, never evidence that meaningful decoded rows or verdicts are legible.

## OBS-V2 interpretive inspector

Decoded points for the selected IEC-101/104 ASDU appear within the existing resizable/collapsible Trace inspector; the UI does not allocate a permanently open sidecar or show oversized cards. The point table has a fixed compact height and recycling, and can be scrolled independently of its parent frame/property list. The explicit **Show details** control still overrides responsive auto-collapse on narrow screens. Acceptance screenshots must include meaningful point content as well as bounded Trace/selection/count checks; named 1540/1366 screenshots on a constrained hosted runner are not by themselves evidence of those physical resolutions or 125%/150% OS DPI.

## POINT-WIRE-01 point investigation and responsive GUI

The existing Values grid has a compact search/filter line and one-line selected-point footer, plus an Open Frame button. Neither an extra dialog nor a permanently open secondary DataGrid is added. The filtered view is not a new capture scope; point source keys and frame evidence survive sorting, tab switches and live updates. On an explicit Open Frame action the Trace inspector may open on narrow windows to show the exact selected IOA; the operator may collapse it afterward. Hosted CI synthetic screenshots cannot certify real monitor DPI.

## UX-GRID-01 compact toolbar / state chip design contract

At constrained 1004-DIP and 1366-wide operator workspaces, the grid receives the horizontal/vertical priority. Search stays on one slim row; Events and Trace retain independently controlled AUTO follow. Value-filter, Event-filter, Trace-scope, Open Frame, Open Case and archive/detail actions are available via right-click; they are not separate headers or permanently large action buttons. The hidden Value selected-point footer is preserved for automation/context state but removes zero-space from the visible grid. Visible filtering is declared by count + scope. Use modest chip padding and mutually distinguishable ON/OFF backgrounds, without using red/green alone as the only textual distinction. Keep a clear fallback text for analog, unsupported or uncertain states; no semantic guessing.

Grid virtualized cells use one consistent no-wrap text and declarative style triggers rather than per-frame UI commands or WPF converters. Do not assert physical DPI/COM performance from CI screenshot names.

### Visible AUTO state

The shared `EvidenceFollowToggle` style must **never** collapse itself when checked. Follow ON uses selected accent background, follow OFF stays visible with a neutral border. Trace and Events display the control in their compact search-first chrome; Events includes a small `AUTO` label. A disappearing follow button previously made the most important observation control inaccessible whenever it was enabled. The Windows rendered-WPF acceptance checks actual `IsVisible`, not only the logical `Visibility` enum.

## UX-HEADER-ALIGN-02 — one 32-DIP line, no second Trace header row (2026-10-31)

**Operator acceptance rule:** Search, filtered count and AUTO follow must share the same center Y coordinate (±3 DIP) in every visible Trace and Events workspace. Values Search/count uses the same 32-DIP header height, with no fake AUTO control on a point snapshot. All three align along left responsive search (maximum 242 DIP), text count, and right follow when applicable. Trace's former independent "Frame timeline + AUTO" row was removed; the redundant frame caption is retained only as counter tooltip and updated by the existing exact-history state machine. Trace current-window search no longer uses a fixed 270px border inside a horizontal ScrollViewer (which clipped on narrow view); instead the header grid provides star sizing without horizontal scrolling. Session-history paging remains conditionally accessible; separate archive search can expand only when explicitly chosen.

The Windows WPF acceptance must inspect actual rendered search/count/AUTO Y centers and top-of-grid distance after layout at all viewport requests (including the CI runner's constrained real work area), rejecting a second row or invisible follow control. No extra timer, animation, receive parser or protocol-state changes.

**History-width negative case:** Older/Newer navigation is not allowed to consume any of the 32-DIP live toolbar width, even when the trace inspector is open and the trace pane reaches its 300-DIP minimum. Both named history buttons are retained in a permanently collapsed host for backward compatibility; source-scoped Older/Newer actions are exposed on the Trace right-click context menu only during an active historical window. The existing actual-WPF session-history acceptance checks the menu items' visibility and exact-frame navigation. Returning live hides history-only actions again. Search, count and AUTO retain one baseline in either mode.

## UX-CASE-03 — Controls/GI compact case investigation cockpit (2026-10-31)

Controls and GI case workspaces now have the **same single 32-DIP grid-first Search + counter baseline** as Frame Trace, Values and Events. No two-level title/action bars. Actions to open the selected source frame and copy the full provenance-preserving case are available from right-click on either the case grid or stage grid, and via double-click to open selected stage. DataGrid columns retain original evidence fields and use compact widths / meaningful star-sizing, with stage rows and selected detail kept. One 24-DIP details footer is bounded so multiline status text cannot unexpectedly shrink case/stage lists.

Windows acceptance verifies actual WPF Search/counter center-Y within 3 DIP, Search flush-left to case grid within 3 DIP, both stage/case panes usable, and context actions in the real controls. No UI timers, protocol byte decoder rewrites, or speculative state interpretation.

## R6 UX-02 — 2026-10-31

Values aligns its first grid with Events/Trace at a 6-DIP toolbar gap. The Events category scope is visible on the same 32-DIP toolbar. Shared outlined search and X geometries replace heavy glyphs across five search surfaces; clear X appears only with a query. Live evidence-row tooltip overlays are suppressed; selected inspector omits redundant Trace summary. Central calm blue-grey contrast tokens are tuned. CI screenshots must be inspected for constrained runner width; physical 150%-DPI and real electrical FAT are separate.

