# Rebranding to ARIEC60870

The project has been renamed to **ARIEC60870** because the product scope now covers more than IEC 60870-5-103. The application currently contains protocol-aware workflows for IEC 60870-5-101, IEC 60870-5-103, and IEC 60870-5-104, and the next major direction is a WPF slave/server simulator inside the same solution.

## Naming rule

Use **ARIEC60870** for product, repository, solution, package, and shared namespace naming.

Use protocol-specific names only for protocol-specific classes, for example:

```text
Iec101MasterSession
Iec103MasterSession
Iec104ClientSession
Iec10xAsduDecoder
```

This avoids the old design smell where the whole product looked like an IEC-103 application even when the active workspace was IEC-101 or IEC-104.

## Solution direction

```text
ARIEC60870.sln
├─ ARIEC60870.Core
├─ ARIEC60870.Master
├─ ARIEC60870.Desktop
├─ ARIEC60870.Cli
├─ ARIEC60870.Protocol.Tests
└─ ARIEC60870.SlaveSimulator.Desktop    planned
```

The slave simulator project should reuse the same profile database, ASDU builder/decoder, timestamp encoder/decoder, and type catalog used by the analyzer.
