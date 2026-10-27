# IEC-101 Passive Monitor — P2A Offline Raw Journal Replay

Status: implementation scope, 2026-10-28; builds on merged P0/P1 and issue #19.

## Why this matters in the field

A technician should be able to capture a real SCADA↔RTU IEC-101 conversation, leave the site, and inspect the **exact same original binary bytes** in ARIEC60870 without connecting serial hardware.

Earlier builds could verify `stream-00.bin`/`stream-01.bin`, but **Open Cap** only supported an excerpt `.ariec` created from selected visible rows. That was inadequate for raw forensic investigation.

## Workflow: no new window or button

1. During live Passive Monitor, start **Record RX**, observe Trace/Events/Values; stop to finalize.
2. Copy the full session directory `ARIEC-PASSIVE-...` (including `manifest.json`, `stream-00.bin`, `stream-01.bin`). Keep original files unchanged.
3. Click existing **Open Cap**. Choose **Full passive raw capture (manifest.json)**, then select that manifest in the captured session directory.
4. Application selects the existing IEC-101 Passive Monitor profile if necessary, verifies the raw journal, reassembles both stream directions and decodes with the existing IEC-101 ASDU decoder; the **existing Frame Trace, Event Log and Values** display the playback results. No COM ports are opened and no commands are sent.
5. Original `.ariec` selected-frame capture import remains supported. **Save View** is still an excerpt; it is not the complete raw journal.

The two stream source offsets and logical directions remain stable. Frames are merged by the host-side monotonic receive timestamps stored in the journals. This does **not** prove the exact on-wire bit ordering between different USB COM devices. Invalid checksums are displayed without updating point values.

## Integrity/architecture guarantees

- Engine `ARIEC60870.Monitor.PassiveJournalReplay` reads authoritative **original binary** records and never trusts optional `frames-00/01.jsonl` as source data. Those derived indexes can be deleted without preventing replay.
- Prior to projecting *any* protocol frame, `PassiveJournalVerifier.Verify` validates both streams, sequence continuity, CRC32 on every record, manifest declared chunk/byte counts and SHA256 for all payload bytes, and requires clean finalized status. Corrupted/incomplete captures fail closed; salvage mode is a separate future work item.
- Two independent incremental FT1.2 scanners preserve partial frame state across UART read chunks. Per-direction records are consumed in exact byte order; a bounded streaming cursor retains one raw record per input direction and one UI batch of at most 96 frames. No whole-file loading into RAM.
- Manifest IEC-101 field widths and gap settings are restored for correct interpretation of historical captures regardless of the active Setup dropdown defaults.
- UI display retains bounded Frame Trace/Events, whereas point Values reflect decoded observations. Capture stays on disk. UI work is performed in bounded Dispatcher batches and reading/verification on a worker thread, with cancellation support.
- No serial writes, GI, link reset, clock sync, COM opening, or output modem control on offline replay.

## Verification gates

- Verified full two-direction capture roundtrip with bytes intentionally split into very small arbitrary UART chunks; recover fixed/variable/single-character frames and exact source offsets.
- Delete derived frame indexes; replay must produce identical results.
- Corrupt one journal byte: verify must reject **before** producing any frame to Frame Trace.
- Cancellation and interrupted journals must fail clearly, never fabricate verified evidence.
- Windows WPF `--passive-gui-smoke` unchanged, plus all xUnit/protocol regression suites and release gate.
- Physical non-interference, full master/slave exchange correlator, exact offline paging/search of multi-day journals, independent A/B (4 COM) observation, and device FAT remain on issue #19.

## UI terminology

**Open Cap** opens existing `.ariec` decoded excerpts **or** `manifest.json` of a full passive journal. **Capture → Open Folder** opens the original full recording directory. **Save View** only exports selected decoded visible evidence, and must never be represented as saving the entire raw journal.
