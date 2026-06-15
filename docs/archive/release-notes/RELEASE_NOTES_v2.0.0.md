# ARIEC60870 v2.0.0 — Reliable Lightweight Evidence Engine

## Improved

### Evidence Summary de-noise

- Evidence Summary is now proof-grade instead of a duplicate Class 2 scan stream.
- Analog measurement values are compressed:
  - first proof is shown,
  - significant drift is shown,
  - slow heartbeat proof is shown,
  - small repetitive Class 2 scan changes are suppressed.
- Digital SP/DP and command feedback remain event-grade:
  - exact duplicates are suppressed,
  - real state changes remain visible.

### Value Viewer stays live

- Value Viewer still updates every actual received IOA frame.
- De-noising only affects Evidence Summary, not the live value table.

### Performance

- Reduces high-frequency UI churn caused by repeated analog measurements.
- Keeps Protocol Trace as raw source-of-truth for complete frame evidence.
- Keeps Evidence Summary lean for commissioning/proof reading.

## Why

IEC-101 Class 2 scan can produce frequent small analog updates. Showing every repeated measurement in Evidence Summary makes the product look noisy and heavy. Value Viewer is for live state; Evidence Summary is for proof, changes, warnings, and milestones.
