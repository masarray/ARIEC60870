# P0 — IEC-101 Passive Serial Raw Capture (GUI Engineering Field Preview)

Status: implementation branch, **hardware not yet qualified**. Tracking: issue #19, design PR #20.

## Purpose

Read-only observation of an existing IEC-101 RS-232 full-duplex connection using **two monitor COM ports**. COM monitor #1 listens to the SCADA transmitter, COM monitor #2 listens to the SICAM RTU transmitter. It does not issue GI, Class 1/2, Link Status or any control. P0 is the **capture substrate only**, not full ASE2000-style protocol decoding, GUI, or packaged .ariec V2. P1 adds interpretation and shareable .ariec export. Do not misrepresent P0 raw journal as a complete .ariec investigation package.

## Physical prerequisites

1. Confirm the plant interface is truly RS-232, not RS-485, and get the actual A8000 module's pin assignment; generic RJ45-to-DB9 pinouts cannot be assumed.
2. Leave production SCADA↔RTU cables and hardware handshake unchanged. An interrupted handshake can itself cause a ~30-second failover.
3. Use an approved high-impedance/buffered **isolated RX-only tap** and an electrically safe ground reference.
4. **Do not connect monitor TXD, RTS or DTR to either production endpoint.** A USB COM adapter/driver may toggle these outputs on open even when the software sets them false.
5. Two COMs cover only both directions of ONE physical duplex link. To see both paths of redundant A/B, four RX inputs are needed (P3).

## Main GUI workflow (preferred)

1. Download the Windows x64 GUI test package from the PR's latest passing CI artifacts. Open `ARIEC60870.Desktop.exe` from its published folder (runtime bundled; no .NET SDK needed).
2. Click **Sniff RX** on the main ARIEC60870 left toolbar. This opens a dedicated **IEC-101 Passive Monitor** window, not a Master/Command session.
3. Select two different receiver ports: *SCADA TX → RTU* and *RTU TX → SCADA*; verify baud, parity, data and stop bits, and choose the recording directory.
4. Confirm the electrical isolation checklist after physically verifying the RX-only tap.
5. Click **Start Recording**. See cumulative byte counters from each direction and sampled raw HEX rows. The preview is bounded and intentionally may skip rows; actual journal raw byte capture is independent.
6. Click **Stop & Finalize**. Use **Verify Capture** for CRC32, sequence, offsets and SHA-256. Use **Open Capture Folder** to access both binary journals and manifest.
7. Switching to another window, freezing the view or scrolling does not interrupt the disk archive. Closing the monitor while recording requests Stop and waits for finalization.

P0 GUI shows raw UART sample chunks, **not yet fully framed and decoded IEC-101 telegrams**. P1 adds protocol parser, transaction pairing, timeline, and final `.ariec` export.

## Internal CLI diagnostic (optional, not the user-facing app)

## Build & run on Windows

Requires .NET 8 SDK to run directly from source. From repository root:

```powershell
dotnet build ARIEC60870.sln -c Release
dotnet run --project src/ARIEC60870.Monitor.Cli -- capture --scada-com COM7 --rtu-com COM8 --output C:\ARIEC-Captures --baud 9600 --parity Even --data-bits 8 --stop-bits One --seconds 180
```

Replace port names and line parameters with verified hardware values. Record at least several switching cycles (e.g. three minutes for a 30-second interval). Ctrl+C finalizes after the current short serial read timeout. Local opening of COM ports is NOT proof of IEC-101 exchange.

Captured directory is named `ARIEC-PASSIVE-<UTC timestamp>-<random>`. It contains:

- `stream-00.bin`: raw received bytes from SCADA TX, wrapped in checksummed journal records.
- `stream-01.bin`: raw received bytes from RTU TX, wrapped in checksummed journal records.
- `manifest.json`: serial port roles, explicit baud/parity/data/stop framing, UTC/Stopwatch origin, per-direction record/byte counts, SHA256, error counts and capture completion status.
- `manifest.pending` may be present if Windows/process terminated during manifest update. Never claim crash-interrupted capture is finalized.

Each record is 40-byte header plus received payload: `APM1`, version, stream, flags, increasing sequence, Stopwatch ticks, raw stream offset, payload length, CRC32 of header prefix + payload. No byte is silently discarded by protocol framing. Timestamp is USB/Windows read-completion time, not a physical bit timestamp.

## Verify without COM connected

```powershell
dotnet run --project src/ARIEC60870.Monitor.Cli -- verify --folder "C:\ARIEC-Captures\ARIEC-PASSIVE-..."
```

A passing audit verifies journal structure, per-direction sequence and raw offsets, CRC32 on every chunk, recorded payload SHA256 and manifest counts; it does **not** prove no bytes were lost inside a USB adapter/driver before the software saw them. Driver-reported parity/frame/overrun errors, disk failures, aborted recording and backpressure invalidate the clean evidence status.

## Operational boundaries

- This is an integrated GUI P0 capture. The existing WPF IEC-101 Master and Dual Link sessions remain unchanged; Sniff RX opens a separate receive-only window.
- The GUI includes live raw-byte chunk preview, capture controls and verification. IEC-101 decoded telegrams, event correlation, failover detection, V2 .ariec ZIP export, and multi-link 4-COM capture are P1–P3.
- `NoApplicationLossDetected` means no known application-layer drop; it is **not** hardware-grade lossless proof.
- When one COM fails to open, the session is marked `Incomplete`, not silently reduced to one-direction observation.
- Do not upload unsanitized customer/SCADA/RTU capture files to public GitHub.

## P0 test gates

Automated regression tests cover two deterministic independent byte streams (including arbitrary bytes), raw SHA256 parity, journal CRC, truncation, source/sequence checks, failed second COM opening and driver error degradation. Windows CI builds all .NET projects and runs Monitor tests with the existing regression suites.

**Physical acceptance remains pending**: real adapter driver behavior, electrically nonintrusive tapped wiring, COM read saturation and a multi-hour capture on the target field PC must be tested separately before live commissioning use.

Sources: docs/IEC101_PASSIVE_MONITOR_PRD.md, docs/IEC101_PASSIVE_MONITOR_ARD.md in design PR #20.

## CI bench artifact

When all Windows CI gates pass, the PR workflow publishes a **non-release** self-contained win-x64 **ARIEC60870 Desktop GUI** ZIP with SHA256SUMS.txt as an Actions artifact named `ARIEC60870-Passive-Monitor-P0-GUI-win-x64`. This is a field/bench test artifact, **not** a certified production deployment.
