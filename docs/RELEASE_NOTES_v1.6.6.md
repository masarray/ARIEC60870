# Release Notes v1.6.6 — PLN/Pusertif Seed + Command Dock Pass

## Product direction

This pass moves the product closer to a practical IEC 60870 forensic workspace:

- IEC-101/104 can use a user-editable IOA point profile instead of raw IOA labels only.
- A bundled **PLN Pusertif IEC-101 Default Seed** is provided as a starting profile for Indonesian interoperability-style testing.
- The seed is not hard-coded. Users can copy and edit the JSON profile for any RTU, gateway, utility, project, or global interoperability table.
- A right-side **Command Dock** is always available across workspace tabs, so the operator can issue GI/read/clock/control operations while watching Value Viewer, Event Log, Frame Trace, or Findings in the center.

## Added

### IOA profile seed

Added:

- `profiles/PLN_Pusertif_IEC101_default_seed.json`
- `docs/profiles/PLN_Pusertif_IEC101_default_seed.json`
- `Iec10xPointMappingProfile`

The seed includes editable fields:

- CA
- IOA
- Type ID
- signal name
- group
- signal type
- unit
- scale/offset
- command policy
- state map

### Runtime Command Dock

Added a modern right-side command dock:

- default position: right side
- collapsible to a slim mini-button
- persists expanded/collapsed state in user setup preferences
- stays visible across all center workspaces
- quick operations: GI, Clock Sync, Read IOA
- control command staging: CA, IOA, command type, value, qualifier, select-before-operate

### Runtime command queue

Added a safe runtime queue interface:

- `IProtocolControlCommandSession`
- `Iec60870ControlCommandRequest`

Implemented for:

- IEC-101 serial session
- IEC-104 TCP session

Supported queued commands:

- General Interrogation `C_IC_NA_1`
- Clock Synchronization `C_CS_NA_1`
- Read Command `C_RD_NA_1`
- Single Command `C_SC_NA_1`
- Double Command `C_DC_NA_1`

## Important safety boundary

The command dock queues commands only when an IEC-101/104 runtime session is connected and supports runtime command operation. IEC-103 command operation remains disabled in this build.

For live equipment, command operation must still follow the approved FAT/SAT/site procedure, isolated test boundary, interlocking policy, and confirmed IOA/CA database.

## Changed

- Mapping panel is now visible for all protocols.
- IEC-103 uses FUN/INF mapping profile.
- IEC-101/104 uses IOA point profile.
- Value Viewer and Event Log now use IOA mapping names when available.
- Default IOA seed is copied to output as `profiles/PLN_Pusertif_IEC101_default_seed.json`.

## Known next gaps

- The PLN/Pusertif seed is a starting database, not the final official project database.
- Command validation still needs a dedicated behaviour matrix: ACTCON, ACTTERM, negative confirmation, timeout, wrong CA, unknown IOA, select-before-operate state.
- IEC-104 timer/window enforcement remains the next major forensic state-machine pass.
