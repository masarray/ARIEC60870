# ARIEC60870 Diagnostics Policy

ARIEC60870 is a field tool. Runtime faults must not disappear into unhandled exceptions or modal noise while the operator is testing a relay.

## Product rule

All recoverable runtime problems must be converted into diagnostic evidence:

- serial read/write exception
- serial timeout burst
- transport close warning
- mapping profile load error
- frame quality warning
- checksum/malformed frame warning
- master session fault
- UI-level session exception

The desktop app must keep the session recoverable whenever possible and preserve the detail in a selectable Diagnostics row.

## UI behavior

Diagnostics rows are shown in the **Diagnostics** tab with:

- time
- severity
- source
- code
- message
- detail
- recommendation
- exception type / stack detail

Rows are selectable and can be copied using **Copy Row** or **Copy Detail**, similar to copying an Error List row in Visual Studio.

## Serial timeout handling

A normal no-data condition on the serial port is not treated as a fatal exception. The serial transport avoids blocking `SerialPort.Read` timeout exceptions as the main read mechanism. It polls `BytesToRead` inside a bounded timeout and returns `0` for normal no-data cases.

Real transport errors are still captured and sent to diagnostics.

## Engineering intent

The operator-facing workflow remains:

1. run master test,
2. observe Value Viewer / Event Log / Operator Evidence,
3. inspect Diagnostics when something is wrong,
4. copy a diagnostic row for escalation/debugging.

Raw frame evidence remains available in Frame Trace; diagnostic detail remains available in Diagnostics.


## v1.2.5 layout/diagnostics note

- Keep operator views readable first; raw hex stays in Frame Trace and Inspector.
- Wide protocol columns, horizontal scroll, ellipsis, and tooltip are preferred over visually clipped text.
- Serial close/dispose driver exceptions must be converted into Diagnostics evidence, not allowed to interrupt Stop/Close workflow.
