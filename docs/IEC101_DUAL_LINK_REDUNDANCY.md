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

## Recovery and failback policy

The controller treats recovery as a separate state, not as a simple timeout reset. When a standby link reaches the failure threshold, the link is latched as failed and a `RecoveryStarted` evidence event is produced. The link must then pass the configured `StandbyRecoveryGoodResponseThreshold` before the controller emits `RecoveryCompleted` and returns the channel to normal standby supervision.

Failback to a preferred link is intentionally conservative:

- `ManualOnly` is the default and keeps the recovered old active as standby.
- `PreferredLinkAfterStableRecovery` is available for projects that explicitly require automatic return to the preferred path, but it still obeys recovery threshold and anti-ping-pong safety.

This prevents unstable serial paths from oscillating active ownership after a cable, converter, modem, or RTU channel recovers intermittently.

## Runtime proof actions

The dedicated Redundancy workspace can queue manual switch proof while the session is running. The anti-ping-pong guard is bypassed only for this explicit operator action, but an unhealthy standby link is still rejected.

GI, clock sync, read, and control commands stay in the command dock. In dual-link mode, the command dock routes them through the current active link only. The standby link remains protected from GI, Class 1 drain, Class 2 background polling, and commands.

Manual switch, failover, recovery, command blocking, and post-switch GI outcomes are recorded as evidence events so the generated report can show who requested the proof, which link owned the application layer, and whether the application image was refreshed.

## Desktop workspace

IEC-101 Dual Link Redundancy has its own compact **Redundancy** workspace. It does not share the single-link IEC-101 workspace layout because the operator must see controller state, active/standby ownership, application image status, and switchover proof at the same time. Supporting screens are reduced to Values, Events, Trace, and Report for the public release path.

## Acceptance checklist

- No standby Class 1 or Class 2 polling by default.
- Commands are routed to active link only.
- Link A and Link B do not share FCB state.
- Active-link timeout can promote a healthy standby link.
- Old active returns as standby after demotion/recovery.
- Standby recovery is latched and requires consecutive good supervision probes before it is marked recovered.
- Preferred-link failback is manual-only by default; automatic failback is opt-in and still guarded by the anti-ping-pong window.
- Failover creates evidence rows and journal entries.
- Post-switch GI status is visible.
- Manual switchover produces `ManualFailoverRequested` evidence before `failover completed` evidence.
- Single-link IEC-101 remains clean and separate.
