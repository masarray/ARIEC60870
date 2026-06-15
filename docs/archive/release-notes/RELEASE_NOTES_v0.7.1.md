# ARIEC60870 v0.7.1 - Superseded Universal Profile Correction

v0.7.1 removed vendor-specific naming, but still kept a profile concept that could confuse product direction.

v0.8 supersedes it with a cleaner architecture:

- ARIEC60870 does not ship built-in signal mapping
- users create/import mapping profiles
- the protocol decoder stays universal
- Event Log uses relay timestamp and records state changes/edge events
