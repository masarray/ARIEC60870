# ARIEC60870 Slave Simulator User Guide

This guide describes the planned WPF slave/server simulator for ARIEC60870 Protocol Lab. The simulator is designed to validate the master analyzer without physical RTU, gateway, relay, or IEC-104 server hardware.

## Intended workflow

1. Open **ARIEC60870 Slave Simulator**.
2. Choose protocol: IEC-101, IEC-103, or IEC-104.
3. Load the default PLN PUSERTIF profile seed or a project-specific database.
4. Start the slave/server communication endpoint.
5. Open **ARIEC60870 Master Analyzer**.
6. Connect the master to the simulator.
7. Run GI, monitor values/events, and test command behaviour.
8. Change value/quality/timestamp in the simulator grid and verify the master evidence.

## Runtime signal grid

The simulator should allow runtime edits of signal value, quality flags, and timestamp. A user action such as changing a CB double-point from open to close should create a realistic spontaneous event, update the IED timestamp, and make the master analyzer show the same event in Value Viewer, Event Log, and Frame Trace.

## Setup window

The setup window must be protocol-aware:

- IEC-101: COM port, baudrate, 8E1, link address size, link address, CAASDU, COT size, IOA size, ACD/DFC policy.
- IEC-104: bind IP, TCP port, CAASDU, COT size, IOA size, STARTDT/TESTFR policy, t1/t2/t3/k/w.
- IEC-103: COM port, baudrate, link address, FUN/INF table, Class 1/Class 2 behaviour.

## Database editor

The database editor should support add row, delete row, edit row, import JSON/CSV, export JSON/CSV, duplicate check, and profile validation.
