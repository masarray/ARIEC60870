# IEC-101 L14/P05 — physical COM recovery software evidence

**Status:** software-fixture validated on earlier integration head; latest reconciled head must pass CI. **Physical FAT: pending.**  
**Owner:** [P0 reliability issue #143](https://github.com/masarray/ARIEC60870/issues/143) · [draft PR #145](https://github.com/masarray/ARIEC60870/pull/145)

## Root cause and architecture fix

Previously, an `IOException` or closed COM during the main IEC-101 receive/send loop reached the session-wide `Faulted` handler, ended the active Master, and forced a second operator Start. Physical A/B faults likewise ended the controller rather than isolating the failing channel.

The revised runtime keeps **one operator Start/Stop session** while explicitly separating physical port epochs from confirmed protocol exchanges:

1. **Single-link:** recoverable driver/port errors invalidate the pending transaction/FCB epoch and link health, clear unsent controls, close the COM, then retry opening the **same** configured port using bounded 100–5000 ms exponential backoff. Stop cancellation interrupts backoff. Each fresh port requires an addressed FC9 response before GI/Class/command scheduling. Previously attempted startup clock sync and GI are *not blindly replayed*.
2. **Dual-link A/B:** physical port errors are tagged with their actual link owner. A failing standby does not terminate the healthy active link; a failing active invalidates the command-owner generation and cannot lend its old proof or pending control to the new active. Each closed link independently reopens when its own backoff expires and begins with **unverified** link health. Existing promotion rules still require fresh secondary-response proof; standby never transmits Class1/2/GI/controls.
3. **Driver evidence:** `SerialByteTransport` converts unexpected `SerialPort.BytesToRead` and `SerialPort.Read` invalidation to diagnostic physical I/O errors; cancellation remains distinct. An `InvalidOperationException` from a protocol/scheduler invariant while the transport is still open must not be hidden as a COM disconnect.

No port-number, baudrate, parity, Handshake, RTS/DTR, remote link reset, startup FCB reset, clock adjustment, physical command, or GI policy is guessed or silently changed. Every physical fault marks prior TX/command delivery **unknown**, not successful or safe to replay.

## Deterministic tests and negative gates

- **Single link:** unplug while transmitting Class2, COM unavailable on initial Open, cancellation through repeated failures, RX driver I/O error while `IsOpen` remains true, and unexpected non-transport invariant exception (must fault, not silently recover).
- **Dual link:** active A lost during Class2 while B remains usable, standby B absent at initial Open and then recovered without stopping A, and both COM ports absent while Stop remains cancellable.
- Existing FC1/FC9/FC11, E5, DFC, FCB, GI policy, source-qualified command, burst and passive-monitor regression tests are mandatory at the exact final SHA.

## Important limitations — not a full field sign-off

- **No physical COM unplug/replug acceptance yet.** Windows xUnit simulates transport failures; it cannot establish how a particular USB/RS-232 driver behaves during `SerialPort.Close`, especially if a synchronous write is blocked in the OS.
- **FCB state after physical reconnection remains a field/profile question.** The new epoch resets local FCB and requires FC9 status, but FC9 alone does not prove remote FCV/FCB synchronization or GI completeness. The runtime does not invent a remote reset. A profile-authorized controlled FCB resynchronization must be validated on independent peer and the target RTU.
- **Ambiguous late same-service FC11 (L11/T04)** remains open. Identical FC11 status frames have no transaction identifier; do not claim causality without timing/source qualification.
- **Staged GI/SBO/ACTCON/ACTTERM (S04/S05/T08)** and real 1,200-bps 60/500/2,000-SOE RTU FIFO/independent IEC_Test FAT remain open.
- **Portable/installer and release status:** no new public release or stable certification. PR #145 stays **draft** until exact-head Windows security, WPF and hardware gates are distinguished.

## Evidence policy for field test

Use the same physical COM, adapter, cable, port control-line settings and RTU configuration as a known-working IEC_Test baseline. Collect timestamped original TX/RX hex, COM open/close failures, driver exceptions, link A/B ownership epoch, latency, Class 1/2 counters, command unknown-delivery facts, GI COT lifecycle, and RTU FIFO overflow diagnostics. Do not reset a production RTU or replay uncertain controls to obtain a green test.
