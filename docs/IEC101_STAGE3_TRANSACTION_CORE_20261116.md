# IEC-101 Stage 3 — shared per-link transaction/FCB authority

Base: merged Stage 2 master `2b2274009272df62646ca2f63c4ffaf63e80bf3d` (#220) on top of Stage 1 #219. This is a focused transaction port from draft #145, not a merge of its old orchestrators.

## Runtime ownership

- Every physical single COM or dual A/B channel has **exactly one** `Iec101PerLinkTransactionEngine` responsible for the original primary frame, service context, one pending request, validated response, connection epoch and FCB advancement.
- Single-link and dual-channel use identical Begin → MarkTransmitAttempt → SendRaw → MarkAwaitingResponse → ValidateCandidate → Complete flows. Only the engine advances FCB; channel/link-layer FCB values are snapshots.
- A/B channels keep independent transactions and FCB bits. A standby may only send its existing FC9 supervision; no Class1/Class2/GI/controls were added to the standby path.
- The engine invalidates an in-flight exchange on physical epoch end; a reconnect does not inherit outstanding FCB or response authority.
- Incorrect-service, echo, malformed, DFC-busy, FC1 NACK and late status cannot advance FCB. A potentially mutating FC3 with unconfirmed delivery is classified UnknownDelivery; the runtime does not automatically replay it.
- FC7 reset only clears FCB after a positive accepted reply. No blind FCB clearing on timeout.

## Proof

- Imported the isolated golden-vector tests from draft #145 for shared single/A/B response/FCB parity, negative/busy, one pending transaction, stale epochs and unchanged-read retransmission authorization.
- The engine exposes a one-time unchanged **read-only** retransmission API; this slice does **not** enable retransmission in live scheduler. Actual bounded retry and late same-service ambiguity are separate risk-reviewed work.
- Required exact-head Windows Release, formatting, 19 protocol smoke, full xUnit, CodeQL, dependency review and hygiene gates before merge. Never mistake synthetic tests for physical 1200-bps FIFO or matched physical SICAM A8000 FAT.

## Non-goals and release safety

No UI redesign, changed operator command admission, auto GI, physical control retry, clock-sync replay, COM reopen automation, NUC balanced mode or public release. #220's command authority remains preserved by layering on its exact head. Stage 2 #220 has been merged; this focused transaction PR now targets master and requires fresh exact-head CI before merging.
