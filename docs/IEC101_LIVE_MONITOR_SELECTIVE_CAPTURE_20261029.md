# IEC-101 Passive Monitor — Live-first workflow and selected frame sharing

## Default workflow

1. In **Setup**, choose **IEC-101 Passive Monitor**, configure Link A SCADA/RTU RX COM inputs (and optional Link B), baudrate, serial framing and IEC field widths.
2. Leave **Record entire raw session to disk** **unchecked** (default). Press **Start Monitor**.
3. The existing **Trace**, **Values**, **Events** and **Findings** show received traffic. No directory, manifest, binary journal, or JSONL files are created in this live-only mode.
4. In **Trace**, click a row or use **Shift/Ctrl** to select a contiguous block or multiple frames. The selected frames remain inspectable with their raw bytes and decoded semantics.
5. Right-click a frame in **Trace**: choose **Copy Selected Frame** to place readable text directly on the clipboard (ready to paste), or **Save Selected Frames (.ariec)** to create a file that ARIEC60870 can reopen. Right-clicking within an existing Ctrl/Shift selection copies the entire selection without clearing it. The **Save Frames** rail action also exports the selected file.
6. Press **Stop Monitor** to close read-only COM ports. The visible ring remains available for selecting and exporting retained frames.

## When full serial recording is required

Before pressing Start Monitor, check **Record entire raw session to disk** and choose the output folder. The same receive-only engine then writes raw source journals, frame indexes, transactions, GI facts and interval summaries, including SHA-256 integrity evidence. They can later be verified and replayed using **Open Capture → manifest.json**. Optional full recording cannot be enabled retroactively for already-missed frames; stop and reconnect with the option checked.

Live-only monitoring is **not** a lossless complete session archive. UI queues and the frame ring are bounded. Frame selection exports only the chosen **visible** evidence. The interface never claims unseen frames were captured or that an excerpt is complete. Physical RX-only wiring (TXD/RTS/DTR not connected to the plant) remains mandatory.

## Portable `.ariec` selected-frame format

`.ariec` is a **ZIP container** holding:

- `manifest.json` — selection scope, source protocol and SHA-256 of `frames.jsonl`.
- `frames.jsonl` — authoritative machine-readable per-frame evidence with original raw HEX, decoded fields, source and selected-object reference, and field-size profile when known.
- `analysis.md` — human/AI-readable ordered frame explanations: direction, raw HEX, COT and P/N/TEST/OA, quality flags, command response details, CA/IOA and per-object values when decoded.
- `report.md` — compact table/retention summary.
- `retention.json` and `hash.txt` — disclosure of the visible-ring limitations and source evidence hash.
- `README.md` — instructions inside the portable archive.

**Open Capture** checks the manifest SHA-256 before interpreting the selected frame file and reuses the shared decoder for trusted IEC-101/104 raw/profile bytes. Historic V1 remains readable without fabricating missing width semantics. The Markdown file is deliberately separate from the authoritative JSONL; AI summaries must not overwrite the raw source.

**Security:** The application does not upload selected frames to AI or other services. Clipboard and saved files are explicitly user-controlled. Share only after checking that raw addresses, source labels, timing and payload may be disclosed.

## Engineering acceptance

- The default GUI checkbox is OFF; Start Monitor does not create folders/files.
- Two COM inputs deliver original, intact FT1.2 frames to Trace; four COM A/B remains supported.
- Opt-in journal mode retains byte-for-byte and SHA verified replay, no TX.
- Trace select-block yields a reopenable `.ariec` and `analysis.md` that faithfully preserve original bytes, source offset, all decoded object rows where available, COT and quality semantics.
- Copy Selected Frames produces identical human-readable evidence, not a network request.
- Windows CI Release/test/WPF smoke must pass. Real serial hardware interoperability and sustained load need independent FAT.
