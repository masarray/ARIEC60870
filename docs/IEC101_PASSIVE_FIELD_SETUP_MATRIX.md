# IEC-101 Passive Monitor — Field Configuration and GUI Contract

Status: P1 UI/serial usability refinement, 2026-10-28. Tracking issue #19.

## Principle
**Passive** means *no serial transmit*. It does not mean reduced GUI functionality.
The original left rail must remain useful in Passive mode:
Setup, Record RX/Stop RX, Report, Open Cap, Save View, Trace TXT, Auto, Clear, Signals, Help.
The rail must remain scrollable at the supported minimum window height.

### Distinguish the two different saved artifacts
- **Full raw recording**: automatic, disk-backed per-direction `stream-00.bin`/`stream-01.bin`, associated manifests, hashes and derived frame indexes. Capture tab → Open Folder / Verify. This remains authoritative and **does not depend on selection in Trace**.
- **Save View (.ariec)**: selected decoded Frame Trace rows via the existing Save Capture action. This is an excerpt/view, **not a substitute for the full raw serial journal**.
- **Trace TXT**: selected decoded rows in a text format, or currently visible rows when none selected.
- **Open Cap**: opens an existing `.ariec` offline *frame* capture. It is not a generic PCAP parser and does not reprocess raw passive journal folders yet. Disable during live passive recording because opening a different archive clears the current evidence view.
- **Clear** clears only the current UI evidence/Trace, never the raw journal on disk.
- **Signals** opens the existing IOA list editor. Monitor mode still benefits from engineering names/point mappings.
- **Auto** controls live scroll only; recording continues independently.

## Parameters: capture vs decode

| User field | Purpose | When to change |
|---|---|---|
| SCADA TX COM | Receive-only input from the SCADA→RTU transmitter | When changing tap/cable/USB adapter |
| RTU TX COM | Receive-only input from the RTU→SCADA transmitter | When changing tap/cable/USB adapter |
| Baud rate | UART bit rate, editable from 300 to 921600 depending on hardware | Match the working production link exactly |
| UART framing | 8E1, 8N1, 8O1, 8E2, 8N2, 8O2; **8 data bits mandatory** for FT1.2 bytes | Match production parity/stop bits, e.g. 8E1 at 1200 |
| Link address width | 0/1/2 octets | Match IEC-101 link profile; 0 can occur in balanced profiles |
| COT width | 1/2 octets | Match IEC-101 ASDU profile |
| CA width | 1/2 octets | Match IEC-101 ASDU profile |
| IOA width | 1/2/3 octets | Match IEC-101 information-object profile |
| Link mode | Auto/Unbalanced/Balanced observation label, preserved in evidence manifest | Auto unless reliable device config is known |
| Frame gap | 100–10000 ms, default 900. Silence threshold for classifying *incomplete* fragments | Adjust when slow serial adapters/reporting gaps yield false partials |
| Capture folder | Disk destination for complete journal | Select a writable disk with sufficient free space |

User selections are saved/restored for repeated field use. The link/ASDU field sizes change **interpretation**, never whether received raw bytes are saved. An incorrect parity/baud setting can corrupt UART bytes before software can decode anything. None of these settings affects the production devices directly when an approved RX-only tap is used.

**No** SCADA/RTU target Link Address, Common Address, IOA filter, GI, Class poll interval, clock-sync or command setpoint is required to sniff. Addresses and service codes are observed from the actual telegrams; no active Master engine is constructed. Flow control/RTS/DTR settings from Active Master are deliberately absent in Passive mode because the monitor must not drive production handshake pins.

### Two COM inputs vs redundancy
Two receive-only COMs cover **both directions of one full-duplex serial circuit**. To examine an A/B redundant SCADA system simultaneously, four independent RX monitor inputs are needed. Do not label one monitored line as evidence of the unseen line.

## Test gates
1. Existing active IEC-101, IEC-103 and IEC-104 workflows unaffected when switching from passive profile.
2. Real MainWindow GUI smoke asserts all former left rail tools remain visible and left rail scroll viewer is live.
3. GUI smoke configures COM201/COM202, 1200, 8E2, Balanced and gap=1500 ms without opening hardware, and checks resulting options.
4. Monitor xUnit asserts stop bits 2, profile widths, valid gap bounds, immutable 8-bit data, mismatched directional UART rejected.
5. Warnings-as-errors Windows Release build, xUnit suite, 19 protocol smoke, CodeQL and Windows portable GUI publish PASS.
6. Physical RS232 tap non-interference, USB adapter wiring, long recording endurance, offline raw-journal replay remain separate uncompleted field gates.

Do not claim this is a full independent IEC-101 reference or a physically proven zero-loss analyzer. Monitor software intentionally does not assert outgoing modem control wires, but USB devices may toggle them on open; physical isolation remains required.
