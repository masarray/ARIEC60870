# ArSee v3.6.20-rc.1 — IEC-101 first-Connect GI reliability (public test candidate)

**Windows x64 · prerelease candidate · not stable and not RTU-certified.**
This patch is the isolated field-test build for the operator-observed v3.6.19 startup General Interrogation failure. The existing [v3.6.19 public stable](https://github.com/masarray/arsee/releases/tag/v3.6.19) remains unchanged and its tag/assets must never be overwritten.

## What was actually observed

On a live IEC-101 link, the v3.6.19 screenshot showed COM1 at 1200 bps, Link=105, CA=105, 26 Events, 1 NO DATA, and an empty GI workspace (0/0). The screenshot does **not** include original GI TX/RX frames or persisted Setup GI policy, so it alone cannot prove which of the distinct fault paths occurred. Any claim of physical GI repair requires a new original-frame capture.

## Corrected application behavior

- **Isolated automatic startup GI setting per protocol.** A saved GI OFF in IEC-103/104 no longer turns off IEC-101 startup GI on switching modes. Fresh active IEC-101 defaults to ON; an explicitly saved IEC-101 OFF remains OFF. Active IEC-103/104 retain independently saved operator choices. Passive IEC-101 remains RX-only (zero transmitted frames).
- **Preserve first Connect GI across an early physical COM failure.** If the initial COM open or clock-sync fails before the first TI100/COT6 attempt, the first-Connect GI remains eligible when the recovered COM proves its addressed secondary link. The engine never automatically resends any GI once the driver has begun its FC3 write attempt, because that delivery may be ambiguous. There is no automatic clock-sync, FC0, FC7, physical control or previous-session command replay on recovery.
- **IEC-101 dual A/B initial active recovery.** Keep a never-attempted first-Connect GI pending even if the first active COM was unavailable, dispatch it once on a freshly verified, non-DFC-busy active owner, and do not let continuous routine ACD Class-1 drain starve it. Standby must not transmit GI.
- **Operator evidence/diagnosis.** A GI tab with zero cases now explains whether the *effective* startup GI preference was OFF or ON with no observed source-qualified GI; Connect session log states the effective setting, and the operator is directed to raw TX and the link/DFC diagnostics. The GI casebook still requires actual TI100/COT6, ACTCON COT7 and ACTTERM COT10 evidence; Event count or link ACK are not GI completion.
- **No change** to original raw RX bytes, signal mappings, scaling, link/CA/COT/IOA configuration, passive zero-TX guarantee, Class 1/2 polling policy, physical redundancy ownership, or authorized remote controls.

## Deterministic verification

The source patch is [PR #303](https://github.com/masarray/arsee/pull/303), merged at `b793e88e2e5fde63e87df454b153c022c68ca3a6`. Windows PR build, xUnit including initial COM open failure and clock-sync driver fault before GI, no replay after an attempted GI, dual active A recovery, real WPF acceptance, CodeQL, hygiene and dependency checks passed. This candidate is a different version/source and must independently pass **its own exact-source Package verification**, installer checksums, SPDX SBOM and provenance. The public release remains prerelease unless real user FAT results are independently reviewed.

## Field FAT — NOT TESTED in this release

On an **authorized isolated RTU/test bench**, close any previous ArSee session, save the existing profile and choose IEC-101 active (not Passive), serial COM/1200 bps/8E1 and validated Link/CA/COT/IOA address widths. Explicitly turn ON **General interrogation on connect** in Setup, Connect once and collect:
1. Original verified FC9 addressed secondary response and DFC not busy.
2. One TX **TI100 / COT6 / QOI20** from the active link (record source/epoch/HEX and timestamp).
3. Matching positive **TI100 / COT7 ACTCON**, requested CA/QOI; record negative or absent response faithfully.
4. Returned data (normally COT20 station interrogation), **TI100 / COT10 ACTTERM** if sent, and independent point coverage verification. No fake completion from a link ACK.
5. Reconnect after an approved simulated COM-open failure: if no GI TX had been attempted, one delayed GI is expected. If a GI TX attempt already occurred, **no automatic retransmission** is allowed; report delivery/coverage as uncertain instead.

This prerelease has not been proven to detect all RTU hardware behavior, serial driver bugs, true FIFO event age, 2-/4-channel independent hardware, or energized-switchgear control results. Do not use it as an IEC conformance/production SCADA certificate.
