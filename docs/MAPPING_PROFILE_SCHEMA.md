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
