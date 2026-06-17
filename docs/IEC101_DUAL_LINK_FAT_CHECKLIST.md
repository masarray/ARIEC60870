# IEC-101 Dual Link Redundancy FAT Checklist

Use this checklist to prove that IEC-101 dual-link operation is safe before connecting the master to a production RTU/outstation.

## Preconditions

- Link A and Link B use separate serial paths or separate converter channels.
- Both endpoints use the intended IEC-101 link address and common address.
- Standby Class 1 and Class 2 polling remain disabled unless a project-specific interoperability document explicitly allows it.
- Commands, GI, clock sync, Class 1 drain, and Class 2 background scan are owned by the active link only.

## Startup proof

| Check | Expected evidence |
| --- | --- |
| Open both links | `ChannelOpened` appears for Link A and Link B. |
| Active elected | Controller shows one active link and one standby link. |
| Standby supervision | `LinkStatusRequested` / `StandbySupervisionConfirmed` appears without Class 1/Class 2 polling on standby. |
| Startup GI, if enabled | GI TX occurs on active link only; application image becomes Ready or Partial. |

## Manual switch proof

| Check | Expected evidence |
| --- | --- |
| Press Manual switch | `ManualFailoverRequested` appears. |
| Promote standby | Failover start/completion shows old active and new active link names. |
| Post-switch image refresh | `PostSwitchGiStarted`, RX GI data, and ACTTERM evidence appear when supported by the outstation. |
| Command route after switch | Commands route only to the newly active link. |
| Old active demotion | Old active is shown as standby and only supervised. |

## Failure proof

| Check | Expected evidence |
| --- | --- |
| Break active serial path | Active timeout count increases until threshold. |
| Healthy standby promotion | Controller switches active ownership to standby. |
| Anti-oscillation | Automatic failover inside stabilization window is rejected; manual switch remains explicit proof action. |
| Restore old active | Old active returns as standby after recovery, not as active owner. |

## Report acceptance

The report is acceptable when it can show:

- active link before switch;
- standby link before switch;
- failover reason;
- failover latency;
- post-switch GI status;
- application image object count;
- command route after switchover;
- no standby Class 1/Class 2 drain evidence.
