# ARIEC60870 Troubleshooting Guide

This guide focuses on practical IEC 60870 communication symptoms and likely checks for IEC-101 serial, IEC-103 serial, and IEC-104 TCP/IP sessions.

## No response from device

Check in this order:

1. Confirm the selected protocol mode.
2. For serial, confirm COM port, baudrate, parity, data bits, stop bit, and RS-485 polarity.
3. For IEC-104, confirm host, port, firewall, routing, and that the server allows a client session.
4. Confirm link address and common address.
5. Confirm only one active master/client is using the endpoint when the device does not support multiple clients.
6. Try startup with **Reset FCB** enabled for IEC-101/103.
7. Increase timeout for the first test.
8. Check TX counter: if TX is zero, the session is not sending.
9. Check RX activity: if RX is zero, suspect wiring, adapter, network path, port, or device settings.

## Many checksum errors or malformed serial frames

Likely causes:

- wrong baudrate;
- wrong parity;
- noisy serial wiring;
- bad USB-to-serial or RS-485 adapter;
- missing or unsuitable termination;
- half-duplex direction-control issue;
- another device transmitting on the line.

Recommended action:

- validate the physical layer first;
- reduce cable length for bench test;
- use a known-good adapter;
- verify device serial settings from its front panel or configuration file;
- try a conservative timeout and polling interval.

## IEC-104 connects but no data appears

Check:

- STARTDT confirmation is visible;
- common address matches the server profile;
- GI is enabled when a startup snapshot is needed;
- I-format frames are received after STARTDT;
- TESTFR behavior is visible during idle periods;
- server permits the selected client IP/session count.

## General Interrogation does not complete

Check:

- device supports GI on the configured address;
- COT size, CA size, and IOA size match the interoperability profile;
- timeout and GI follow-up limits are not too low;
- Diagnostics/Findings for DFC busy, timeout, ACTCON negative, malformed responses, or missing ACTTERM.

## IEC-101/103 Class 1 events are not visible

Remember: Class 1 is event/high-priority data. It should not be hammered continuously.

Check:

- whether the device advertises ACD=1;
- whether a real event was generated;
- whether GI follow-up is bounded too tightly;
- whether the event is mapped or only visible as raw FUN/INF or IOA;
- whether Class 2 starvation or low-baud timing is affecting polling fairness.

## Values appear but names are not readable

This is usually a mapping issue, not a protocol failure.

Check:

- mapping profile is loaded;
- FUN/INF or IOA values match the project signal list;
- state map contains expected digital values;
- analog scale expectations are documented externally;
- unmapped rows still show raw protocol evidence.

## Commands do not produce expected feedback

Check:

- selected command type matches the device profile;
- select-before-operate behavior is configured correctly;
- command IOA and feedback IOA are both present in the mapping profile;
- COT, negative confirmation, ACTCON, ACTTERM, and command-return information are visible in the trace;
- interlock, local/remote, bay mode, or device supervision conditions are not blocking execution.

## Evidence report contains unexpected information

Before sharing outside the project team:

- review project names;
- review relay/RTU/gateway address and serial/TCP settings;
- review raw frame evidence;
- review mapping profile file names and labels;
- remove customer-sensitive comments if added manually.

Public issue reports should use sanitized frames, synthetic examples, or screenshots with project identifiers removed.
