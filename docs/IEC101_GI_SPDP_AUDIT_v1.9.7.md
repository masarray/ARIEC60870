# IEC 60870-5-101 GI / SP-DP Startup Audit

## Evidence conclusion

The captured evidence shows that the startup General Interrogation was sent as `C_IC_NA_1` with `QOI=20` using `Link=105` and `Common Address=105`, but the outstation returned `C_IC_NA_1 COT=7 NEG activation confirmation`. After that, the only process values repeatedly observed were `M_ME_NC_1` measured values using `Common Address=1`. No `M_SP_*` or `M_DP_*` objects are present in the evidence.

This strongly indicates that the serial link address is alive, but the configured IEC-101 Common Address used for station GI is wrong for this test profile. Link Address and Common Address are separate fields. The master addressed GI to CA=105 while the process data came from CA=1.

## Correct master behavior

A robust IEC-101 master must not treat the immediate FT1.2/link-layer ACK as the final command result. After sending GI, it must continue bounded Class 1 draining until it observes the application-layer command lifecycle: positive/negative ACTCON and, when supported, ACTTERM. If station GI is rejected, it should try bounded group interrogation QOI=21..36 before returning to normal Class 2 polling.

SP/DP startup states are valid process values when received as Type 1/2/3/4/30/31 with COT 20..36. They are not necessarily spontaneous COT=3 at startup, so the value viewer and event evidence layer must accept interrogated digital states as startup snapshots.

## Source changes applied

### `src/ARIEC60870.Master/Iec101MasterSession.cs`

- Added GI attempt orchestration that separates link ACK from ASDU-level GI result.
- Added bounded GI follow-up Class 1 drain that watches for `C_IC_NA_1 COT=7` negative ACTCON and `COT=10` ACTTERM.
- Added delayed negative confirmation handling. If `C_IC_NA_1 COT=7 NEG` arrives during Class 1 drain, the master treats station GI as rejected and runs group GI fallback.
- Added group GI fallback QOI=21..36 for station GI rejection.
- Added observed Common Address tracking from received monitor/process ASDUs.
- Added observed-CA station GI retry. If configured CA differs from the CA actually seen in process data, the master retries GI using the observed CA while keeping the same Link Address.
- Added findings for CA mismatch and incomplete observed-CA GI retry.

### `src/ARIEC60870.Desktop/MainWindow.xaml.cs`

- Added startup digital snapshot recognition for IEC-101/104 digital types Type 1/2/3/4/30/31 with COT 20..36.
- Event Log now keeps first-observed SP/DP startup state from GI/group GI instead of suppressing it as an unchanged value.
- Existing spontaneous/return event behavior remains unchanged.

## Field setting for this capture

For the evidence provided, test the next run with:

- Link Address: `105`
- Common Address: `1`
- GI on connect: enabled
- Class 1 follow-up drain: enabled
- Group GI fallback: enabled
- Verify digital Type IDs: `M_SP_NA_1`, `M_SP_TA_1`, `M_DP_NA_1`, `M_DP_TA_1`, `M_SP_TB_1`, `M_DP_TB_1`

## Build note

The sandbox used for this audit does not include the .NET SDK, so the source was not compiled here. Static source checks and brace-balance checks passed. Please build in Visual Studio 2022 / .NET SDK environment.
