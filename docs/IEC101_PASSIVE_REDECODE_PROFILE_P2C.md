# IEC-101 P2C — Verified Offline Re-Decode Without Re-Capture

Implementation contract: 2026-10-28 · Parent [#30](https://github.com/masarray/ARIEC60870/issues/30) · Clean-room IEC-101 passive workflow.

## Operator problem and outcome

On a production SICAM / SCADA line the IEC-101 Link Address can occupy **two octets** while the default ARIEC60870 passive profile uses one octet. A capture can contain all exact original bytes but yield an incomplete/misleading Frame Trace when the interoperability profile is wrong.

P2C must let the operator **re-decode the very same full raw journal** with another Link Address, COT, Common Address, IOA width, idle-gap, or link-mode profile. **Never** overwrite the saved manifest, capture, frame files or hashes when merely changing the interpretation.

## Existing GUI — no new workspace or command rail

1. Choose IEC-101 Passive Monitor via existing Setup. For the first review, set decoder widths based on the actual RTU interoperability list, when known.
2. Use existing **Open Cap** and select the full session `manifest.json`.
3. When saved and current Setup decoder profiles differ, the existing themed message dialog shows both complete field profiles and asks explicitly:
   - **Yes — saved capture profile (safe default)**.
   - **No — current Setup profile (read-only re-decode)**.
   - **Cancel — do not load the session**.
4. The existing Frame Trace / Events / Values / Findings views are repopulated from a fully verified two-direction binary journal. The Capture status and session log state whether the **saved** or **override** decoder was used, and show the exact profile string.
5. Compare by changing Setup and reopening the same `manifest.json`. The source remains unchanged and independently verifiable after every pass. No serial ports are opened for replay.

The operator must understand the difference between **raw evidence** and **decoded inference**. No metadata is silently changed in the original capture; a profile override is for investigation, not proof of the device's configured sizes.

## Engine boundaries

`PassiveReplayProfile` is a small immutable Monitor-layer record describing Link Address width (0..2), COT (1..2), Common Address (1..2), IOA (1..3), partial-frame gap (100..10000ms), and link mode (Auto/Balanced/Unbalanced). It validates independently of COM hardware.

`PassiveJournalReplay.ReadVerifiedFrames(folder, profile, cancellationToken)` uses the **same SHA-256, CRC32, per-record continuity and manifest finalization check** as standard replay. The optional override selects only the *incremental scanner* link-address width and gap; it cannot affect physical bytes. Standard `ReadVerifiedFrames(folder, cancellationToken)` retains saved-profile behavior.

The Desktop uses the chosen profile consistently for scanner, IEC-101 ASDU decoder, and transaction correlator. It does **not** call the Active Master, write UART, or change production transport.

Disk remains authoritative; the live WPF rows are only a bounded preview. Reopening clears existing selected view before streaming; final UI metadata identifies the used profile.

## Performance and integrity

- One sequential cursor per physical RX direction, one bounded dispatcher batch (96 frames), no `ReadAllBytes` of journal data, no WPF work per byte.
- Both original source streams are verified **before displaying replay frames**; corrupt/torn capture remains rejected.
- Re-decode is deterministic for the same complete journals + exact profile; derived `frames-XX.jsonl` and `transactions.jsonl` are not needed and may be reconstructed.
- Runtime cancellation and close/restore controls use the same existing P2A path.
- Timeline across two USB COMs is approximate read-completion order, not on-wire per-bit timing.
- An incorrect 1-octet profile can fail to detect an otherwise valid 2-octet fixed frame; this is an *interpretation mismatch*, not necessarily physical corruption.

## Test matrix

1. Produce validated full duplex journal from raw 2-octet fixed requests and responses, intentionally fragmented in 1–2 byte COM chunks.
2. Replay saved profile and assert correct byte sequences, 2 physical directions, checksum and offsets.
3. Re-decode with Link Address 1; do not silently present original fixed telegrams as correctly decoded. Re-decode again with the saved 2-octet profile and regain identical interpretations.
4. Verify manifest and both binary journals are byte-identical before/after several replays; hashes and verifier remain green.
5. Test all invalid IEC-101 field widths and invalid gap/mode rejected before interpreting data.
6. Windows WPF smoke verifies current Setup → pure offline profile mapping (Link 2, COT1, CA1, IOA2, gap1500, Balanced) with **no COM open**.
7. Release warnings-as-errors, all protocol/xUnit regression, WPF integration smoke, CodeQL/dependency/hygiene and Win-x64 portable package must pass before merging.

## Explicitly outstanding

The full ASE2000 V2 comparison requires more than this feature: intelligent width recognition with confidence and errors, GI Type100/COT6–10 lifecycle, unknown link-function policy, searchable transaction journal over multi-day captures, simultaneous 4 RX monitoring of both redundant A/B links, and field non-interference/2-hour endurance tests. Those are subsequent milestones, **not claims made by P2C re-decode**.
