# IEC-101 Stage 2 — Operator intent and link authority (2026-11-16)

Base: master `cb66d815876446fa3e7b69cd56b32b03878a86fb` following merged Stage 1 #219. 
Semantically ported bounded FIFO and generation-aware command admission from older draft #145, **not** the entire stale implementation.

## Behavior
- A shared, lock-protected `Iec101BoundedFifo<T>` provides a 64-command limit for both Master types. Overflow is rejected with an explicit diagnostic; older items remain in order. Dual manual failover requests have a separate limit of 8.
- Single COM session: manual commands wait until an **addressed valid secondary link proof**; physical commands and clock synchronization are rejected if issued before readiness, from a previous proof generation, or after 30 seconds. DFC-busy/NACK and three validated exchange timeouts revoke pending command authority; a later successful poll cannot retroactively authorize it.
- Dual A/B: commands are associated with the active physical name, revocable command-authority epoch, and A/B election generation. A/B promotion or any physical reopen, DFC-busy, negative response or timeout invalidates queued physical commands. Read-only requests from an already owned different active link cannot silently retarget either.
- The existing `IProtocolControlCommandSession.QueueControlCommand` remains callable; no new manual GUI gate, wizard or forced isolation. After fresh verification, newly requested controls still work. All undelivered commands and pending manual failover intents are discarded on session stop; queued extra switches cannot trigger unintended A-B-A ping-pong after promotion.
- This is **command admission**, not proof that a sent physical command was accepted by RTU. An uncertain FC3 delivery remains uncertain; no automatic mutating replay. The current FCB/XID core is unchanged in this PR.

## Verification
- New deterministic bounded FIFO, owner-stamp, pre-proof negative and post-proof positive physical-command tests for single and dual.
- Gate Windows Release build, blocking changed-C# format, xUnit, 19 protocol smoke, CodeQL, hygiene, dependency review on exact head.
- Do not mutate published v3.6.12 artifacts. Physical SICAM 1200-bps, serial COM pull/replug, third-party IEC101 and 60/500/2000 SOE FIFO acceptance remain **unverified field FAT**.

## Remaining P0 engine
The shared transaction/FCB exchange core, late same-service FC11 correlation, physical COM close/reopen/backoff and staged application GI/SBO ownership are still to be independently reconciled from #145. No bulk merge of obsolete source files.
