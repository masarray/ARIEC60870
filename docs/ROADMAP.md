# ARIEC60870 Planned Improvements

ARIEC60870 is focused on IEC 60870-5-103 active master testing, relay communication troubleshooting, and reviewable protocol evidence. The roadmap below describes the user-visible improvements planned for future releases.

## 1. Easier release packages

Planned outcome: users can download a Windows portable ZIP, verify the checksum, run the desktop app, and follow the quick-start guide without opening the source project.

Current status:

- Windows portable package workflow is available.
- SHA256 checksum file is generated with release assets.
- Quick Start and Troubleshooting guides are included.
- GitHub Pages landing page links to releases and documentation.

## 2. Stronger validation evidence

Planned outcome: users can see which simulator, bench, and relay scenarios were checked for each release.

Current status:

- Public validation matrix is available for simulator, bench, and sanitized relay tests.
- Sanitized IEC-103 frame test vectors are available in `samples/test-vectors/`.
- ASDU decoder smoke tests cover Type 1 event, Type 5 identification, Type 8 GI end, Type 9 measurand, and private/unknown ASDU transparency.
- FCB retry checks cover timeout and invalid checksum recovery.

Planned next items:

- Add more known-good relay captures when they can be sanitized safely.
- Add relay busy / DFC validation cases.
- Add long-duration Class 2 polling validation notes.
- Add clearer notes for baudrate, parity, link address, GI, Class 1, Class 2, and measurand behavior.

## 3. Better operator workflow

Planned outcome: first-time users can complete a relay communication check with less protocol guesswork.

Planned items:

- Save/load connection profiles.
- Recent profiles for frequently used relay settings.
- Serial health diagnostics: RX/TX activity, timeout rate, checksum error rate, malformed frame rate, and likely wrong-address symptoms.
- Guided test checklist: connect, reset link, GI, Class 2 polling, Class 1 event observation, evidence export.
- More actionable troubleshooting messages.

## 4. FAT/SAT evidence package

Planned outcome: users can export a cleaner evidence package for review and handover.

Planned items:

- Formatted PDF report.
- Session metadata, app version, COM settings, relay address, duration, counters, warnings, and raw evidence appendix.
- Pass/fail style assessment summary without replacing formal project acceptance procedures.
- Export package containing Markdown, JSON, PDF, and sanitized raw trace.

## 5. Analyzer maturity

Planned outcome: ARIEC60870 becomes more useful for repeated analysis and long-duration troubleshooting.

Planned items:

- Capture replay mode.
- Compare-two-sessions workflow.
- Stronger long-duration evidence retention.
- Better event/value filtering.
- Cleaner desktop architecture for future UI-heavy features.
