# R4 — Session Trace history: HOLD is not STOP

Date: 2026-10-30. Field feedback: Control Case Open Source Frame showed unavailable for actual negative ACTCON; Event Open Frame previously replaced the entire Trace with one pinned row; HOLD erroneously stopped updating the WPF list and a 1200-row ring evicted old frames.

## Operator contract

- **Live Frame Trace**: Continue receiving and adding rows incrementally when auto-follow is OFF. HOLD stops automatic scrolling, not RX, decoding, logging or Trace update. WPF only displays 1200 recent rows.
- **Event / Controls / GI → Open (Source) Frame**: Parse the exact physical source reference (protocol+stream+byte offset), use a binary-indexed temporary session history on disk, show a bounded **chronological window** of both SCADA TX and RTU RX frames around the selected frame, select the correct frame and display the existing Frame Properties/HEX. Never use a same-IOA substitute. Link A/B remain independent. Holding a historical window is intentional: incoming live traffic continues to append to the session history; Resume returns to latest live Trace.
- **Older / Newer** appears in Frame Trace only in Hold/history mode. Each click reads another short page via on-disk binary search, rather than creating tens of thousands of WPF objects.
- **File lifecycle**: temporary history exists for live RX even when full recording is unchecked. It is session-scoped, inside the operating system temp directory under ARIEC60870/TraceSessions. A new monitoring session, opening a raw replay, or closing the application deletes the old owned cache. A crash may leave an orphan cache; the next run opportunistically removes caches older than 24h. No user-visible permanent session archive is created unless full recording was enabled separately.
- **Privacy and evidence**: temp history contains raw captured serial bytes and is deleted under normal lifecycle. It is a navigation cache, NOT a verified raw journal and NOT a signature. Full raw recording is still explicitly opt-in for durable replay/FAT evidence. The UI must not describe live-only monitoring as creating *no files*, since the temporary cache is present.
- **Limits**: frame index is written before the lossy WPF preview channel, one buffered sequential writer and small fixed-size per-source indexes. The historical reader reads a bounded per-source time neighborhood via binary search, merges by estimated host time (NOT guaranteed cross-port wire order), and only builds ~161 WPF objects. No per-frame dispatcher disk I/O. No active IEC-101/103/104 transmit path changes.
- **Failure semantics**: if temporary disk fails, engine must report failure/degraded evidence explicitly rather than silently claim complete. Trace can still operate from its bounded RAM view when history is not present. Full recording retains its independent manifest, integrity checks and raw journals.

## Regression and field FAT

- 3600 interleaved physical frames (more than WPF 1200), exact source+offset recovery with SCADA request/RTU reply context.
- Four physical sources A/B, round trip original exact HEX/checksum/link width and strict incorrect-offset rejection.
- Dispose removes session folder; new sessions never reuse old source identities.
- Existing WPF passive-gui-smoke verifies Start/Stop and Event navigation. Controls/GI now go through the same strict session locator rather than searching only a GUI ring. Test 2/4 COM on actual hardware for RX direction, live HOLD and history browsing with continuous high-rate Class 2 polling.
