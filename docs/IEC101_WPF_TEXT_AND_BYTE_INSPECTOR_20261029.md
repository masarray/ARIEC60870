# WPF Trace readability + byte-true IEC-101 Inspector (2026-10-29)

## Screenshot reproduction
At 1526x955, live Trace labels and HEX looked jagged/faint. Existing XAML directly overrode the theme's ClearType/Ideal text default with `TextFormattingMode="Display"` and ~10.5 DIP monospaced font across explanatory prose; Inspector used tiny two-column labels, noisy generic prose and fake raw chips (e.g. `L=105,CA=1,IOA=...` instead of hex).

## Fix
- Trace title/meaning rendered in normal AppFont (Segoe UI/Aptos), 12.8/12.2 DIP, 18-DIP lines, darker ink and **Ideal + ClearType**, preserving light ListBox virtualized 1200-row bounded display; raw HEX uses Cascadia Mono/Consolas 11.6 DIP with Ideal + ClearType and no shadow, animation or per-frame additional UI.
- Inspector uses neutral white background; larger 11.9-12.2 DIP type and simple compact field + HEX + decoded value layout, not a large lecture-style two-column table.
- New **pure `Iec101FrameByteInspector.Explain`**, invoked only when a frame is selected, maps actual FT1.2 byte offsets and already-decoded ASDU information-object offsets into linked field/HEX groups; WPF receives one identical list as both HEX highlights and field explanations. Shows Start/L, Control (PRM/FCB/FCV or ACD/DFC), Link Address, Type ID, VSQ, COT, CA, *each* IOA, value/quality/CP56 time and checksum/integrity. It never invents an address string as HEX, and never reparses the original serial telegram.
- Each chip/field has a stable shared key, including object-N/value-N for multiple IOAs. Default focus is first value if decoded, else control. Typed check and raw byte length guard prevents out-of-bounds extraction. Unknown fields explicitly remain unknown; invalid checksum/length cannot become Good.
- Copy decoded frame uses the same field map as Inspector, not generic prose.
- IEC-103/104 Inspector branches remain unchanged; no changes to serial/TCP I/O, polling, FCB, passive RX-only or backpressure semantics.

## Performance / FAT
Layout/font-only WPF changes; decoder usage restricted to selected Inspector frame. No new timers, repeated UI updates, polling, font dependencies, animations or bitmap buffers. 1200-row virtualization and 75ms batching unchanged.

**Field FAT required**: test Windows display scaling 100%, 125%, 150% with COM 1200 bps and same SICAM setup, immediate Trace first paint, long streaming readability/no stutter; select 1-octet and 2-octet address frames, multi-IOA, FC9/ACD, Type30/CP56, invalid checksums; every HEX chip must match exact raw slice and update linked field highlight. Test screenshot legibility and compare ASE2000. CI is not visual proof of hardware rendered text.
