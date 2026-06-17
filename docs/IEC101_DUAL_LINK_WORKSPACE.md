# IEC-101 Dual Link Workspace

The IEC-101 Dual Link workspace is designed for active/standby operations, not for ordinary single-link polling.

## Layout

Top cards show:

- controller state;
- active link;
- standby link and last supervision age;
- application image, recovery summary, failover count, and failback policy.

The evidence grid below focuses on redundancy-specific evidence, including link status supervision, active timeouts, manual switchover requests, failover start/completion, standby recovery probes, recovery completion, command routing, and post-switch General Interrogation.

## Operator proof actions

The controller card includes two small actions that are available only for the dedicated dual-link workflow:

- **Manual switch** queues a controlled active/standby ownership change. Use this during FAT/SAT to prove that the standby link can be promoted and that post-switch GI refreshes the application image.
- **Active GI** queues General Interrogation through the current active link only. It is intentionally not sent on the standby link.

These actions do not make the UI the owner of redundancy logic. The workspace only queues the request; the engine still owns standby health checks, promotion, rejection, post-switch GI, and evidence generation.

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
