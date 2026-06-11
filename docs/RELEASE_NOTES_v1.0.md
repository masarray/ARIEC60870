# ARIEC60870 v1.0 - Field MVP Assessment Layer

## Added

- AutoTest-style master session assessment.
- Overall assessment status and score.
- FAT/SAT-oriented checklist covering communication, frame quality, GI, polling policy, timing, value acquisition, event timestamp quality, mapping coverage, and findings.
- WPF **AutoTest Assessment** tab.
- CLI assessment summary and checklist output.
- Markdown report **AutoTest assessment** section.
- `docs/AUTOTEST_ASSESSMENT.md`.

## Product direction

v1.0 keeps ARIEC60870 focused as a universal IEC-103 active master tester and analyzer:

- Active master to one IEC-103 slave relay.
- controlled polling policy.
- User-defined mapping profile.
- Relay timestamp event log.
- Evidence-first engineering diagnosis.

## Not included yet

- PDF export.
- Full test scenario builder.
- Slave simulator as a formal product feature beyond the existing demo transport.
- Vendor-specific built-in signal mapping.
