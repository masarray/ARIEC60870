# Release Notes - v0.3 Single-Connection Master Foundation

## Main direction change

ARIEC60870 is now aligned as an **IEC-103 Master Tester** first, not only a passive analyzer.

Target:

```text
Single active IEC-103 master connection -> protection relay IEC-103 slave
```

## Added

- New `ARIEC60870.Master` project.
- New active master session engine.
- Serial COM transport using `System.IO.Ports`.
- Clean-room FT1.2 frame builder.
- Reset FCB command.
- GI command.
- Clock sync command.
- Class 2 normal/background polling.
- ACD-driven Class 1 drain.
- Live TX/RX evidence events.
- CLI `master` command.
- JSON master evidence export.
- `RUN_MASTER_SAMPLE.bat`.
- Reference study document for IEC101MasterTester single-connection product pattern.

## Kept

- Existing offline analyzer command.
- Existing Markdown/JSON report export.
- Apache-2.0 clean-room policy.
- GitHub hygiene rules.

## Intentionally excluded

- Dual redundancy.
- NUC active/standby orchestration.
- Slave simulator.
- external protocol stack/dissector dependency.

## Important note

This is the first active master foundation. It should be tested with a lab relay or serial simulator before field usage.
