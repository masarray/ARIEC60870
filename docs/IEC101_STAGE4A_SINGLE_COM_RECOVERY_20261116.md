# IEC-101 Stage 4A — bounded single-COM physical fault recovery

**Base:** `master@ad596304acbecbc6fdc1210d8a6e4f6c607a5166` (Stage 1/2/3 merged). Focused source integration from old draft #145, not bulk merge.

## Recovery contract
- Catch only **actual physical Open/Write/Read** exceptions (IOException, UnauthorizedAccessException, TimeoutException, and closed-transport InvalidOperationException); wrap as `Iec101PhysicalLinkFault`. Normal FT1.2 response silence, no-data, DFC busy, peer NACK or application bug do **not** force a COM reopen.
- Recover inside the **same operator RunAsync session**: revoke link/command generation, discard all pending queued commands, close the physical handle, and retry same configured serial port with bounded exponential **100..5,000 ms** cancellation-aware delay. Report first attempts and powers-of-two to bound diagnostic spam; fresh read/state epoch resets FCB/RX uncertainty and TX echo history.
- After physical reopen, verify the addressed IEC-101 secondary link via **FC9 before any Class1/Class2**. Do not silently re-run operator-Connect FC0, FC7, startup GI, startup clock synchronization, or **any** old operator command on the recovered epoch. An in-flight FC3 write's delivery is **unknown**, never retransmitted.
- This does not alter CTS/RTS/DTR/handshake configuration, hard-coded link address, field protocol profile, mapping, WPF event layout, v3.6.12 public installer, or active/standby behavior.
- `IsOpen` from the OS is not RTU secondary proof. Explicit physical reopen does not count as protocol link verification.

## Software acceptance
- Synthetic fake COM: physical unplug on Class2 transmit, first-open failure, cancellation during repeated missing-port opens, I/O failure while driver still reports IsOpen=true, unexpected programming error classified as non-physical, and startup-GI not replayed after COM loss.
- Required CI gates: Windows Release, changed-source formatting, all xUnit suites, 19 protocol smoke, CodeQL, dependency review, hygiene and WPF 1,400-frame acceptance on exact head.
- **Not claimed:** physically unplug/replugged SICAM A8000 at 1,200 bps, multi-hour USB/COM driver soak, full 60/500/2,000 FIFO delivery evidence, direct/parallel port pairing or IEC101 balanced mode. Windows drivers may block synchronously during `SerialPort.Write`; this requires physical device FAT and cannot be certified by a fake transport.

## Remaining Stage 4B and P0
- Dual A/B physical fault quarantine and bounded reopen with source-specific failover/recovery; preserve standby restrictions and old-owner command revocation.
- Late **identical-service** FC11 ambiguity, full staged GI/SBO proof, receive/archival 60/500/2,000 SOE accounting, physical matched-port FAT and public release gate.
