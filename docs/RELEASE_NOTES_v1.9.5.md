# ARIEC60870 v1.9.5 — IEC-101 Group Interrogation Fallback

## Fixed

Station interrogation QOI=20 negative confirmation was being treated only as a Class 2/background fallback. That is not enough for many RTU profiles because SPS/DPS static status may be returned only through interrogation groups.

## Changed

- If station GI `C_IC_NA_1` QOI=20 is negatively confirmed, the master now tries group interrogation fallback QOI=21..36.
- For each accepted group interrogation, the master performs bounded Class 1 drain.
- After group fallback, the master continues adaptive Class 2/background sweep.
- Negative group confirmations are recorded but do not stop the scan.
- Value Viewer remains non-destructive: actual IOA frames are the only thing that update values.

## Why

IEC-101 supports station interrogation and group interrogation. A station-wide GI rejection does not prove SPS/DPS values are unavailable. Many SCADA/RTU profiles rely on interrogation groups for digital status collection.
