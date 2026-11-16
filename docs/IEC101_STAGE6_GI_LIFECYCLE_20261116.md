# IEC-101 Stage 6 — source/CA/QOI-qualified runtime GI lifecycle

Date: 2026-11-16. Base master: `25cc2658d9defe40fcc9e9cf044fe33e9532ad3a` after Stage 5 #224. Scope: one shared GI application truth gate; single- and dual-link use the same rules, not vendor-specific hidden modes.

## Root cause

Previously, every valid-looking TI100/COT10 seen during IEC-101 receive processing incremented single/dual `GiEndResponses`. The A/B application image tracker also moved to Ready on raw TI100/COT10 even if it did not belong to an initiated GI, was on standby, used a different CA/QOI or lacked ACTCON. This can prematurely stop Class1 follow-up, produce misleading GI completeness and influence optional post-switch GI policy.

IEC 60870-5-101 COT 6 is activation, COT 7 activation confirmation (positive or negative), and COT 10 activation termination. The application command lifecycle is distinct from FT1.2 FC0/E5 link ACK. Device interop references: Weidmüller IEC 60870-5-101 interoperability documentation and Kepware IEC 60870-5-101 COT table.

## Architecture

- Add one small `Iec101GiLifecycleTracker` for every physical COM reader/transaction owner, using the **existing** canonical `Iec10xAsduDecode` and qualifier. No additional raw parser, scheduler or command path.
- Start a pending GI lifecycle **only after a real TX write** of decoded TI100 COT6 (IOA 0, one valid object, QOI 20..36). This proves TX attempt, not RTU acceptance.
- On service-validated secondary RX, correlate positive/negative COT7 and COT10 to active request by common address, originator, QOI and physical channel epoch. TEST and corrupt/incomplete ASDUs never advance state. Negative ACTCON clears request, repeated overlapping same-target GI marks ambiguous, ACTTERM without positive ACTCON stays incomplete.
- Only source-matched COT10 *following positive ACTCON* increments `GiEndResponses`, so Class1 draining cannot stop on unrelated or orphan ACTTERM.
- Bounded group-GI fallback telemetry now calls FC0/E5 a **link reply**, not “group accepted”; only source/CA/QOI-matched ACTCON proves application acceptance. No additional automatic retries or polling introduced.
- A/B application image cannot become Ready on uncorrelated TI100/COT10. Only the active channel may contribute active image/CA observations; standby remains FC9 supervised and cannot certify GI. Image Ready denotes **correlated GI application lifecycle termination**, not 100% point coverage.
- On COM open/close or role promotion/demotion, the per-channel GI authority resets; no previous A/B owner intent is borrowed. Retain independent A/B FT1.2 FCB and #220 command epoch safety.
- No auto resend of GI after physical COM reopen, no automatic FC0/FC7/clock-sync or controls, no new poll pressure, and no change to public v3.6.12.

## Proof and limitations

Software golden vectors cover station/group GI, common address, originator, qualifier, test bit, negative ACTCON, incomplete ACTTERM, same-target overlap, different-group retry, A/B isolation, physical epoch reset and image readiness. Exact SHA Windows Release, formatter, smoke, xUnit, CodeQL and WPF acceptance required before merge.

A positive ACTCON plus matching ACTTERM ends the **observed application GI lifecycle**, not the physical equipment state or full mapped-point coverage. If a vendor omits ACTCON, retain trace evidence and classify incomplete rather than guessing success. If a vendor sends a stale identical CA/QOI terminal after an older GI attempt, IEC-101 contains no application transaction number to prove identity; repeated/overlapping requests are fail-closed but physical 1200 bps FAT still required.

Outstanding: complete source-qualified SBO execute ACTCON/ACTTERM/target feedback validation, multi-vendor independent peer, 60/500/2000 SOE parity at actual 1200 bps, actual USB COM pull/replug and 7SJ/SICAM device-profile FAT.
