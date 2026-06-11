# Release Notes v0.9 — Product Architecture and Agent Governance

v0.9 is a documentation/governance release. It does not change protocol runtime behavior from v0.8.

## Added

- `AGENTS.md` with hard guardrails for future coding agents and maintainers.
- `docs/PRODUCT_BENCHMARK_AND_STRATEGY.md` summarizing external product/protocol benchmark direction.
- `docs/PROJECT_STRUCTURE_FINAL.md` defining mature repo boundaries.
- `docs/MASTER_POLLING_POLICY.md` formalizing controlled Class 1/Class 2 behavior.
- `docs/EVENT_LOG_POLICY.md` formalizing relay timestamp and edge-event logging rules.
- `docs/CLEAN_ROOM_POLICY.md` to protect Apache-2.0 clean-room implementation.
- `docs/ROADMAP.md` with v1.0–v1.4 product direction.

## Updated

- README now identifies the package as v0.9 and links governance docs.
- Landing page copy now positions ARIEC60870 as a focused IEC-103 Active Master Tester + Analyzer.

## Product Direction Locked

```text
ARIEC60870 = Active IEC-103 Master Tester + Analyzer
Primary mode = connect to one IEC-103 slave relay
Supporting mode = offline trace decoder
Signal names = user mapping profile only
Event time = relay timestamp
Polling = Class 2 normal, Class 1 only on ACD=1 / bounded GI drain
License = Apache-2.0 clean-room
```
