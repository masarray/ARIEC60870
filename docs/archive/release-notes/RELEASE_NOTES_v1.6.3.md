# v1.6.3 — Persistent Setup + Standards Audit Pass

This release focuses on practical field usability and protocol honesty.

## Added

- The setup window now remembers the last user configuration automatically.
- Saved setup is restored on the next application launch from the user's local AppData folder.
- Saved fields include protocol mode, target mode, COM port, baudrate, serial mode, TCP host/port, link/common addresses, COT/CA/IOA sizes, IEC-104 t0/t1/t2/t3/k/w, polling interval, timeout, GI/clock-sync options, mapping profile path, and session duration.
- IEC-104 setup now exposes `t0` connection establishment timeout as part of the forensic interoperability profile.
- IEC-101 link-address-size awareness now includes the standard 0-octet case internally, while the UI prevents false validation for unbalanced master polling.

## Fixed / Hardened

- IEC-101 Balanced mode is now explicitly marked as planned / not active. The app no longer implies that balanced link-layer behaviour is implemented.
- IEC-101 link-address size `0` is treated as a known standard profile case, but blocked for the current unbalanced-master engine with a clear validation message.
- Setup changes are saved when the user starts a session, closes the setup overlay, clears/loads mapping, or exits the application.

## Standards audit findings still open

- IEC-101 balanced link-layer engine is not implemented yet.
- Full IEC-104 t1/t2/k/w state-machine enforcement remains a next pass; current implementation exposes profile values and basic findings.
- Command behaviour validation studio is still required for direct operate, select-before-operate, ACTCON/ACTTERM, negative confirmation, wrong CA, and unknown IOA tests.
- GI completeness requires an IOA point profile import to prove expected vs received objects.
- Immutable forensic package export still needs session manifest/hash/raw-binary packaging.
