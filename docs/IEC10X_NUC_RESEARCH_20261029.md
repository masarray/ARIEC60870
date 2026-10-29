# IEC-101/103/104 Interoperability and Norwegian User Conventions — Research Basis

**Checkpoint:** 2026-10-29. **Classification:** independently authored engineering research, not a conformance certificate. This is the source companion for [PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md) and [ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md).

## Authority, scope, legal safeguards

1. Use the legitimately accessed IEC 60870-5 companion standards, applicable published amendments, and the approved project interoperability list for normative conclusions. Catalogue: IEC 60870-5-101:2003+A1:2015, IEC 60870-5-103:1997, IEC 60870-5-104:2006+A1:2016; a 2026 draft amendment must not be treated as published normative law.
2. **NUC** = Norwegian User Conventions. Historical reference editions: Norwegian IEC 870-5-101 rev.2.0 approved **2000-03-24**; Norwegian IEC 60870-5-104 rev.1.2 approved **2000-02-04**. NUC is an explicitly selected convention, **not mandatory for every IEC-101 installation**.
3. Available NUC101 text is an online third-party reproduction (not official controlled copy). All exact compliance judgments require confirmation against the project-approved original. Do not paste licensed standards tables, vendor manuals or proprietary schematics into this repository.
4. Compare vendor behavior independently; public source/docs use standards language and synthetic cases. Never publish customer traces, device settings, private wiring, competitor product names, or commercial capture formats.

**Research references**
- IEC 101 publication: https://webstore.iec.ch/en/publication/23822
- IEC 60870-5:2026 series catalogue (enumerates applicable editions): https://webstore.iec.ch/en/publication/3755
- IEC 103 publication: https://webstore.iec.ch/en/publication/3745
- NUC101 rev 2.0 transcription (verify against authorized original): https://www.scribd.com/doc/239326949/IEC-101-Norwegian-USer-Convention
- Norwegian RTU procurement specification explicitly naming NUC101/NUC104 versions and 104 frontend topology: https://tekniskregelverk.banenor.no/tekniske-spesifikasjoner/kontaktledning/remote-terminal-unit/
- IEC104 Norwegian interoperability appendix: https://tekniskregelverk.banenor.no/siteassets/tekniske-spesifikasjoner/kontaktledning/dokumenter/fram-agreement-kl-rtu-appendix-interoperability-ice-60870-5-104.pdf
- Independent 101 configuration documentation: https://support.ptc.com/help/kepware/drivers/en/kepware/drivers/IEC608705101/60870-settings.html
- IEC101 FT1.2 examples: https://infosys.beckhoff.com/content/1033/ts650x_tcplc_iec60870-5-10x/11700014987.html

## Findings mapped to design requirements

| Study / section | Verified interpretation | Required system behavior |
|---|---|---|
| NUC101 sec 5.1 | Interoperability list selects alternatives; projects can negotiate additions. | Explicit editable versioned profile; never universal hard-coded NUC defaults or Type blacklist. |
| NUC101 sec 5.1.3–5.1.4 | FT1.2 and balanced/unbalanced alternatives; 0/1/2 link address where lawful; COT/CA/IOA widths are selected for each system. The revision note changed NUC common address guidance. | Record exact original per-frame decoding widths; default vs project override distinguished. Do not globally disallow 2-octet CA. |
| NUC101 sec 6.1.2–6.1.4, 7.1 | Application events from status change, deadband, counter freeze; GI for snapshot; class-2 polling transport mechanism continues. Cyclic application scans are optional project choices. | Routine class-2 no-data is normal; unexpected cyclic traffic is judged against selected project profile only. |
| NUC101 sec 7.1.2 | Event queue overflow may create a separate single-point indication, with IOA 1 recommended. | Never mistake this for QDS OV, BCR carry, DFC, serial driver overrun or numeric representation. IOA meaning requires approved profile/mapping. |
| NUC101 sec 7.2.1 | Unbalanced A/B: status checks both at startup, selected primary reset and poll, standby status-only; switched-to path reset and poll. Event retention relies on valid acknowledgment/FCB progress. | Model independent link state and confirmation/duplicate history; no standby-status false alarm. |
| NUC101 sec 7.2.1 | Full GI after serial line switching / break is explicitly **not required**, but the controlling station may choose it. | Post-switch GI is a **project policy**, not a universal NUC101 requirement. |
| NUC101 sec 7.2.2 | Balanced redundant links use link test and active/passive ACK/NACK behavior and may use last application ASDU to select active. | Balanced policy/state machine **distinct** from unbalanced class poll and link-status interpretation; no current automatic support assertion. |
| NUC101 sec 6.1.7 | SELECT / confirmation / optional break-off / EXECUTE / confirmation / termination; return-state event and ACTTERM order not specified. | Never claim physical operation based solely on link ACK/ACTCON/ACTTERM. Feedback may be before/after ACTTERM; source, phase and expected state required. |
| NUC101 sec 7.8 | Normalized measured/setpoint values require agreed engineering scales. | Preserve raw representation, quality, engineering conversion and user mapping as separate fields. |
| NUC104 rail deployment | Four potential frontend addresses, one active; general polling after frontend change is specified for that deployment. | Do not project 104 host takeover / GI policy onto NUC101 serial line switchover. |

## Layer-specific issues an analyzer MUST NOT conflate

1. **QDS OV:** sender's measurement quality bit. Can be set alongside raw signed value +32755. Being below +32767 does NOT authorize the analyzer to clear sender OV; diagnose effective source/scaling/map by evidence, not assumption.
2. **Signed 16-bit representation:** Type11 scaled value low/high bytes decode +32755 = 0x7FF3, +32763 = 0x7FFB, +32767 = 0x7FFF, -32768 = 0x8000, -32767 = 0x8001. An apparent sign transition is a representational boundary, not proof of a particular gateway bug or modulo reset.
3. **BCR carry:** counter sequence condition, not QDS measurement overflow.
4. **Link DFC:** receiver flow-control/busy condition, not the measurement bit or event buffer indication.
5. **Queue overflow indication:** a separate mapped single-point (NUC101 recommends IOA 1 but project mapping controls interpretation).
6. **UART driver overrun / capture loss:** local observer fidelity problem, not necessarily remote RTU condition.

### Link and command evidence

- IEC101 secondary FC9 **no requested data** is not negative ACTCON; E5 is context-sensitive and cannot prove an application confirmation without a unique matching request. Unexpected A2 should remain visible raw and not be counted as a valid IEC101 NACK without protocol evidence.
- NUC101 §7.2.1 acknowledgment/FCB alternation matters for event delivery/retransmissions. Host receipt completion timestamps from independent USB adapters do not establish strict wire-edge ordering or 33-bit UART idle compliance.
- NUC101 §6.1.7 lets ACTTERM and state feedback event arrive in either order. An analyzer must distinguish unobserved historical ACTCON from a proven missing response.
- The NUC historical day-of-week encoding note must be a **profile diagnostic**, never a global IEC CP56Time2a decoder rejection rule.
- Two receive-only RX taps observe two directions of **one** RS232 full-duplex link. Four such inputs are needed for simultaneous A/B full-duplex observation; electrical isolation and handshake preservation require approved hardware.

## Confidence and sources of truth

**Observed valid:** exact raw bytes, verified integrity, trusted field widths. **Observed unverified:** bytes present but missing/corrupt integrity/width. **Correlated:** unambiguous source-scoped match. **Inferred:** likelihood based on incomplete topology/timing. **Not observed:** missing physical source or time window, never automatically a failure.

## Outstanding protocol and FAT questions

Which approved IEC edition, NUC revision, Type/COT compatibility options, balanced vs unbalanced link procedure, field widths, originator, serial parity/handshake, chosen GI-after-switch policy, class priorities, event queue overflow mapping, process-state feedback IOA and engineering scale are actually configured? Physical tap and external timing reference are required for absolute electrical/bit timing claims. Record answers in project profile and evidence header, never hard-code guesses.

**Traceability:** NUC §5.1 → PROFILE-01; §§6.1/7.1 → EVENT-01/CMD-01; §7.2 → RED-01; §7.8 → MEAS-01; NUC104 frontend practice → RED-01 / IEC104 session separation. See linked PRD/ARD.
