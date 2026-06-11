# ARIEC60870 v1.2.5 - Visual Fit + Serial Close Diagnostics

## Fixed
- Contained `SerialPort.Close()` / `SerialPort.Dispose()` driver exceptions so shutdown-related `NullReferenceException` no longer escapes the workflow.
- Added transport close diagnostics to the master evidence stream through a drainable transport diagnostic source.
- Improved WPF header stability so the Session State card no longer visually clips long completion text.
- Increased high-value DataGrid columns and enabled DataGrid text ellipsis/tooltips to avoid abrupt visual clipping while preserving horizontal scrolling for expert trace review.

## Product behavior
- Operator-facing views remain concise.
- Frame Trace keeps full protocol transparency with wide columns and copyable raw evidence.
- Diagnostics remains the escalation surface for serial driver / transport anomalies.

## Notes
- This release does not change IEC-103 polling logic or slave simulator protocol behavior.
