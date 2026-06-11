# ARIEC60870 v1.7.0 — Command Lifecycle + Mapping UX Proof Pass

This release fixes high-impact usability and protocol-validation issues reported from IEC-101 field/demo testing.

## Command Dock

- Replaced the generic queued command UI with explicit command lifecycle buttons:
  - `Select Open`
  - `Operate Open`
  - `Select Close`
  - `Operate Close`
- Regulating command mode now changes the action buttons to:
  - `Select Lower`
  - `Operate Lower`
  - `Select Raise`
  - `Operate Raise`
- Setpoint mode now exposes separate `Select Setpoint` and `Operate Setpoint` actions.
- Command status wording now says `Issued priority command` instead of `Queued`, because runtime commands are treated as operator-priority actions ahead of normal background polling.
- The UI intentionally allows mismatched test sequences such as `Select Open` followed by `Operate Close` so engineers can observe and validate slave/server behaviour.

## PLN PUSERTIF IOA Mapping

- Added a dedicated `Signal List` workspace and left-rail button.
- The signal list shows IOA, CA, Type, Group, Class, COT, command policy, and command-to-feedback binding.
- Value Viewer, Event Log, and Operator Evidence now use the default PLN PUSERTIF seed names more aggressively.
- IOA resolution now falls back to IOA + Type ID when the live device/simulator returns a CA different from the profile CA. This keeps the UI readable while preserving CA mismatch as a forensic condition.

## Timestamp / Arrival Time UX

- PC arrival time is hidden from operator-facing grids by default.
- Event Log and Value Viewer focus on `IED/RTU time` so users do not mistake PC receive time for device timestamp.
- Event Log no longer prepends the PC date when the decoded relay/IED timestamp already includes a date.

## Header / Table Usability

- Header indicator chips are now fixed-width pill cards to prevent jitter as counters change.
- Scrollbar thumb minimum size increased for long sessions so the thumb remains usable even with many rows.

## Validation

Sandbox validation performed:

- XAML XML parse
- JSON seed parse
- C# brace balance
- ZIP integrity

Full compile still requires Windows/.NET SDK:

```bash
dotnet build ARIEC60870.sln
dotnet run --project tests/ARIEC60870.Protocol.Tests
dotnet run --project src/ARIEC60870.Desktop
```
