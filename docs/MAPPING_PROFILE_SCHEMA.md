# ARIEC60870 Mapping Profile Schema

ARIEC60870 is a universal IEC-103 master tester. It does not ship built-in vendor signal names.

A mapping profile is a user/project-owned JSON file that maps IEC-103 raw signal address fields to human-readable names.

```text
IEC-103 raw evidence:
  Type ID + FUN + INF + value/DPI + relay timestamp

User mapping profile:
  FUN/INF/type → signal name, group, state map, unit

UI/report output:
  signal name + value + raw FUN/INF evidence
```

## Required Principle

Raw frame evidence remains the source of truth. Mapping profile is only a display layer.

## Minimal Example

```json
{
  "schema": "ariec60870-mapping-profile-v1",
  "profileName": "Project A Feeder 01",
  "deviceName": "Relay Bay 01",
  "projectName": "Substation FAT",
  "createdBy": "User",
  "linkAddress": 1,
  "commonAddress": 1,
  "signals": [
    {
      "id": "bay01.breaker.position",
      "fun": 192,
      "inf": 36,
      "type": "DPI",
      "name": "Breaker Position",
      "group": "Switchgear",
      "description": "User/project validated mapping.",
      "source": "Relay communication database",
      "stateMap": {
        "0": "Intermediate / indeterminate",
        "1": "Open",
        "2": "Closed",
        "3": "Invalid"
      }
    }
  ]
}
```

## Field Meaning

| Field | Meaning |
|---|---|
| `schema` | Must be `ariec60870-mapping-profile-v1` |
| `profileName` | User-facing mapping profile name |
| `deviceName` | Relay/bay/device name |
| `linkAddress` | Optional IEC-103 link address reference |
| `commonAddress` | Optional IEC-103 common address reference |
| `signals[].fun` | IEC-103 Function Type |
| `signals[].inf` | IEC-103 Information Number |
| `signals[].type` | Optional qualifier, e.g. `DPI`, `DPI_RT`, `MEASURAND_I`, `MEASURAND_II` |
| `signals[].name` | Signal name displayed in Value Viewer/Event Log |
| `signals[].group` | UI grouping, e.g. Protection, Switchgear, Alarm |
| `signals[].stateMap` | Maps DPI/raw state to display text |

## Event Log Rule

The Event Log must use relay timestamp, not PC arrival time.

- GI/status frames update Value Viewer.
- A changed state creates an Event Log entry.
- Spontaneous relay events create an Event Log entry even if the previous state is unknown.
- Every Event Log row keeps raw FUN/INF and raw hex for evidence.

## No Vendor Claim

Do not name a profile as a vendor profile unless the mapping is actually validated from that relay/project data.

Good:

```text
ProjectA_Bay01_IEC103.profile.json
Project_Feeder07_Validated.profile.json
```

Avoid:

```text
RelayVendorX.profile.json
SpecificVendor.profile.json
GenericVendorTruth.profile.json
```

unless that file is genuinely maintained from authoritative project/vendor data.

## IEC-101/IEC-104 IOA Signal List — separate schema

The IEC-101/104 Signal List is a separate mapping contract from the IEC-103
FUN/INF profile documented above. Its JSON schema remains
`ariec10x-ioa-profile-v1`. It resolves source-qualified
`CommonAddress + IOA + IEC Type ID` process observations. This profile
calibrates **display only**, not original IEC bytes, IEC timestamp/quality,
GI completeness, command feedback or physical CT/VT values.

### Mutually exclusive engineering scaling modes

| JSON field | Default on legacy profiles | Meaning |
| --- | --- | --- |
| `points[].scalingMode` | `Factor` | `Factor` (legacy multiplier) or `TwoPoint` (input/output endpoints) |
| `points[].scale` | `1` | Factor-mode multiplier |
| `points[].offset` | `0` | Factor-mode additive offset |
| `points[].ratioPrimary` | `1` | Factor-mode optional primary ratio |
| `points[].ratioSecondary` | `1` | Factor-mode positive ratio denominator |
| `points[].normalizedFullScale` | `1` | Factor-mode multiplier for normalized NVA types |
| `points[].x0` | `null` (unconfigured) | First decoded source endpoint |
| `points[].x100` | `null` (unconfigured) | Second decoded source endpoint |
| `points[].y0` | `null` (unconfigured) | Engineering output corresponding to X0 |
| `points[].y100` | `null` (unconfigured) | Engineering output corresponding to X100 |
| `points[].unit` | empty | Display suffix only; never an implicit conversion |

`Factor` preserves the pre-existing behavior exactly:

```text
Y = SourceNumber * (NormalizedFullScale for NVA, otherwise 1)
    * Scale * (RatioPrimary / RatioSecondary) + Offset
```

`TwoPoint` uses the operator-approved source and engineering endpoints:

```text
Y = Y0 + (SourceNumber - X0) * (Y100 - Y0) / (X100 - X0)
```

The two methods **must never be multiplied together**. TwoPoint preserves
but ignores Factor/Offset/Ratio/NormalizedFullScale values; switching back
to Factor restores the legacy formula. Original source values are retained
and reused when the user changes Signal List calibration (including
Event Log/history and Values). Neither mode performs implicit clamping:
an out-of-range source extrapolates.

The source domain depends on the configured measurement Type ID:
- TI11/12/35: signed I16 **SVA** (not an unsigned register).
- TI9/10/21/34: decoded normalized NVA fractional value in [-1,1),
  **not** the original I16 counts.
- TI13/14/36: decoded IEEE-754 R32 source number.

A TwoPoint profile is invalid unless the point explicitly identifies a
supported measured IEC Type ID, uses MonitorOnly, has all four finite
endpoints, has `x0 != x100`, and has a finite slope (reversed and bipolar
spans are allowed). An unknown scaling mode, nonfinite endpoint, zero
source span, command-type calibration or incomplete field is rejected
before Save & Apply. This also prevents an unapproved name-only CA alias
from inheriting a different RTU's calibration. Correct calibration
endpoints **must come from the project**, never from one screenshot.

### Illustrative IEC-101/104 Signal List JSON

This example is **not** a validated real-feeder calibration:

```json
{
  "schema": "ariec10x-ioa-profile-v1",
  "profileName": "Project test measurement range",
  "commonAddress": 105,
  "points": [
    {
      "ca": 105, "ioa": 790447, "typeId": 11,
      "name": "FEEDER REACTIVE POWER (Q)", "unit": "MVar",
      "commandPolicy": "MonitorOnly",
      "scalingMode": "TwoPoint",
      "x0": 0, "x100": 20000, "y0": 0, "y100": 1000
    }
  ]
}
```

For raw TI11 source value 13641, this test mapping gives 682.05 MVar.
The bundled user example intentionally does **not** automatically enable
TwoPoint or invent CT/VT coefficients. The published v3.6.13 binaries
predate this source feature; a later Windows release is required for users.
