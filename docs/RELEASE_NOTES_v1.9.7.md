# ARIEC60870 v1.9.7 — IEC101 Auto ASDU CA Learning

## Root Cause Addressed

Evidence showed GI was sent using configured/profile CA=105, while live process data from the outstation arrived with ASDU CA=1. Sending station GI to the wrong ASDU common address can produce negative confirmation and prevent digital SPS/DPS snapshots from being returned.

## Changed

### IEC-101 master session

- Learns dominant ASDU common address from valid RX ASDU traffic.
- If observed CA differs from configured CA, retries station GI using the observed CA.
- If observed-CA station GI is still negatively confirmed, performs bounded group interrogation QOI=21..36 using the observed CA.
- Continues bounded Class 1 drain + adaptive Class 2/background polling.
- Does not overwrite Value Viewer placeholders from GI verdicts.

### Desktop diagnostics

- Adds `IEC101-RUNTIME-CA-MISMATCH` warning when live process data CA differs from setup/profile CA.
- Value Viewer still maps values by IOA where possible.
- GI status becomes diagnostic evidence; actual received IOA frames remain the source of truth for Value Viewer values.

## Why

Common Address of ASDU is a separate application-layer address. Link address and ASDU CA can differ. GI must target the correct ASDU CA. If measurements arrive from CA=1 but GI is sent to CA=105, the tester is interrogating the wrong application address.
