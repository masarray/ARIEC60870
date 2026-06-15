# ARIEC60870 v0.6 - Demo Relay and Product Workflow Upgrade

This release strengthens ARIEC60870 as a real IEC 60870-5-103 Master Tester product, not only a parser or UI shell.

## Added

- Internal generic IEC-103 relay demo / simulated slave transport.
- WPF target mode selector:
  - Serial COM - real relay
  - Generic relay demo - simulated slave
- CLI `--simulate` mode for regression/demo runs without relay hardware.
- Demo batch launcher: `RUN_DEMO_MASTER.bat`.
- Simulated relay behavior:
  - ACK for reset/clock/GI commands
  - ACD indication after GI/background condition
  - bounded Class 1 event queue
  - DPI(TM)
  - DPI(RT)
  - Identification response
  - GI END
  - normal NO DATA after queue drain
- Master engine fix: NO DATA with ACD=1 is now preserved as access demand, so a Class 2/background response can correctly trigger bounded Class 1 event drain.

## Product direction

ARIEC60870 is focused as a single-connection IEC-103 Master Tester connecting to protection relay slaves. It uses Class 2 as the normal scan and drains Class 1 only when ACD=1 or inside a bounded GI follow-up window.

## Why this matters

The simulator makes the project testable before field hardware is available. It also gives a stable regression path for the polling policy that differentiates ARIEC60870 from noisy Class 1 bombardment patterns.
