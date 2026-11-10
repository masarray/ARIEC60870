# IEC-101 T02 — One Safe Unchanged Read Retransmission

**Layer:** stacked implementation on draft PR #145 (shared active single/A-B transaction core).
**Requirement:** IEC101-R02/R03, fixture T02; grade A software fixture only.
**State:** candidate pending exact-head CI and actual physical 1200-bps FAT.

## Transaction invariant

- The shared Iec101PerLinkTransactionEngine grants exactly one retry permit for an awaiting fixed read FC9, FC10 or FC11 when the pending exchange has had no accepted or rejected secondary candidate. Silence is required: malformed, wrong-service, echo, NACK or addressed busy data does not get blindly retransmitted.
- The same pending object holds identical OriginalTransmit bytes, original FCB and physical source + connection epoch. No new exchange admitted until completion. The engine increments retransmit count only on an authorized transmit attempt; unauthorized double-marking throws.
- Single-link and dual-channel orchestrators retry through the same core admission API. The second physical TX is separately labeled in evidence and incremented in TX counters. Link failure/timeout accounting happens once only after the final unanswered attempt; success can advance FCB once, never twice.
- Variable FC3 (GI, clock sync, Read Command, setpoint/Select/Execute) stays PotentiallyMutating: **no automatic retransmission** after uncertain write even when the application command is logically a read. Stop/reconnect invalidates permits by owner epoch/cancellation.
- No existing decoded/received frame is deleted. No UI-owned protocol state, no retransmission after an out-of-context FC11, no assumed link-health proof from unaddressed E5.

## Automated acceptance

- Same pending FC9/FC10/FC11 retry preserves byte[] and FCB across single/A/B fixture identities; full response after retry advances FCB at most once.
- Negative fixture: mutating FC3, wrong FC after Class1, unauthorized second MarkTransmitAttempt, and reconnection epoch reject retry.
- Runtime no-RX 1200-bps session: first FC9 replay is byte-identical, trace labels physical retry and one terminal timeout per exhausted exchange, GI remains unsent.
- Existing dual-link failover, busy/ACD and serial 1200-bps tests stay green. WPF UX PR #147 remains independent.

## Remaining field risks (NOT solved by synthetic software proof)

- L11/T04: late same-service response belonging to a prior **completed** exchange cannot be distinguished from current merely by FC/link address; preserve ambiguity and do not claim solved.
- L14/P05: COM write/read cancellation and driver unplug/reopen, transport lifetime and synchronous serial driver faults still need bench evidence.
- S04/S05/T08 and P02: staged GI/command outcomes and 60/500/2000 SOE independent field grade pending.
- Repetitions increase wire load; initial profile may opt out in a future controlled configuration gate. Never enable mutating command retransmission by generalizing this read guard.

**Software acceptance does not certify SICAM A8000 interoperability or any production deployment.**
