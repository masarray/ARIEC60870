# ARIEC60870 v1.1 - Operator Output and Performance Buffer

## Added

- Operator-first evidence messages for users who do not read raw hex.
- Separate advanced Frame Trace tab for raw protocol transparency.
- Protocol meaning and operator action fields in evidence events.
- WPF event queue and timed batch renderer to avoid per-frame UI rendering.
- Bounded UI rolling windows for evidence trace, relay event log, findings, and session notes.
- Engine-side retained evidence memory limits:
  - `MaxRetainedEvidenceEvents`
  - `MaxRetainedRelayEvents`
  - `MaxRetainedFindings`
- Report counters for retained evidence drops.
- Output/performance governance document.

## Changed

- Main live tab is now Operator Evidence, not raw-first Live Evidence.
- Raw hex remains available in the selected evidence panel and Frame Trace tab.
- Event Log remains relay-timestamp based and edge/state-change oriented.
- Evidence report explains that raw retained trace may be bounded for long sessions.

## Product principle

ARIEC60870 must be usable by relay/FAT engineers who need readable results, while still exposing enough raw protocol detail for protocol experts and R&D review.
