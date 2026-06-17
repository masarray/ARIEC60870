# IEC-101 Dual Link Redundancy

IEC-101 Dual Link Redundancy is a dedicated ARIEC60870 master workflow for RTU/outstation profiles that expose two independent serial IEC 60870-5-101 paths.

The feature is intentionally named with protocol-neutral wording. Public code, UI, docs, tests, and reports should use **IEC-101 Dual Link Redundancy**, **active link**, **standby link**, **failover**, **recovery**, **post-switch GI**, and **application image**.

## Design principles

- Link A and Link B have independent transports, link-layer state, counters, timeout state, and FCB values.
- Only the active link owns General Interrogation, clock sync, command dispatch, Class 1 drain, and Class 2 background scan.
- The standby link is supervised without draining Class 1/Class 2 queues.
- Failover is controlled by the redundancy controller, not by the desktop UI timer.
- A promoted standby link runs post-switch General Interrogation according to policy so the application image can be marked ready, partial, or stale.
- Every switchover and recovery decision must create evidence suitable for FAT/SAT review.
- Manual switchover is supported as an operator proof action, but the controller still verifies that the standby link is promotable before ownership changes.

## Engine structure

```text
ARIEC60870.Master.Iec101.Redundancy
  Iec101DualLinkRedundancySession
  Iec101DualLinkRedundancyOptions
  Iec101DualLinkEndpoint
  Iec101DualLinkChannel
  Iec101LinkLayerState
  Iec101ApplicationImageTracker
  Iec101FailoverJournalEntry
  Iec101RedundancySessionSnapshot
```

## Runtime proof actions

The dedicated workspace can queue two runtime proof actions while the session is running:

- **Manual switch** requests an active/standby switchover for FAT/SAT proof. The anti-ping-pong guard is bypassed only for this explicit operator action, but an unhealthy standby link is still rejected.
- **Active GI** queues General Interrogation through the active link only. The standby link remains protected from GI, Class 1 drain, Class 2 background polling, and commands.

Both actions are recorded as evidence events so the generated report can show who requested the proof, which link owned the application layer, and whether post-switch GI refreshed the image.

## Desktop workspace

IEC-101 Dual Link Redundancy has its own workspace. It does not share the single-link IEC-101 workspace layout because the operator must see controller state, active/standby ownership, application image status, and failover evidence at the same time.

## Acceptance checklist

- No standby Class 1 or Class 2 polling by default.
- Commands are routed to active link only.
- Link A and Link B do not share FCB state.
- Active-link timeout can promote a healthy standby link.
- Old active returns as standby after demotion/recovery.
- Failover creates evidence rows and journal entries.
- Post-switch GI status is visible.
- Manual switchover produces `ManualFailoverRequested` evidence before `failover completed` evidence.
- Single-link IEC-101 remains clean and separate.
