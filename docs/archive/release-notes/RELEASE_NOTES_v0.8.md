# ARIEC60870 v0.8 - User Mapping Profile + Relay Event Log

## Product Direction

ARIEC60870 remains an active IEC-103 Master Tester and analyzer. The product is relay-agnostic and does not include built-in vendor signal names.

## Added

- User mapping profile JSON schema/config.
- Sample user-editable mapping profile under `samples/mapping-profiles/`.
- CLI `--mapping <profile.json>` support.
- WPF mapping profile import/clear.
- Value Viewer table showing current relay value/status snapshot.
- Relay Event Log table.
- Master report sections for Value Viewer and Relay Event Log.

## Correct Event Log Behavior

The Event Log now uses relay timestamp from IEC-103 ASDU time fields, not PC arrival time.

The Event Log records:

- state changes; or
- relay spontaneous/edge events.

It does not blindly record every incoming GI/status frame. GI snapshots update Value Viewer first.

## Correct Mapping Behavior

Signal naming is not hardcoded.

- No vendor/family-specific built-in profile.
- No fake generic profile that pretends to know relay signal names.
- User/project profile supplies signal names.
- Raw FUN/INF remains visible when a signal is mapped.
- Without a mapping profile, the app displays raw FUN/INF evidence.

## Demo Change

The internal generic relay simulation now emits a later spontaneous state change on the same FUN/INF used in the GI snapshot. This demonstrates the intended Value Viewer vs Event Log separation:

- GI updates Value Viewer.
- Later state change creates Event Log row using relay timestamp.
