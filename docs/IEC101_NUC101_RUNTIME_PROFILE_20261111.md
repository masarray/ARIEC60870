# IEC-101 Historical NUC101 — Runtime Profile Integration (PROFILE-01 / RED-01)

**Date:** 2026-11-11. **Status:** coded runtime policy + synthetic tests; **not** NUC conformance, independent-peer or hardware FAT.
**Tracker:** [P0 #143](https://github.com/masarray/ARIEC60870/issues/143); [verification matrix](IEC101_RELIABILITY_VERIFICATION_MATRIX.md).
**Implementation lineage:** reconciled draft [PR #145](https://github.com/masarray/ARIEC60870/pull/145).
**Applicable normative standard:** [IEC 60870-5-101:2003+A1:2015](https://webstore.iec.ch/en/publication/23822). Historical Norwegian User Conventions IEC 870-5-101 rev 2.0 (2000-03-24) must be checked against the project-authorized original; [research/limitations](IEC10X_NUC_RESEARCH_20261029.md). Vendor-neutral evidence, no copied standards text.

## Prior failure and actual fix

The Setup convention selector already resolved a versioned generic / historical NUC101 / imported project profile, checked IEC-101 active-mode field widths, and stored a report fingerprint. But the WPF dual-link session factory unconditionally set `PostSwitchGiPolicy = Required`, regardless of the selected `GiAfterSwitch` field. Selecting NUC101 had **no behavioral effect** on post-switch GI.

The session factory now applies its *already-selected, validated* profile to `Iec101DualLinkRedundancyOptions`; the options map into the **existing** `RunPostSwitchGiPolicyAsync` runtime, rather than adding a second GI scheduler. The exact policy is logged at session startup.

| Selected profile choice | Actual dual-link runtime | Interpretation |
|---|---|---|
| Generic IEC101 default: `OnFailover` | `Required` | Keeps the application's previous default for backward-compatible UI sessions, **not** a universal standard requirement |
| Historical NUC101 rev2.0 default: `Disabled` | `Disabled` | Post-switch GI is not mandatory under the historical convention; image may remain stale |
| Imported project: `Manual` | `ManualOnly` | Operator must explicitly request GI |
| Imported project: `OnFailover` / `ProjectRequired` | `Required` | Project-selected post-switch GI; not inferred from vendor or historical label |
| Imported project: `Disabled` | `Disabled` | Do not silently transmit GI on link switch |

Unknown policy, mismatched protocol, unsupported active balanced IEC101 or mismatched widths must be rejected **before** changing runtime policy or sending frames. The existing active-link ownership, independent A/B FCB, standby FC9, RTU DFC busy gating and command non-replay safety remain authoritative.

## Explicitly not implemented or verified here

- Historical NUC101 **full** interoperability, device-specific class mapping, SOE overflow IOA mapping, NUC command lifecycle, and redundant-link reset/ack sequence have **not** been certified. The profile changes only supported runtime GI policy.
- Active IEC101 balanced mode is **not implemented**; historical NUC101 balanced redundancy needs a separate state machine and tests.
- No automatic RTS/DTR/baud/handshake change, remote reset, GI on startup override, or clock synchronization inferred from the NUC label.
- NUC104 frontend takeover/GI is **not** applied to IEC101 serial switching.
- The profile is not a vendor conformance badge. Neither CI nor test simulator proves SICAM A8000/IEC_Test physical parity.

## Deterministic acceptance

Master xUnit tests cover generic retained default, historical NUC101 no-GI, every allowed imported project GI policy, wrong link width, wrong IEC protocol and unsupported balanced mode. Existing dual-link runtime tests separately cover `PostSwitchGiPolicy.Required` and `Disabled`. For release, repeat shared single/A-B invariants, matched physical COM FAT and profile-tied post-switch wire captures, including zero *unsolicited* GI in Disabled/Manual modes.

### Dual physical link-address safety

For any active A/B profile, both endpoint link addresses must be representable in the configured 1- or 2-octet FT1.2 link field **before** starting serial communication. One-octet out-of-range values are rejected, not truncated to another station address; 2-octet valid distinct addresses remain supported for explicitly configured links. This is a generic safety check, **not** a claim that a particular NUC deployment supports disparate station addresses.
