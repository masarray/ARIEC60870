# R3-E — Verified historical command forensics (2026-10-30)

## Cross-thread and ownership checkpoint

Based on master `b8e6bb57e631dd2d8589e183fa50a9d45c54defb` with no open PR at branch creation. Implementation is isolated in `feat/r3e-trace-command-evidence-20261030`; prior R3-A/B/C/D commits remain authoritative. This milestone owns CMD-01, TRACE-01 and the portable-capture **search slice** of CAPTURE-01, not active transport or IEC 61850/RTU configuration. Do not force-push over sibling threads.

## Implemented engineering changes

- Command casebook distinguishes secondary-originated COT=6 from standard COT7 ACTCON; leaves a legitimate pending SELECT unresolved and records a separate protocol anomaly. The passive Monitor alerts on the unusual physical direction/COT, without interpreting it as rejection or successful operation.
- Trace narrator refuses to label TEST=1 command frames as operationally accepted. Master-originated COT7 and secondary-originated COT6 get explicit abnormal-direction meanings.
- Bounded 24-stage case history now discloses truncation in forensic reports; the old stages are not claimed to exist.
- Read-only **portable `.ariec`** archived search: filter `ioa`, `ca`, `ti`, `cot`, `source`, `direction`, `time`, `hex`, `neg`, `test`. Searches all `frames.jsonl` records, not only latest 600; original SHA-256 / declared row count verified before UI exposure; bounded 150-frame pages, background I/O, existing Trace inspector and Resume. P/N/TEST and secondary SQ IOA matches use original bytes + declared protocol widths; insufficient legacy profile never becomes a trusted match. Repeated pages rescan verified ZIP instead of relying on unverified derived indexes: CPU/I/O are proportional to archive size but peak memory is bounded. Never claim random-access indexing or complete raw-journal support from this feature.

## Not complete (must stay open in issue #63)

- Direct indexed seeking into the **full passive binary `manifest.json` journal** is not yet available in Frame Trace. Existing offline replay is complete but live preview is bounded and event-pinned evidence expires with event retention. Full raw binary journal must be opt-in and independently verified; follow up with verified sparse disk index and exact event/case navigation.
- No comparison UI for arbitrary two command cases yet; current Casebook/Events and archive query enable manual comparison.
- Recorded portable capture filter is complete-read SHA-verified and bounded, **not sparse-indexed**. Very large files require performance measurements.
- No hardware FAT, electrical capture order proof, or claim that this diagnoses internal SICAM A8000 ETI5/UMPSI0 behavior. This app observes IEC-101 communications only.

## Synthetic regression gates

Run Windows Release/xUnit/GUI smoke and CodeQL on the PR head. New tests cover secondary COT6 despite valid checksum, TEST=1, stage-history eviction, full-search beyond 600 rows, filters/strict unknown fields and deterministic pagination. Existing active-master transport untouched. CI is not an IEC-101 device interoperability certificate.

## Usage

Open a portable `.ariec` capture, then in Frame Trace enter e.g. `ioa:123 cot:7 neg:1` and choose **Find .ariec**. The entire archived record set is checked; use arrows for pages and **RESUME** to return to recent live Trace. A full passive raw `manifest.json` must still be inspected through verified Replay/Anomaly navigation while indexed journal search is being implemented separately.
