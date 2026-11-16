# IEC-10x Stage 7C — connection-epoch command association and feedback trust

2026-11-16. Base `master` SHA `51996d8faf2aade793c8a7f29c29c83543ad0aae` after Stage 7B PR #227.

## Observed implementation gaps

1. The IEC-104 command casebook indexed protocol/source/CA/IOA/TI/OA but had
   no TCP connection identity. Aggregated live or archived evidence from
   consecutive connections could cross-match a new ACTCON or process value
   with an old command.
2. A feedback point arriving later in processing could still carry an older
   host capture sequence/time. A response predating the latest request could
   be promoted by matching CA/IOA and COT alone.
3. CP56Time2a quality is distinct from SIQ/DIQ. A substituted or time-invalid
   SOE event with valid DPI could be interpreted as positive command feedback.

## Implementation contract

- Generate one opaque GUID for each `Iec104ClientSession.RunAsync` connection
  attempt. Stamp all emitted IEC-104 events in `AddEvent`, including the
  additional objects of a multi-object ASDU. Persist through the existing
  capture record and restore to the typed observation; no second decoder.
- Bind the evidence casebook key and mapped feedback ownership to the exact
  IEC-104 connection instance. Keep the existing per-COM physical identity
  for IEC-101 and legacy empty-ID archive compatibility.
- Never promote out-of-order ACTCON or feedback at/before the accepted execute
  sequence. Compare **host observed UTC only to host observed UTC** where
  available; UnixEpoch marks legacy capture time unknown.
- Do not turn CP56Time2a into assumed UTC. Invalid/substituted/unexpected
  reserved bits/inconsistent supplied weekday gate off positive feedback;
  retain raw bytes and explain the unverified status.
- Bounded 128 active cases / 240 history / 24 stages unchanged. No new
  telemetry buffer, retry, extra RX/TX, timer or UI mode.

## Tests

- Different TCP IDs with identical CA, IOA, TI and qualifier cannot share ACTCON
  or feedback; two active associations keep independent case outcomes.
- Offline captured TCP ID survives authoritative raw decode and casebook lookup.
- Feedback predating EXECUTE ACTCON by host sequence or arrival time remains
  unverified; later valid feedback still progresses.
- A replayed ACTCON predating TX cannot authorize selected or direct control.
- CP56Time2a time IV/substitution cannot become a positive process proof.
- Existing command/IEC-101 recovery/GI, WPF capture, smoke and CodeQL CI remain
  mandatory; physical devices are a separate FAT requirement.

## Not yet proven

- Real multi-vendor interoperability, independent IEC-104 reconnect soak,
  IEC-101 CP8050/CI8551 at 1200 bps, 60/500/2,000 end-to-end SOE parity
  or RTU FIFO safety. No public release or certification claim.
