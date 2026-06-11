# Release Notes - v0.4 Real IEC-103 Master Engine

## Added

- Real active master state machine.
- Optional Reset Remote Link command.
- Startup Reset FCB synchronization.
- Optional clock synchronization.
- General Interrogation with bounded Class 1 follow-up.
- SCADA-style Class 2 normal polling cycle.
- ACD-driven Class 1 event drain.
- Class 1 drain stop conditions: NO DATA, GI END, ACD clear, DFC busy, drain limit.
- Timeout burst recovery with optional Reset FCB.
- Response time measurement.
- Master evidence sequence numbering.
- Master state and polling reason on every evidence event.
- Master findings engine.
- Markdown evidence report for active master sessions.
- Expanded CLI options for master behavior.

## Changed

- Product package label moved from v0.3 master foundation to v0.4 real master engine.
- README and architecture documentation now treat ARIEC60870 as an IEC-103 Master Tester first, passive analyzer second.

## Not Included Yet

- WPF UI.
- project-specific FUN/INF profile mapping.
- Full command test suite.
- Slave simulator.
- PDF report.

## Recommended Next Step

Validate v0.4 against a real IEC-103 relay IEC-103 slave through serial/USB-RS485. Once the master evidence is stable, build v0.5 WPF Master Tester UI on top of the engine callbacks.
