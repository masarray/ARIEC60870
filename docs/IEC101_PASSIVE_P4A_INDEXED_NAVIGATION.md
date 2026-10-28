# IEC-101 P4A — sparse anomaly navigation and verified raw drill-down

2026-10-28 · Based on merged P0–P3 and P2B/P2D/P2E. Old P3 experiment branches are not a baseline.

## Operator workflow

1. Configure IEC-101 Passive Monitor in the existing Setup profile. Record two physical RX-only taps for Link A or four for A/B.
2. Finish recording. In Capture click **Anomalies**. Alternatively Open Cap and choose a finalized manifest.json, finish full replay, then click Anomalies.
3. The selector displays 60 latest anomalous five-second windows, newest first, labeled by UTC, circuit, retries, timeouts, invalid frames and FC15. **Older** navigates backwards.
4. Select a row. The existing Capture HEX inspector resolves the physical stream and original byte offset against CRC/SHA-verified APM1 data. The derived timeline JSONL is not treated as raw evidence.
5. If the selected profile cannot resolve the exact frame, the inspector says so. It does not manufacture frames, vendor states, NUC switching or accurate electrical timestamps.

## Technical boundary

- New WPF-free PassiveTimelineEvidenceNavigator in Monitor.
- Sparse index stores one block descriptor per 128 timeline lines: observed UTC envelope, circuit mask and anomaly flag. Result pages are capped at 200 engine-side, 60 UI-side. No File.ReadAllLines, no unbounded UI collection.
- Each page uses an exclusive reverse byte-offset cursor and rejects mutated or stale JSONL. Time and circuit filters are engine-supported.
- OpenVerified refuses unverified, incomplete or damaged original raw journals. Derived timeline JSONL remains a navigation hint, not an authenticated source.
- Anomaly selection triggers independent cancellable verified raw replay. Original raw is never modified, and no serial TX is issued.
- Timeline source numbers are circuit-relative: Link B local RX1 resolves to physical RX3.
- Historical selection does not inject old values into live Values or Event Log. It displays raw bytes and provenance only.
- Host USB timestamps cannot prove electrical idle, precise inter-circuit ordering or NUC role switching.
- Raw drill-down currently uses bounded-memory sequential verified replay up to the target; it can be slow on multi-day archives. Checkpointed raw seeking is future P4B work after real capture benchmarks.

## Tests and delivery gates

- 1200-line synthetic A/B timeline, exclusive backwards pagination and no repeated hits.
- UTC/circuit filters; B RTU RX3 resolution; malformed/modified derived JSONL rejection; incomplete archive refused; derived-only readers cannot claim authenticated raw.
- Windows Release GUI smoke, all xUnit/protocol tests and artifact packaging must be green before merge.
- Real SICAM A8000/ETERA hardware FAT and multi-day CPU/RAM benchmarking are still pending.

## IEC-Test benchmark findings

The current IEC-Test view provides dense Type ID, COT, CA, IOA and quality detail. ARIEC60870 should exceed it in verified cross-source forensic navigation and guided diagnostics rather than merely copying grid density.

The captured 10 09 69 00 72 16 is a valid 2-byte-link-address IEC-101 secondary FC9 no-data fixed telegram for link address 105. Repetition alone is not evidence of parser loss. Compare captures taken at identical times and with identical field widths. The screenshot IOAs 16712711 (0xFF0407) and 16712713 (0xFF0409) warrant raw byte-level cross-check before claiming they are equivalent to IEC-Test's grouped IOAs. CP56Time2a invalid-time bits must stay visible and must not be treated as trusted relay time.
