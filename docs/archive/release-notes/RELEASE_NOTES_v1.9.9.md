# ARIEC60870 v1.9.9 — Clean IEC101 Startup and Value Viewer Seed

## Fixed

### Startup NACK

- IEC-101 no longer forces `Reset FCB on connect`.
- `Reset FCB on connect` default is now off.
- For IEC-101, the Reset FCB checkbox is disabled and cleared in the setup UI.
- Routine startup link-layer NACK from reset/sync traffic is filtered out of Evidence Summary attention noise.

Many IEC-101 RTUs answer NACK to startup reset/sync function codes. This is link-layer startup behaviour, not digital data failure. FCB reset is still available internally for timeout recovery.

### Command IOA in Value Viewer

- Command IOAs are no longer seeded into Value Viewer.
- Type IDs 45..51 and command policies such as `DoubleCommandRemoteOnly`, `RegulatingStepRemoteOnly`, and `SetpointNormalizedRemoteOnly` are treated as command points.
- Value Viewer is reserved for monitor/feedback/process values.
- Command points remain in Signal List / Command Dock, not Value Viewer waiting rows.

## Behaviour

- Value Viewer expected rows now contain only real monitor/feedback IOAs.
- Command IOA rows no longer show `waiting for GI / scan`.
- Startup connection should no longer show routine Reset FCB NACK when using IEC-101 default flow.
