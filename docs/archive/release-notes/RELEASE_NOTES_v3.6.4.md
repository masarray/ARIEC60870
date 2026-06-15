# ARIEC60870 v3.6.4 — utility Evidence Workflow Cleanup + Smart Capture Rules + HTML Report Foundation

## Removed

### Messages tab

The Messages tab introduced in v3.6.2/v3.6.3 has been removed. It added UI surface without directly improving the utility/FAT/SAT evidence workflow.

### AutoTest tab

The AutoTest Assessment tab has been removed from the main workspace until the checklist is driven by explicit utility FAT test items.

## Changed

### Smart IEC trigger capture is now rule-based

Automatic capture no longer starts from hardcoded protocol triggers.

Capture is OFF by default. The engineer must enable a rule first.

User-defined rule fields:

- preset
- direction
- CA
- IOA
- Type ID
- COT
- decoded text contains
- raw hex contains
- pre rows
- post rows
- max captures

When the enabled rule matches traffic, ARIEC records pre/post `.ariec` evidence.

### Capture Rules workspace

The old trigger dashboard is now `Smart Capture Rules`.

It contains:

- simple rule configuration,
- capture history,
- copy path,
- open folder,
- readable detail panel.

## Added

### Standalone HTML Evidence Report

The left-rail Export action now generates a standalone HTML evidence report instead of a markdown dump.

The report is designed to be opened without ARIEC and printed to PDF from a browser.

Report sections:

- cover / verdict
- report metadata
- session counters
- communication setup
- summary verdict
- GI evidence
- command evidence
- SOE / event evidence
- important protocol evidence
- notes for PDF use

## Preserved

- Protocol Trace default workspace.
- Evidence Summary workflow.
- Value Viewer.
- Event Log / SOE evidence.
- Findings and Diagnostics.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.
- Clickable panel header collapse UX.
