# IEC-103 Test Vectors

These sanitized FT1.2 frame examples are used by the dependency-free protocol smoke tests.

They are intentionally small and project-neutral so they can be published with the repository:

- `fixed-no-data.hex` — secondary fixed response with no requested data.
- `class1-event-type1.hex` — time-tagged protection/status message with relay time.
- `identification-type5.hex` — identification response with printable text.
- `gi-end-type8.hex` — General Interrogation end indication.
- `class2-measurand-type9.hex` — simple measurand payload with raw numeric value.
- `unknown-private-type205.hex` — private/unknown ASDU retained for transparent review.

Run them with:

```bash
dotnet run --project tests/ARIEC60870.Protocol.Tests
```

These vectors are not a substitute for relay-specific validation. They are a release guardrail to reduce parser and decoder regressions.
