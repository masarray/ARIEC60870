# ARIEC60870 v1.6.8 — Project Rebrand and Solution Rename

This release locks the product identity as **ARIEC60870 Protocol Lab**. The project is no longer branded as an IEC-103-only tester because the solution now covers IEC 60870-5-101, IEC 60870-5-103, and IEC 60870-5-104 workflows, with the slave simulator roadmap planned inside the same solution.

## Changed

- Renamed solution file from `ArIEC103.sln` to `ARIEC60870.sln`.
- Renamed project folders and project files:
  - `src/ARIEC60870.Core`
  - `src/ARIEC60870.Master`
  - `src/ARIEC60870.Cli`
  - `src/ARIEC60870.Desktop`
  - `tests/ARIEC60870.Protocol.Tests`
- Renamed assembly names and root namespaces to `ARIEC60870.*`.
- Updated WPF application title and header to **ARIEC60870 Protocol Lab**.
- Updated GitHub URLs, release package names, scripts, workflow artifacts, and portable launcher name to `ARIEC60870`.
- Kept IEC-103-specific classes such as `Iec103MasterSession` where the class represents a protocol-specific engine, not the product brand.

## Product direction

The intended solution identity is now:

```text
ARIEC60870 Protocol Lab
├─ Master Analyzer / Client Tester
├─ IEC-101 / IEC-103 / IEC-104 protocol engines
├─ PLN PUSERTIF profile seed
└─ Future WPF Slave Simulator in the same solution
```

## Build commands

```bash
dotnet build ARIEC60870.sln
dotnet run --project tests/ARIEC60870.Protocol.Tests
dotnet run --project src/ARIEC60870.Desktop
```
