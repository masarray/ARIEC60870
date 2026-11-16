# IEC-101 Stage 7A — source-qualified SBO admission (2026-11-16)

## Root cause and scope
Prior command requests encoded only the wire S/E bit. A button marked **Execute** in SBO mode became indistinguishable from an explicitly selected **Direct Operate** request. Existing link readiness and command casebook are necessary but not sufficient as a pre-TX SBO safety decision. This milestone changes only IEC-101 active single/A-B control dispatch; IEC-104 and passive monitoring retain their existing behavior.

## Architecture
- The Command Dock now carries `OperationMode = Direct | Select | ExecuteSelected`, preserving existing `SelectBeforeOperate` wire encoding and old API default Direct.
- One `Iec101ControlAsduFactory` builds command bytes for both single and A/B, avoiding diverging duplicated switch statements.
- Each physical COM holds one bounded `Iec101SboSelectionAuthority`: record SELECT only **after successful physical TX**, accept only service-validated, positive, matching COT7 ACTCON using canonical decoder identity (TI, CA, OA, IOA and exact command bytes with only S/E bit masked).
- SBO EXECUTE is blocked **before** sending FT1.2 if its selection is missing, negative, ambiguous, mismatched, expired (>30 s), or owned by another COM/epoch. An unrelated FT1.2 E5/FC0 link ACK is never application SELECT acceptance.
- Direct Operate continues independently. SELECT authority is consumed on real control EXECUTE TX, revoked on timeout/DFC/NACK, connection close/reopen, and A/B promotion/demotion. No implicit retry, SELECT replay, new poll pressure, UI redesign or SCADA-target mutation.
- Existing historical casebook continues to describe wire evidence; active pre-TX admission uses one per-COM tracker, not presentation strings.

## Test plan and limitation
Unit golden vectors: SELECT TX vs RX only; ACTCON required; wrong CA/OA/IOA/value/qualifier; malformed/test/negative; duplicate overlapping SELECT; expiry/epoch reset; A/B isolation. Scheduler integration: single and dual simulated RTU block unconfirmed EXECUTE with **zero TX Type46**. Existing DO, GI, Class1/2, COM recovery and WPF regressions must remain green.

**Not claimed:** full observed EXECUTE ACTCON/ACTTERM/physical feedback completion verdict, vendor timing tolerance/positive field FAT, safe operator command defaults in IEC-104, 1200-bps SICAM field certification, 500/2000 SOE parity, or stable public release. Do not merge this feature before exact-head Windows tests pass.
