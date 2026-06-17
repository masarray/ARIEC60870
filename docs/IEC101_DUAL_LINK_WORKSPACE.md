# IEC-101 Dual Link Workspace

The IEC-101 Dual Link workspace is designed for active/standby operations, not for ordinary single-link polling.

## Layout

The release layout is intentionally compact:

- a single health strip for controller state, active link, standby probe age, image state, and command gate;
- one Link A card and one Link B card with role, state, port, last RX, timeout, ACD/DFC, and FCB;
- one Image/Switch card for application image and last switchover proof;
- one filtered redundancy timeline.

The timeline is not a dump of all evidence rows. It only shows redundancy decisions and proof events: manual switch requests, failover start/completion/rejection, standby timeout/failure, recovery milestones, command blocking, image stale/ready transitions, and post-switch GI result. Routine supervision and normal Class polling belong in Trace, not here.

## Operator proof actions

The Redundancy workspace exposes only link-ownership actions:

- **Manual Switch** queues a controlled active/standby ownership change. Use this during FAT/SAT to prove that the standby link can be promoted and that post-switch GI refreshes the application image.
- **Open Report** opens the report workflow and refreshes the proof preview.

GI, clock sync, read, and control commands remain in the command dock so the operator has one command path. In dual-link mode, that command path is routed through the current active link only.

These actions do not make the UI the owner of redundancy logic. The workspace only queues the switch request; the engine still owns standby health checks, promotion, rejection, post-switch GI, and evidence generation.

## Separation from single-link IEC-101

Single-link IEC-101 remains the right workspace for normal one-port RTU/outstation testing. Dual-link mode has a separate setup path because it needs two serial endpoints and different operator mental model.

## Evidence expected during manual switch proof

A clean manual switchover proof should show this sequence:

```text
ManualFailoverRequested
IEC-101 failover started
post-switch GI started, unless policy disables it
GI activation termination observed, when the outstation sends ACTTERM
IEC-101 failover completed
```

If the standby is not promotable, the controller must reject promotion and keep the current active ownership rather than sending commands or polling through an unsafe path.


## Recovery evidence

A clean recovery sequence after an active-link failure should show:

```text
IEC-101 failover started
IEC-101 failover completed
RecoveryStarted on the old active link after standby supervision failures
RecoveryProbeSucceeded until the configured good-probe threshold is met
RecoveryCompleted when the old active is safe as standby again
```

The workspace must not automatically steal active ownership back unless the engine is configured with the explicit preferred-link failback policy. The default operator workflow is controlled manual switch proof.
