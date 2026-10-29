# Forensic Quality and COT Grid Contract

IEC-101/104 Quality and COT display decoded typed evidence, not inferred flags from English messages. Every relevant grid uses compact actionable Quality badges (Good/Unknown/OVF/INVALID/CARRY/NT/BLK/SUB/ADJ), maximum three chips; full raw bits remain in typed details/tooltips and selected Inspector. Type31 DIQ cannot imply overflow and BCR carry is distinct. COT stays in one column with numeric phase and NEG/TEST chips. No per-flag columns. Unknown never becomes Good. IEC-103 legacy quality text remains visible without inventing 10x quality bits. Virtualization and batched UI rendering remain unchanged.

Tests: `tests/ARIEC60870.Desktop.Tests/ProtocolFlagPresentationTests.cs`. Hardware, durable capture, reporting and command lifecycle transaction parity are separate work; CI cannot certify field behavior.
