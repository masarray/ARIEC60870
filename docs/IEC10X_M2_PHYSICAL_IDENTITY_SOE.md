# M2 — Physical point identity, received-event facts and evidence references

2026-10-29 · ARIEC60870 / IEC-101 & IEC-104. Builds on merged M1 `6688363`.

## Operator outcomes

- **Values**: the same IOA from Link A and Link B or different common addresses is no longer silently overwritten. Physical identity is `protocol / capture channel / CA / IOA`; the source channel appears compactly beside the IOA (no extra wide grid).
- **Signal mapping**: an exact CA+IOA+Type matching user point can supply its friendly name. A mismatch does **not** silently borrow an IOA-only label. Explicit wildcard CA/Type in a user profile is allowed. Actual unexpected CA remains visible unmapped for diagnosis.
- **GI completeness**: logical *expected profile* CA+IOA keys are tracked separately from physical Values keys. A response from another CA does not satisfy a configured station's GI coverage. Source-specific snapshots do not inflate one logical GI point into two.
- **Event Log**: an actually received, valid digital spontaneous/return-information object (COT 3, 11, 12) is a preserved observation even for the first occurrence or a repeated state. Reason distinguishes *first observation*, *repeat observation*, and *state change*. Cyclic/GI no-change digital snapshots are not converted into SOE events.
- **Provenance**: each event carries lightweight source-channel metadata and a session-local reference. IEC-101 passive source stream and original byte offset + information object index can be used for exact raw journal verification. An Active Master sequence number is not a physical byte offset. The journal remains read-only and verification must still occur before any raw record is claimed authenticated.
- No new polling, timers, busy loops, COM writes, or duplicated WPF decoder. No new grid columns or expensive per-frame formatting.

## Identity contract

`Iec10xObservationIdentity.ValueKey` is pure and bounded, using protocol, physical source, CA and IOA. No type in the key because time-tagged and untimed ASDU variants can update the same logical process value from the same physical source. The full Type ID and Quality remain on the evidence. Unknown CA is distinct from known CA=0. A channel name is normalized but not inferred from an IOA.

For active IEC-101/104 without a separately identified redundant channel, source is `SINGLE` within the active session. In IEC-101 A/B runtime channel names are explicit, and passive streams 0/1 map to Link A, streams 2/3 to Link B. No cross-session/global uniqueness is claimed without a persisted session/archive identifier.

## Regression gates

1. Identical IOA but different protocol, channel, CA or IOA must produce distinct keys; whitespace/case variants of the *same* channel normalize.
2. COT 3, 11, 12 received digital observations remain facts even without baseline/state transition; GI COT 20, outbound TX and command ASDUs must not be mislabeled as spontaneous.
3. A passive source reference retains the original stream, byte offset and object index; Active Master references carry sequence only.
4. Existing IEC-101/104 active, dual-link, passive, GI and event behavior must retain all established tests; Release Windows + protocol smoke + xUnit + CodeQL + hygiene must succeed on the same head before merge.
5. Real SICAM/ETERA and multi-day loading still require FAT. No sampled-value SV transport was added: that is ARStack61850, not ARIEC60870.

## Remaining M2/M3 scope

Full durable Frame→ASDU→Object ID persistence across `.ariec`, command SELECT/EXECUTE/ACTCON/ACTTERM pairing, command feedback proof, stable historical drill-down, and identical report fields are follow-ups. Do not describe this physical identity/SOE checkpoint as full forensics parity with IEC-Test.
