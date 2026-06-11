# Generic Relay Demo Mode

ARIEC60870 v0.6 includes an internal simulated IEC-103 slave for product validation and regression testing.

## Purpose

Demo mode is not a full relay simulator. It exists to validate the master workflow:

1. Open endpoint.
2. Reset FCB.
3. Optional clock sync.
4. General Interrogation.
5. Bounded Class 1 follow-up.
6. DPI event decoding.
7. GI END detection.
8. Return to Class 2 background polling.
9. NO DATA behavior after queue drain.

## Desktop

Open `ARIEC60870.Desktop`, set **Target Mode** to:

`Generic relay demo - simulated slave`

Then click **Start Test**.

## CLI

```bat
dotnet run --project src\ARIEC60870.Cli -- master --simulate --duration 10 --report out\demo-master-evidence.md --json out\demo-master-evidence.json
```

## Important limitation

The simulator is a deterministic test harness. It is intentionally small and should not be presented as a complete IEC-103 slave implementation.
