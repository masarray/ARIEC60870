# IEC-101 full-workflow CA/GI recovery & A/B ownership — 2026-11-11

**Application behavior:** ARIEC60870 remains a directly usable IEC-101 active Master in normal configured single-link and dual A/B workflows. **No lab mode, no feature-disable toggle, no extra readiness wizard, no forced user isolation or mandatory operator confirmation was added.** This implementation is a protocol correctness refinement, not a restriction on the application.

## Actual defect and fix (IEC101-R09/R10, S04/S09)

1. Previous single/A-B scheduler ran an automatic observed-CA GI retry after seeing two process ASDUs from a different Common Address, even when **no GI had ever been requested** in the session. Now automatic one-shot observed-CA GI recovery occurs **only after a configured startup, operator or project-selected post-switch GI request actually ran**. Disabled automatic GI still permits FC9 link status, Class 1/2, spontaneous and cyclic observations, as well as deliberate operator GI.
2. Previous dual-link session wrote observed CA directly into `BaseSettings.CommonAddress` and redirected subsequent physical control commands through `ApplyLearnedCommonAddress`. Configuration, result/report snapshot and command destination now remain the original configured CA unless a command explicitly includes its own CA. The existing evidence-backed GI retry can still use observed CA where authorized.
3. A/B owner promotion clears old owner's learned CA and retry evidence. The new active physical link starts with its configured CA and builds independent process evidence before further GI recovery. No false cross-link identity borrowing.
4. CA learning considers valid decoded process objects, excluding invalid, negative or command ASDUs, rather than treating arbitrary ASDU traffic as proof.
5. No change to normal configured startup GI, manual GI, Class1/2, operator controls, clock, physical link reset parameters, RTS/DTR, profile selection, GUI operation or supported IEC103/104 behavior.

## Regression acceptance

`Iec101DualLinkRedundancySessionTests` asserts:
- Single-link with startup GI disabled but wrong observed process CA: no unsolicited GI, while real Class2 event reception continues.
- A/B with GI disabled: active Class2/event flow continues; no unsolicited GI.
- Positive configured startup GI: observed CA can still provide a bounded GI recovery while the configured CA in report and controls remains unchanged.
- Operator-requested physical control after GI CA learning is sent to its configured CA.
- Failover after owner A learned a different CA: required post-switch GI on owner B uses B's own configured CA rather than A's old CA.

## Evidence and limitations

Exact-head CI validates deterministic simulated protocol behavior, not the physical driver, actual SICAM A8000 1200-bps SOE throughput or certified third-party interoperability. The application has full normal active features available without a special lab mode. Physical electrical isolation is the responsibility of the environment/operator, separate from correct link transaction ownership and diagnostic evidence.

No public RC is modified by this draft. The separate unresolved tasks remain Windows serial Stop/COM recovery, same-service intrinsically ambiguous delayed replies, richer source-qualified GI/SBO outcome evidence and real hardware FIFO endurance.

## Owner-scoped GI authorization and post-switch opt-out

The global `GiCommands` counter is evidence, **not** a transferable authorization. A startup/manual GI on active A must not let process traffic on promoted B bypass `PostSwitchGiPolicy.Disabled` or `ManualOnly`. The runtime now resets the GI-intent flag alongside owner-specific CA learning on each promotion. A configured or deliberate GI on B independently establishes permission for one evidence-backed GI recovery; Class1/2 and normal commands remain available without extra UI steps. An explicit A→B fixture exercises disabled post-switch GI while B still returns Class2 process data.
