# IEC101-P0 — First-Connect GI loss / missing GI conversation (21 Nov 2026)

**Scope:** Active unbalanced IEC 60870-5-101, single COM and A/B. No change to passive RX, IEC103/104 protocol engine, user data, signal calibration, serial RTS/DTR, FC0/FC9 semantics, address widths, remote controls or physical device settings.

## Operator observation vs proven causes

On a real IEC101 COM1 / 1200-bps / Link=105 / CA=105 screenshot of the public v3.6.19 GUI, Events=26, NO DATA=1 and GI workspace=0/0. Those counters show received process observations and an empty GI conversation view, **not original evidence of whether TI100/COT6 was transmitted**. The operator expects the configured first-Connect General Interrogation automatically, after verified secondary link and no DFC busy.

Code audit identifies three independent ways that an intended GI can appear missing:

1. **Cross-protocol saved preference:** `GiCheckBox` was one shared preference across IEC101/103/104. A previous mode's GI OFF could silently become IEC101 OFF on protocol switch; Master receives `SendGeneralInterrogationOnConnect=false` and deliberately sends no GI. The old JSON cannot prove whether a saved IEC101 OFF was explicit: preserve the saved-mode choice, scope future overrides per active protocol, default IEC101 ON for fresh/other-mode states, and display the effective policy at Connect rather than covertly overriding OFF.
2. **Single COM epoch restart before TX:** `RunTransportEpochAsync` recomputed `pendingStartup = !reopened && (...GI...)`. A physical open/clock-sync/DFC fault **before the first GI wire attempt** caused automatic COM recovery with `reopened=true`; it could then poll Class2 forever without ever issuing the approved startup GI. The fix preserves first-Connect GI intent until the first actual `WriteAsync` attempt. After that point delivery is ambiguous and GI must **not** be replayed automatically. Clock sync, reset, SBO/controls are never replayed on recovery.
3. **Dual A/B pending loss and ACD starvation:** Early active-COM absence discarded `_startupGiPending`; physical link fault also unconditionally cleared it. If still pending after subsequent FC9 proof, Class1 ACD drain had priority over startup GI. The fix retains not-yet-attempted startup GI over COM restart, initiates it once on a genuinely healthy active owner (not standby, not DFC-busy), and prioritizes it over steady-state ACD polling. An already attempted GI remains consumed; no automatic duplicate.

## Behavioral acceptance (what should happen)

- **Explicit ON, first Connect:** acquire active COM, apply configured reset policy if appropriate, obtain accepted addressed secondary proof (FC9 if needed), obey DFC, then send one TI100 / COT6 / QOI20 station GI. A link ACK is not ACTCON; collect typed ACTCON COT7, COT20 data and ACTTERM COT10 separately. Once GI has been attempted, do not silently replay it after a COM fault even if no ACTTERM was observed.
- **Initial COM unavailable:** recover physical COM with existing bounded backoff, acquire fresh FC9 secondary proof, then send the **still-unsent** first GI once. No hidden FC0/FC7 reset, clock sync or operator control replay on new epoch.
- **Explicit OFF:** never auto-send GI. The GI tab explains the effective OFF and how to enable it in Setup, rather than displaying only 0/0 with no reason.
- **Passive IEC101:** always 0 TX. IEC103/104 retain their independently configured defaults and OFF choices.
- **Link unverified or DFC-busy:** do not send GI; preserve pending first-Connect intent, provide original frame/diagnostic truth. GI tab prompts inspection of raw TI100/FC9/DFC evidence; never claim success from Events count.
- **A/B:** only healthy verified active source may issue startup GI. Failover/post-switch GI remains governed by configured policy; no silent control or GI replay from an already attempted older owner.

## Regression vectors

- Desktop: fresh active IEC101 GI ON, passive zero-TX, other-mode legacy OFF does not transfer, saved IEC101 explicit OFF persists, overrides independent for IEC101/103/104.
- Single runtime: absent COM on initial Connect then reopen → one GI TX and ACTTERM; clock-sync driver fault before GI → no sync replay, one GI/ACTTERM; GI already attempted before COM loss → **no** duplicate GI after reopen.
- Dual runtime: initial active A COM absent then recovered while B is healthy → one startup GI from verified active and ACTTERM; GI OFF → none; no standby GI.
- Existing negative: unverified secondary → no GI; DFC-busy → FC9 supervision only; no Class1 storm, A/B owner isolation, source CA/QOI matching and cancellation.

**Release gate:** This document is a root-cause/code/test workstream, not a public hotfix or physical FAT claim. Require exact PR head Windows build, focused xUnit, source tests, CodeQL, hygiene, browser checks where applicable. Only create a new candidate/package after all automated gates pass; retain immutable v3.6.19 tag. The screenshot alone is not sufficient to certify RTU wire behavior or claim which of the three failure paths occurred at the operator's station.
