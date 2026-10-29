# IEC-101 Frame Trace — contract: exact engineer-readable multilines (2026-10-29)

Screenshot/telegram-driven requirement: **one physical FT1.2 frame = one compact four-part block**. Do not regress to a one-line summary. 1. `⇧/⇩ [PC time] human-friendly event name`; 2. `Data Link:` service, all defined bit flags and link address (+ L for variable); 3. when a decoded ASDU exists, separate `ASDU:`, `Cause of transmission:`, `Comm Addr`, and one line per information object; 4. `Hex Frame:` all original bytes.

## Examples, protocol-correct
```
⇧ [03:39:37] Class 2 request
Data Link: Class 2 (Respond) [PRM:1,FCV:0] [FCB:0] Data Link Addr 105
Hex Frame: 10 4B 69 B4 16

⇩ [03:39:37] Class 2 Data Not Available, Class 1 Data Available!
Data Link: Nack (Data Not Available) [PRM:0,ACD:1,DFC:0] Data Link Addr 105
Hex Frame: 10 29 69 92 16
```

For valid Type 30 data, present e.g. `ASDU: Type ID 30 <Single-point with date/time> Count 1`, `Cause of transmission: 20 (...) | Origin: 0`, `Comm Addr 1`, `SI 8388716 1 [Valid] 10/28/26 20:39:28.460 [Valid time,Standard time]` and the whole physical frame. Multiple IOAs all belong in that one frame block; object-specific Value/Event evidence still flows independently. Open the inspector to analyze individual bit/byte groups.

**Bit-level correction:** control 0x4B (and 0x6B) has FCV bit 4 = 0, not 1. Source HEX takes precedence over a copied human label. Control 0x5A/0x7A for Class 1 has FCV=1.\n\n**Strict integrity:** the provided example variable frame `68 14 14 68 ... 85 16` is 25 bytes long although L=20 requires 26. Its checksum does not match supplied bytes and `64 FC` in its CP56 milliseconds field exceeds 59,999 ms/minute. It MUST NOT be silently accepted as `[Valid]` just to mimic an example. Synthetic regression instead creates a valid Type 30 frame, correct checksum, CP56Time2a, QDS/SIQ, COT, origin and CA. Time-zone origin/genuine-status not encoded in CP56 alone; show validity and summer/standard flags only, never invent authenticity.

## Architecture and performance
- Pure `Iec101TraceNarrator` receives **already-decoded typed evidence**, formats exactly once per physical frame; no new regex parser, no serial I/O or history mutation.
- `EvidenceRow` renders label, newline-separated details and full raw HEX in 3 compact WPF text rows; TX blue/RX green lightweight data triggers. No large cards.
- Frame Trace suppresses redundant **secondary per-object evidence rows** only. They remain in Values and Event Log. Primary physical event includes the entire `AsduDetails.Objects` list.
- Initial WPF blank tab fix and source-separated M1/M2 SSOT remain unchanged. Existing bounded ring 1200 rows, 75ms UI batched flush, and passive zero-TX protection retained.

## FAT gates
Inspect real 1200-bps 8E1 SICAM link: raw FC10/FC11, FC9 with ACD/DFC, FC8 Type 30 multi-object, GI ACTCON/ACTTERM and link E5. Confirm each exact original checksum, length, time flags and link address. Test Connect without switching tabs, inactive tab retention, selection/Auto, performance under long sessions and multi-COM passive. CI passing is not a substitute for real FAT. Also verify IEC-103/104 and imported old .ariec separate from IEC-101 on-wire formatting; do not blindly interpret their raw as IEC-101 FT1.2.
