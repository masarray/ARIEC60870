# ARIEC60870 v1.8.4 — Lightweight Protocol Trace Line Monitor

## Changed

- Replaced Protocol Trace DataGrid with a lightweight virtualized ListBox line monitor.
- Protocol Trace now renders as readable multiline mono text:
  - line 1: direction, class/service, address, type, signal/address
  - line 2: commissioning-readable meaning
  - line 3: raw hex evidence
- TX/RX/Error tone is applied at row container level only.
- Removed DataGrid column visibility dependency for Protocol Trace.
- Protocol Trace is no longer treated as a tabular export DataGrid. Full evidence remains visible in the line monitor/interpreter.

## Why

Raw protocol trace is a forensic line monitor, not a business table. A single line item is lighter than many DataGridCell containers and keeps the UI responsive under large trace volume.

## Font Note

Protocol Trace uses:
`Sometype Mono, Cascadia Mono, Consolas`

The font binary is not included in this package. If Sometype Mono is installed on the target PC, WPF will use it automatically.
