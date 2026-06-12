# ARIEC60870 v3.3.0 — ARIEC Capture File Model + Save Selected Line Monitor Block

## Added

### Save selected Protocol Trace rows as capture

Protocol Trace / Line Monitor now supports extended selection.

Users can select one row or a block of rows with Ctrl/Shift and click:

- `Save Capture`

The selected rows are saved into a portable `.ariec` capture file.

### ARIEC capture container

The `.ariec` file is a ZIP-based container with:

- `manifest.json`
- `frames.jsonl`
- `retention.json`
- `report.md`
- `hash.txt`

### Capture integrity

The selected frame ledger is written as JSON Lines and hashed:

- `frames.jsonl`
- SHA256 stored in `hash.txt`
- SHA256 also stored in `manifest.json`

### Evidence context

Selected captures include:

- protocol mode,
- trace mode,
- selected sequence range,
- session counters,
- retention policy,
- proof state,
- backpressure/suppression counters,
- raw hex,
- decoded meaning,
- CA/IOA/COT/TypeID,
- ACD/DFC,
- IED/RTU time.

### Diagnostic markers

New diagnostics:

- `ARIEC-CAPTURE-SELECTION-SAVED`
- `ARIEC-CAPTURE-SELECTION-FAILED`

## Why

This starts ASE2000-like capture behaviour: select a block from Line Monitor, save it as portable evidence, then build re-open/print/report features on top of the capture file model in the next phase.
