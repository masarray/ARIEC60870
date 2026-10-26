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

## SICAM A8000 IEC-101 forensic diagnostics

In unbalanced IEC-101 mode, RX primary frames (PRM=1), wrong secondary link
address, and malformed FT1.2 frames never mark a link healthy or advance
FCB. The receiver retains the original transaction response-time budget even
if the serial driver temporarily returns a zero-byte read.

- `IEC101-RX-TX-ECHO`: RX matches the most recent TX on the same COM port.
- `IEC101-RX-DELAYED-TX-ECHO`: RX matches an older TX in the bounded
  eight-frame history. A match is a symptom, *not* proof of physical loopback.
- `IEC101-RX-FOREIGN-PRIMARY`: RX PRM=1 with a different link address.
- `IEC101-RX-UNEXPECTED-PRIMARY`: RX PRM=1 and not in recent TX history.
- `IEC101-RX-LINK-ADDRESS-MISMATCH`: genuine secondary address differs.
- `IEC101-RX-INTEGRITY`: malformed/truncated/checksum error.

Setup has explicit COM flow control (None, RTS/CTS, XON/XOFF) and RTS/DTR
output settings. Defaults remain None, RTS=on, DTR=off to preserve existing
three-wire compatibility. When RTS/CTS is selected, the serial driver owns
RTS. Settings persist in setup preferences.

Live `IEC60870-SERIAL-OPEN-PROFILE` and
`IEC60870-SERIAL-CLOSE-PROFILE` diagnostics include requested serial
settings and locally observed CTS/DSR/DCD, when the adapter supports those
inputs. These *do not* prove that the outstation responded.

Rejected frame counts remain complete. To prevent UI flooding, full
diagnostic detail is retained for the first four events and then power-of-two
occurrences. Report totals are not sampled. Diagnostics show the stable
error code and preserve raw TX/RX, port, mode, PRM, expected/received address
and cause of rejection.

Use `docs/IEC101_SICAM_A8000_FIELD_FAT.md` for isolated field testing.
