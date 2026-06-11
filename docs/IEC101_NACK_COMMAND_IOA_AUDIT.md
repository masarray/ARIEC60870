# IEC-101 startup NACK and command IOA audit

## Findings

1. Startup NACK at connection open is usually a link-layer response, not an application-layer GI rejection.
   - Some outstations return NACK for optional startup reset / FCB synchronization when the link is already initialized or the reset sequence is not supported by the device profile.
   - The previous engine treated a single-character/link-layer NACK as possible GI negative confirmation. This made the startup evidence look more severe than it should be.

2. Command IOAs were being seeded into Value Viewer.
   - Value Viewer must represent inbound monitor/process information only.
   - Command objects such as C_SC_NA_1, C_DC_NA_1, C_RC_NA_1, C_SE_NA_1, and system commands such as C_IC_NA_1 must remain in Command Dock / Protocol Trace.
   - A command IOA may have the same number range as monitor points, but semantically it is an output object. Showing it as `waiting for GI / scan` is misleading.

## Changes applied

- Link-layer NACK no longer counts as application-layer C_IC_NA_1 negative confirmation.
- Startup reset / FCB NACK is categorized as an Info compatibility note when GI and process data continue normally.
- FCB is no longer toggled after a NACK response.
- Value Viewer seed now includes only monitor/process information types 1..40.
- TX frames and command/control ASDUs are prevented from updating Value Viewer and Event Log.
- Session log now reports how many command/control IOAs were excluded from Value Viewer seeding.

## Operator guidance

If startup NACK appears but GI, Class 1/Class 2 polling, SP/DP, and metering are healthy, do not treat it as a failed session. For a clean RTU profile, disable `Reset Remote Link on connect` and/or `Reset FCB on connect` and rely on GI + normal polling as the startup proof.
