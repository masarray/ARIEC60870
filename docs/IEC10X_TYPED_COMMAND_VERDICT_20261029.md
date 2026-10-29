# Typed command verification and physical source guard — 2026-10-29

Command application phases are determined by typed COT/P-N and original command echo, never English-string detection. Correlation includes protocol, physical circuit/source, CA, IOA and Type ID. SELECT ACTCON confirms only select acceptance and must not count as completed operation; EXECUTE ACTCON and ACTTERM remain distinct. An RTU feedback object from another circuit/CA is not proof of this command. An invalid quality sample is excluded. A matching feedback observation alone is not proof of commanded physical position unless expected state is independently checked. Link ACK is not an application confirmation.

This checkpoint changes the existing command ledger only; no transport, FCB/FCV, TX scheduling, passive capture, or timer changes. Tests use synthetic records with explicit source and COT. Real RTU operation / interlock behavior requires commissioning FAT.
