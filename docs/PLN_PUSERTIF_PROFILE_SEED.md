# PLN PUSERTIF IEC-101/104 Default Seed Profile

The bundled profile:

```text
profiles/PLN_Pusertif_IEC101_default_seed.json
```

is an editable seed database for Indonesian PLN/PUSERTIF-style gateway communication testing. It is derived from the uploaded PUSERTIF form for **Gateway IEC 60870-5-104 to IEC 61850** and includes the same practical test families used in the form.

## Default interoperability parameters

The seed contains these defaults:

| Parameter | Default |
| --- | --- |
| Link address size | 2 octets |
| CAASDU size | 2 octets |
| IOA size | 3 octets |
| COT size | 2 octets |
| IEC-101 speed | 1200 bps |
| Serial format | 8E1 |
| CAASDU main/backup | 105 |
| IEC-101 serial hint | COM21/22 |
| IEC-104 IP hint | 172.21.1.35 |

## Included point classes

The seed includes 27 points:

- `M_SP_TB_1` TSS single-point time-tagged status.
- `M_DP_TB_1` TSD double-point time-tagged status.
- `M_ME_NC_1` measured value short floating point.
- `M_ME_NB_1` measured value scaled value.
- `M_ME_NA_1` measured value normalized value.
- `M_ST_TB_1` step position with CP56Time2a.
- `C_DC_NA_1` double command for RCD.
- `C_RC_NA_1` regulating step command for tap changer control.
- `C_SE_NA_1` normalized setpoint command for RCA.

## Included test scenario metadata

The profile also contains PUSERTIF-style scenario metadata for:

- Telesignal Single.
- Monitoring Link Komunikasi.
- Telesignal Double.
- Telemetering.
- Remote Control Digital.
- Setpoint RCA.
- Control Tap Changer.
- SOE.
- Time Synchronization.
- Pengujian Fitur Komunikasi.

The app uses the points for naming and default setup. Scenario metadata is intentionally stored in the JSON so future validation engines can calculate GI completeness, command feedback checks, SOE pass/fail, timestamp delta checks, and PUSERTIF-style evidence reports.

## Important caution

This profile is a **seed**, not a universal official acceptance record. Copy it and edit it for each actual project:

- Confirm CAASDU.
- Confirm IOA list.
- Confirm command policy: direct or SBO.
- Confirm engineering ranges and units.
- Confirm feedback IOA pairs.
- Confirm real serial ports and IEC-104 IP address.
- Confirm test criteria from the currently approved PUSERTIF/FAT form.
