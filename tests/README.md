# ARIEC60870 Test Suite

ARIEC60870 keeps two layers of automated tests:

1. **Dependency-free protocol smoke tests** in `ARIEC60870.Protocol.Tests`.
   These are console-based and intentionally simple so protocol vectors can run even when a full unit-test stack is not required.
2. **xUnit regression suites** for public release credibility.
   These cover the parser core, IEC-101/104 master helpers, report output, desktop capture contracts, and repository hygiene.

## Test projects

| Project | Purpose |
|---|---|
| `ARIEC60870.Protocol.Tests` | Fast dependency-free protocol smoke tests and sanitized test-vector checks. |
| `ARIEC60870.Core.Tests` | FT1.2 parser, IEC-103 ASDU decode, trace extraction, analyzer findings. |
| `ARIEC60870.Master.Tests` | IEC-101/104 ASDU builder/decoder, IEC-104 APDU parser, settings privacy, assessment policy. |
| `ARIEC60870.Reporting.Tests` | Markdown report structure, privacy sanitization, table escaping, evidence row limits. |
| `ARIEC60870.Desktop.Tests` | Desktop capture JSON contract and evidence-row fallback behavior. |
| `ARIEC60870.Repository.Tests` | Public repo guardrails: version alignment, required files, CI posture, architecture ownership. |

## Local commands

```bash
dotnet restore ARIEC60870.sln
dotnet build ARIEC60870.sln --configuration Release
```

Run the dependency-free smoke test:

```bash
dotnet run --project tests/ARIEC60870.Protocol.Tests/ARIEC60870.Protocol.Tests.csproj --configuration Release
```

Run the xUnit regression suites:

```bash
dotnet test tests/ARIEC60870.Core.Tests/ARIEC60870.Core.Tests.csproj --configuration Release
dotnet test tests/ARIEC60870.Master.Tests/ARIEC60870.Master.Tests.csproj --configuration Release
dotnet test tests/ARIEC60870.Reporting.Tests/ARIEC60870.Reporting.Tests.csproj --configuration Release
dotnet test tests/ARIEC60870.Desktop.Tests/ARIEC60870.Desktop.Tests.csproj --configuration Release
dotnet test tests/ARIEC60870.Repository.Tests/ARIEC60870.Repository.Tests.csproj --configuration Release
```

On GitHub Actions, the CI workflow publishes `protocol-smoke-tests.log`, `.trx` test result files, and coverage collector output under the `ARIEC60870-test-results` artifact.


## Desktop PDF report test

`ARIEC60870.Desktop.Tests` verifies direct PDF evidence report generation through the QuestPDF-backed report service.
