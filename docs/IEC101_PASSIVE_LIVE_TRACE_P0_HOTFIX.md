# P0 field regression — IEC-101 passive RX receives bytes but Frame Trace appears frozen

Date: 2026-10-28. Base: merged P3 four-COM A/B on `master` `d3c2e5bd6b9d913a78d5d03a9ecdcc7b8be662e1`. This is a production usability/reliability hotfix, **not another new-window feature**.

## Root causes in prior source

1. `MainWindow.RefreshPassiveUi` consumed decoded frames and pushed `OnEvidenceReceived` into `_pendingEvidence` but **did not call `FlushUiQueues`**. The Active Master owns its own flush lifecycle and is intentionally stopped/hidden in Passive mode. The passive 160-ms DispatcherTimer must explicitly flush the same existing Frame Trace / Values / Event Log + Findings queues; otherwise the COM RX counters may advance while the visible interface appears to do nothing.
2. `PassiveFt12Scanner.ShiftOne()` incremented `NoiseBytes` for unrecognized material but never published those bytes as frame evidence. With the wrong configured IEC-101 Link Address width, even a correctly formed fixed Link Status / ACK would be silently discarded as noise. The raw journal was retained but frame-level diagnostic visibility was incomplete.
3. There was no explicit indication that a fixed frame was structurally valid under a **different** 0/1/2-octet Link Address width. This matters for SICAM A8000/SCADA interoperability where devices may use two octets.

## Implementation

- The existing passive GUI dispatcher tick now calls `FlushUiQueues()` **after** draining the bounded frame/transaction/GI/timeline feeds, and **before** the raw-chunk-only Freeze guard. Freeze raw samples no longer accidentally prevents the Frame Trace from keeping up. It uses the established adaptive flush budget and WPF batched UI collections, not one WPF redraw per byte.
- Each `PassiveFt12Scanner` reports discarded serial material in bounded (≤128-byte) **Noise** groups with exact source offset and raw hex. It still maintains byte-exact original binary journal, and never transmits. Noise can represent wire noise, wrong UART settings, a bad tap, or an unsupported/corrupt FT1.2 frame; **not automatically an RTU failure**.
- Fixed 0/1/2-octet frames with valid checksum/end on an **alternative address width** are tagged **LinkWidthMismatch** (with detected octet count and full raw) instead of being silently lost. This is a diagnostic candidate, **not a silent configuration override**; the existing Setup or P2C offline re-decode remains the authoritative way to change interpretation. Variable-length frames do not have enough information to infer the actual Link Address width safely from checksum alone.
- All emitted Noise/Mismatch candidates propagate through the complete writer and SHA-verified offline replay, go into the full original frame indexes, and reach the existing Frame Trace as RX warnings with concrete instructions. They do not advance GI/transaction state as if valid under the selected profile. Active IEC-101 Master TX remains unchanged.
- Existing scanner tests, dedicated new tests for 2-octet Link Status under 1-octet Setup, unrecognized bytes before valid FC9 and E5, 512-byte junk burst bounded into four 128-byte notes; verified offline replay regression and actual Windows WPF MainWindow smoke for Noise + LinkWidthMismatch evidence.
- The app's RX byte progress still comes from committed writer bytes, not GUI row counts. Frame Trace is bounded presentation; full raw remains durable.

## Practical checks on site

1. **Setup → IEC-101 Passive Monitor**: select BOTH actual RX COM ports (SCADA TX and RTU TX), baud/parity/stop, **Link Address width 2 octets if that matches the SICAM/ETERA interoperability profile**, and correct COT/CA/IOA widths. With A/B redundancy enable four distinct RX COM ports.
2. Start recording. Raw RX byte counters must advance on the corresponding source when electrical traffic exists. Frame Trace must show FC9 Link Status, FC11 secondary Link Status, Class1/2 polls, E5 and complete frames. Noise/mismatch warning rows should be inspectable instead of disappearing.
3. If bytes increase but no legitimate frame appears, inspect Noise/LinkWidthMismatch rows and change the profile accordingly. Check RS232 cabling, grounding, baud/parity and tap impedance. If even RX byte counters remain zero, verify electrical tap/COM choice; software cannot invent missing bytes.
4. **Open Cap → manifest.json** after recording to rebuild all evidence from complete verified raw files; the raw journal remains the legal/forensic original. A GUI rate cap does not delete original bytes. Use full offline replay when evidence exceeds bounded visible WPF rows.
5. Use an isolated, receive-only high-impedance tap and keep monitor TXD/RTS/DTR disconnected from production. Do not disrupt the existing modem handshake jumpers required by the RTU. USB/Windows timestamps cannot prove 33-bit electrical idle.

## Verification gates

Windows Release build with warnings-as-errors, xUnit regression, 19 protocol smoke, actual WPF MainWindow GUI smoke, CodeQL, dependency and repo hygiene, verified self-contained Windows-x64 artifact. Real live SICAM/ETERA hardware FAT is still pending and must not be claimed from simulated CI.
