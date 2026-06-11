# ARIEC60870 Troubleshooting Guide

This document focuses on practical IEC-103 field symptoms and likely checks.

## No response from relay

Check in this order:

1. Confirm the COM port is correct.
2. Confirm baudrate, parity, data bits, and stop bit.
3. Confirm IEC-103 link address.
4. Confirm common address.
5. Confirm RS-485 A/B polarity.
6. Confirm only one master is active on the serial segment.
7. Try startup with **Reset FCB** enabled.
8. Increase timeout for the first test.
9. Check TX counter: if TX is zero, the session is not sending.
10. Check RX activity: if RX is zero, suspect wiring, adapter, port, or relay setting.

## Many checksum errors

Likely causes:

- wrong baudrate;
- wrong parity;
- noisy serial wiring;
- bad USB-to-serial or RS-485 adapter;
- missing or unsuitable termination;
- serial line shared with another active master.

Recommended action:

- validate the physical layer first;
- reduce cable length for bench test;
- use a known-good adapter;
- verify relay serial settings from its front panel or configuration file.

## Malformed frames or random bytes

Likely causes:

- wrong serial settings;
- noise or half-duplex direction issue;
- adapter driver problem;
- another device transmitting on the line.

ARIEC60870 attempts to resynchronize the FT1.2 stream after noise bytes, but persistent malformed traffic should be treated as a physical-layer or serial-setting problem first.

## Relay answers once, then stops

Possible causes:

- wrong link state after previous master activity;
- relay expects a clean reset sequence;
- timeout too aggressive;
- FCB sequence recovery needed after communication errors.

Recommended action:

- enable **Reset FCB** at startup;
- run a short test with conservative timeout;
- disconnect other masters;
- stop and restart the session after confirming address/settings.

## General Interrogation does not complete

Check:

- relay supports GI on the configured address;
- common address is correct;
- timeout and GI follow-up limits are not too low;
- Diagnostics tab for DFC busy, timeout, or malformed responses.

## Class 1 events are not visible

Remember: Class 1 is event/high-priority data. It should not be hammered continuously.

Check:

- whether the relay actually advertises ACD=1;
- whether a real event was generated;
- whether GI follow-up is bounded too tightly;
- whether the relay event is mapped or only visible as raw FUN/INF.

## Values appear but names are not readable

This is usually a mapping issue, not a protocol failure.

Check:

- mapping profile is loaded;
- FUN/INF values match the relay/project signal list;
- state map contains expected DPI/value values;
- unmapped rows still show raw FUN/INF protocol evidence.

## Evidence report contains unexpected information

Before sharing outside the project team:

- review project names;
- review relay address and serial settings;
- review raw frame evidence;
- review mapping profile file names;
- remove customer-sensitive comments if added manually.

Public evidence export is designed to avoid full local workstation paths by default, but engineering evidence can still contain project-specific information.
