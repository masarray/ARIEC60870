# IEC-101 Stage 1 — cross-thread reconciliation (2026-11-16)

Base: `829b6fcdf77bece420a23eca1ea973d0601ff544`. Focused reimplementation from draft #145; **do not merge/rebase #145 wholesale**.

## Consolidated authority

- Merged #215/#218 already introduced the shared session-scoped fail-closed CA policy and revoked stale dual-link GI targets. Retain **one** production class (`Iec101ObservedCaPolicy`) instead of importing #214's duplicate `Iec101CommonAddressEvidence`.
- Port #214's safety improvements semantically: bounded **three complete process ASDUs**, width-aware CA/global broadcast rejection, thread-safe dual-link observations, and permanent fail-closed multi-CA conflict until session epoch reset. Three observations remain advisory, not physical station certification.
- Valid process traffic alone **cannot authorize an unsolicited GI**. Only an actually sent startup, deliberate operator, or selected post-failover GI establishes permission for one optional evidence-backed retry.
- A/B promotion revokes old-owner CA observations, cached advisory CA and GI permission; implicit manual/post-switch GI always uses configured CA, while explicitly selected operator CA remains honored. Learned CA is restricted to the explicitly labeled recovery GI.
- In uninterrupted ACD Class 1 bursts, schedule a due Class 2 poll on the same healthy physical active owner and continue standby FC9 supervision. Never send Class 1, Class 2, GI or physical controls to standby.

## Regression evidence, not hardware certification

- Port draft #145 deterministic perpetual-ACD fake-peer fixtures for single, A/B and periodic standby FC9.
- Port the GI-disabled single/A-B negative cases and extend CA evidence tests for invalid global address, mixed stations, and concurrent RX.
- Exact-head Windows build, mandatory changed-C# formatting, xUnit, protocol smoke, WPF acceptance, CodeQL, hygiene and dependency review must pass before merging.
- Real SICAM A8000 1200-bps burst FIFO, matched physical COM, external peer, interop, 60/500/2,000 SOE and A/B failover remain **FIELD FAT PENDING**.

## Residual #145 implementation work

- Shared per-link FCB/transaction authority with exchange-id and link epoch, immutable control delivery outcome, safe late FC11.
- Physical COM driver error reopen and failover with bounded backoff, stale control invalidation, and no blind startup-action replay.
- Complete source-qualified GI/SBO lifecycle, bounded read retransmission and original TX/RX evidence.
- Independent synthetic SOE accounting and actual physical 1200-bps acceptance.

Do not merge remaining draft code without reconciling every current-master source and invariant. #214 becomes eligible for non-merged closure after this focused integration and its exact-head checks pass; preserve it as historical review context. No released `v3.6.12` tag, installer or download asset is changed.
