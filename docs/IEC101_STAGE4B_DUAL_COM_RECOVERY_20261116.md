# IEC-101 Stage 4B — source-qualified physical A/B COM recovery

**Base:** merged `master@5c1717486b3453ffe0bcb1d1ae8b78f56cf117dc` (#222 Stage 4A). Focused A/B integration from the historical draft #145; preserve #220 owner-stamp/queue epoch and #221 shared transaction/FCB core.

## Fault isolation and guarantees

- Physical Open/Write/Read exceptions are classified only at the I/O boundary using the shared Stage 4A fault policy. A genuine DFC=1, remote no-data, silent timeout or programming exception does not trigger physical COM reopen.
- A fault on A does not close healthy B. The faulted physical channel is closed, loses all addressed RTU proof, FCB and RX framing/echo history, and is reopened independently under bounded 100–5,000 ms backoff. Reopening never borrows historical secondary proof from the prior COM handle.
- If the *active* physical link failed, revoke election generation, old observed CA/GI evidence and all unsent controls. No cross-link automatic command replay. If standby fails, healthy active polling and operator authority remain intact.
- A newly opened active link receives FC9 secondary link verification before Class1/2. Standby receives only FC9 supervision until verified/promoted; no standby Class1/2, GI, clock-sync or controls.
- Manual Connect startup FC0/FC7/GI/clock sync are never implicitly replayed by a physical reopen. A deliberately configured post-switch GI policy remains separately owned by failover policy, not by COM reopen.
- Same COM serial RTS/DTR/handshake settings, WPF GUI layout and saved profile remain unchanged.

## CI acceptance / field status

- Synthetic A-unplug -> B continuation + A reconnect; initially absent B must not stop A; both absent remain cancellable with zero TX; fresh epoch invalidates old secondary health/FCB while retaining cumulative counters.
- Exact PR-head Windows Release, mandatory formatting, 19 protocol smoke, all xUnit, CodeQL, hygiene, dependency and 1,400-frame WPF acceptance required before merge.
- **Not physical FAT:** SICAM A8000, serial drivers, real 1200-bps FIFO, exact matched COM/IEC_Test, 60/500/2000 events, A/B physical disconnect/replug, low-level driver Write cancellation and long soak remain unverified.

## Remaining P0

L11/T04 intrinsically ambiguous delayed identical-service FC11, physical field FAT, command application-level ACTCON/ACTTERM and SBO sequencing, 60/500/2,000 end-to-end SOE provenance, multi-vendor interoperability. No v3.6.12 release modification.
