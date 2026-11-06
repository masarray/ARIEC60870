# P0 Wire-first passive line monitor — operating contract

Status: implementation milestone staged in Draft PR #95, hardware COM4/COM3 acceptance outstanding. Source-of-truth: actual repository HEAD + CI, not previous test names.

## Zero-configuration operator experience

The operator's normal workflow is **select the already-configured 2- or 4-RX monitor → Start Monitor → watch both independent receive lanes in Raw Frames → select a frame for byte-level interpretation → Save raw or Report**. It is not necessary to understand COT, CA, IOA, link-width, checksum or vendor-specific ASDU to receive and see evidence. An intelligent analyzer automatically identifies validated FT1.2, PRM primary/secondary roles and verified link-address width; it does not silently change COM/plant state.

## Non-negotiable information flow

```text
SerialPort COM4/COM3 read-only taps (no TX or handshake assertion)
  -> separate long-running UART readers
  -> bounded durable-channel handoff
  -> one writer owning original byte offsets, per-COM counts, optional raw journal
       |       | +-> GUI bounded REAL UART sample -> visible in Raw Frames before any decode
       +-> one incremental Core FT1.2 framer per physical COM input
             -> verified candidate / untrusted candidate / noise / idle partial
             -> source-qualified immutable Trace fact (raw HEX is never optional)
             -> optional IEC-101 ASDU interpretation
                  -> Decoded / Unknown-ASDU / Invalid / Partial, never hidden
                  -> independently validated process fact index
                        -> Value snapshot / Event history / GI and Control evidence
```

### Exactly what is a row?

A normal decoded frame shows original timestamp, physical receive lane, logical sender (only if proven), original HEX, FT1.2 fields, Type/COT/CA/IOA and interpretation. **One physical frame = one primary Trace row; multi-object ASDU does not duplicate the frame row**. Clicking the row opens a selection-only byte-true inspector. An unknown, checksum-bad, partial or noise observation also has a primary Trace row with physical port, source byte offset, exact received HEX, a concise reason and an *untrusted* state. A standalone UART read chunk is not misrepresented as an FT1.2 frame: if no frame has completed, show bounded raw chunks in the same Raw Frames workspace so the operator never sees a false blank line monitor. Derived Values and Events are **absent**, rather than guessed.

### Smart automation, with limits

- `All frames` and follow-live are selected automatically on Start, old filters/selection and old epochs are cleared.
- Link width 0/1/2 is verified from checksum-valid physical observations. Auto classification of SCADA/RTU direction uses PRM and complementary port observations, never the user's port dropdown alone.
- Invalid COT, unknown Type and unsupported/vendor-private ASDUs are kept as explicit unknown raw observations. Never guess vendor signal names or quality flags. Source event acceptance fails closed on incomplete ASDU or checksum errors.
- Run processing on bounded reader/writer/background batch. Never per-frame WPF dispatch. UI is an inspectable rolling viewport, not the original source; for long forensic evidence enable and verify full recording.
- Do **not** auto-cycle baud, parity, RTS, DTR, link reset, GI or Class 1/2 polls in passive mode. "Smart" means interpreting observations, not changing a live SCADA connection.

## Acceptance evidence and release gates

1. **Independent physical COMs**: two 1200-bps byte-split replay taps must yield both expected physical TX/RX source frames *before Stop*. Four-stream ordering and partial/gap paths also have independent fixtures.
2. **Every raw observation remains visible**: known fixed, variable with multi-object ASDU, unknown Type with valid FT1.2, truncated Type1, bad checksum, malformed, noise, idle partial, link-width mismatch, A2. No decoder failure may cause a full blank Raw Frames view. No unknown becomes a trusted Value/Event.
3. **Source accounting**: compare original bytes/read counts, framed/untrusted/partial counts, writer-published/consumed UI candidates; explicitly disclose UI preview rollover. Capture is full only if full raw recording was selected and verified.
4. **Operator WPF**: on-screen COM identities, original HEX, summary/unknown reason, select-to-inspect, default All frames, stable 0-reset Event list, no bulky overlay or dynamic animation. 1400 visible simulated frames and 2k/5k/50k mixed bench must report p50/p95 latency/alloc/CPU/memory and buffered counts.
5. **Field gate**: run exact build on known working COM4/COM3 plant tap, both blinking RX adapters, 1200 bps if site configuration requires it. Compare byte counts and original HEX against independent historical working build, capture verified 2k SOE evidence and distinguish RTU FIFO overflow from PC loss. Green CI alone does not prove hardware.
6. **Rollout**: keep Draft PR until actual full-path and field receipt are separately measured; do not promote to master or stable on green tests alone.

This specification is original engineering work based on the general public concepts of independent raw and interpreted line monitoring. Do not reuse commercial manual text, proprietary algorithms or product-specific assets.
