# IEC-101 Common Address observation and command authority

## Source of truth and scope

A wire-level process ASDU may reveal a station Common Address (CA) different from an operator's configured CA. This is **evidence**, not an authorization to change destinations for control, clock synchronization or read commands. The physical FT1.2 frame, CA octets, Type ID, COT and information objects must never be altered.

Single-link `Iec101MasterSession` and dual-link `Iec101DualLinkRedundancySession` use the same bounded `Iec101CommonAddressEvidence` to derive a *GI-only* alternative. The observer does not mutate `Iec103MasterSettings`, has no dependency on GUI/IOA profile display aliases, and retains no unbounded per-station samples.

## Admission / fail-closed contract

- Admit only decoder-validated headers, positive non-TEST, fully decoded standard process ASDUs of TI 1–4, 9–14, 30–31 and 34–36; object count and decoded object count must match and there must be no decode issues.
- Admit only process causes 1–5 and 20–36. Command, activation confirmation, test, private/unknown, malformed/partial and negative evidence cannot steer a transmitted GI.
- Never treat CA=0 or the all-ones broadcast CA for the decoded CA width as an outstation identity.
- Require at least three eligible **ASDU observations** of the *same* alternate CA before one bounded GI retry. More objects within one SQ=1 ASDU do not multiply observations.
- If another eligible CA appears on the session, invalidate auto-selection for that session rather than ranking a dominant station. This includes disagreement between active and standby links.
- Observation may occur on independent receive workers; state updates and decision snapshots are synchronized. No record count grows with traffic volume.
- This is a conservative recovery heuristic, **not** proof of station identity, interoperability, or GI completion. Even a successful link reply does not establish the application command lifecycle.

## Control separation

- The configured CA remains immutable during a session and is the default CA for clock/read/control commands. An operator-explicit CA on a request remains authoritative.
- The observed CA is passed only to the **single explicitly labeled evidence-backed GI retry**. Later startup, manual and post-switch GI use the configured CA unless the operator explicitly specifies another CA. It must **never** rewrite or implicitly override the default CA for Double/Single Command, setpoint, regulating, Read or ClockSync.
- The application should display the observed CA evidence and recommend explicit RTU profile verification. An operator may manually correct setup following site authorization and verification.
- Never merge the unreviewed draft transaction/FCB implementation from PR #145 wholesale.

## Verification and limits

`Iec101CommonAddressEvidenceTests` covers threshold, wrong COT, TEST/negative, partial, multi-station, and configured station observations. `Iec101DualLinkRedundancySessionTests` proves a GI retry can use an observed CA without mutating the configured CA or silently dispatching a breaker command there. The passive monitor COM fault fixture waits for source-and-writer evidence rather than cancelling at a fixed 350-ms deadline.

These are software simulation/regression gates. Field FAT for the real SICAM A8000 at 1200 bps, GI/SBO readback, 60/500/2,000 event FIFO, COM recovery and physical dual-link failover remains **PENDING**. Do not claim IEC conformity or energized-site suitability from CI alone.
