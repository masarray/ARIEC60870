# ARIEC60870 v1.2.14 — Line Monitor Compile Fix

## Fixed

- Fixed WPF compile error `CS0103 ProtocolMapHintText does not exist in the current context`.
- Rewired line monitor hint updates to the existing `SelectedLineSummaryText` element.
- Reduced nullable warnings in `Iec103MasterSession` by normalizing nullable mapping values before storing them in retained session models.

## Notes

- No IEC-103 protocol behavior was changed.
- No polling-policy changes were made.
- Line Monitor Pro grouped raw/meaning behavior from v1.2.13 is retained.
