# ARIEC60870 v1.8.8 — Linked Raw/Interpreter Highlight

## Changed

- Protocol Trace selection now auto-activates the most relevant interpreter group:
  - IEC-101/104 object frames default to object address.
  - IEC-104 non-I frames default to APCI.
  - IEC-103 mapped FUN/INF defaults to ASDU.
- Added active group caption in the Frame Interpreter header.
- Raw hex chips and decoded interpreter lines stay linked through the same active group key.
- Clicking a raw chip pins the decoded block, and clicking a decoded block pins the raw group.
- Active raw and decoded blocks are visually stronger and easier to scan.

## Why

The Protocol Trace line monitor should behave like a protocol analyzer: select a line, immediately see the most important raw group and its decoded meaning. The user should not have to search manually through the interpreter for the relevant frame part.
