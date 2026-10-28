# Live Trace recovery and IEC-101 interpretive line monitor — 2026-10-29

## Problem reproduced in screenshots
Active IEC-101 TX/RX counters and LED traffic are live, but the Trace workspace remains visually blank until changing tabs. The old UI retained Hold/selected-row freeze state across connections; first-batch range notifications to the still-mounted virtualized WPF ListBox could leave the initially cleared viewport unrealized. In addition, Balanced verbosity *silently omitted* routine polling when the Trace workspace was not active.

## Corrected architecture
- **PrepareLiveTraceForConnect** resets selection, Hold and deferred view flags only at connection start (active, dual-link and passive), never on every frame.
- Exactly one dispatcher-priority initial evidence flush after Connect. Continuous traffic remains handled by the existing bounded 75ms timer, adaptive processing budget and deferred UI batch updates, without a per-frame `Dispatcher.Invoke`.
- The first Trace batch is materialized atomically from the authoritative 1200-row bounded ring and schedules one deferred layout invalidation. Subsequent batches append/trim only. Switching workspaces still snapshots that same ring; there is no separate tab-dependent decoder or storage.
- **Every TX/RX raw frame** is retained in the bounded Trace store regardless of which workspace is visible. Backpressure protection remains separately counted and does not silently discard raw bytes from the capture journal.
- Physical poll/reply interpretation uses the existing typed FT1.2 and ASDU decode; no regex/string guess of PRM, ACD, DFC or COT. IEC-101 Class 2 FC11 request, secondary FC9 no-data, FC8 user data with type/COT/CA/IOA/value/quality/time, link ACK/NACK and link reset are explained as engineer-readable lines. E5 remains contextual: never inferred as physical command success.
- Compact TX ➤ and RX ◀ markers; raw hex and selected-byte Inspector remain directly below/alongside the line. No heavy graphic controls/animations, wide grid columns, or per-frame WPF layout refreshes.

## FAT acceptance
1. Start IEC-101 Active Master on COM at 1200 bps with Trace already selected; first Reset Link TX and RX ACK must display **without switching tabs**.
2. Keep Trace selected during GI, FC10/11 polls and FC8/9 replies; every raw frame has a direction, protocol meaning and selectable original HEX.
3. Switch to Values/Events while polling, then back to Trace: ring snapshot must show recent traffic from the inactive period.
4. In Trace select one row: deliberate Hold preserves inspection; choose Auto or new Connect to resume live. Prior session Hold must never freeze the next session.
5. FC9 with ACD=1/DFC=0 prints that requested class has no data but Class 1 pending. Never conflate it with RTU application error.
6. Run same capture alongside ASE2000 V2 and compare FC, COT, CA, IOA, quality and CP56Time2a. Verify linked selected raw field highlights and command lifecycle separately.
7. Run stress 4-COM passive and long duration Windows integrated sessions; verify no unbounded RAM/GUI repaint. CI does not substitute real-device FAT.

## Scope
This milestone corrects initial WPF Trace rendering and per-frame human-readable decoding for IEC-101. It does not redesign the rest of the GUI, implement a complete bidirectional byte-field highlight for every IEC-104/103 frame, or claim full ASE2000 V2 parity yet.
