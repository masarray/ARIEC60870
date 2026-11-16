# IEC-101 Stage 5 — fail-closed late FC11 response evidence (2026-11-16)

Baseline: `master@c7f4ce9715a396d753c928659110d419d520e820` (Stage 4A physical single-COM reconnect and Stage 4B independent dual-COM reopen already merged).

## The actual L11/T04 risk

Secondary FT1.2 FC11 is an addressed link-status telegram, **not** an exchange-id or transaction-number-bearing response. After a previous primary FC8/FC9 times out, a subsequently arriving addressed FC11 can belong to the expired request or a newly sent request on the same link. Checksum, PRM, link address and FC alone cannot distinguish them. The previous service correlation validator rejects FC11 after unrelated FC3/Class1/Class2 requests; this milestone covers the *same-service* residual.

## Bounded quarantine in the existing single shared engine

- Per physical COM epoch, the first unconfirmed transmitted FC8/FC9 status request arms a pending delayed-status ambiguity guard. No new reader, duplicated decoder or alternative exchange owner is introduced.
- When a later valid addressed FC11 would otherwise validate a status exchange, quarantine **one** plausible delayed status as `IEC101-RX-AMBIGUOUS-LATE-FC11` and retain RX source/raw evidence. Never advance FCB, mark link good, or promote standby from this quarantined observation. The existing response loop may continue to receive other candidates until its ordinary timeout.
- Wrong-address, echoed-primary, malformed, FC1 NACK and out-of-context messages do not consume this guard. The first plausible FC11 consumes it; if that exchange then times out the guard is *not* automatically rearmed, allowing a new status request to establish evidence.
- A new physical connection epoch clears the guard and all old exchange ownership. Each A/B channel has an independent engine and guard. An aborted unanswered status exchange also arms the guard only when TX actually began.
- Subsequent successful addressed FC11 proves an addressed *link-status observation*, not command success, station identity beyond the configured link, or ASDU application completion.

## Strict limitation

IEC-101 unbalanced FC11 has no transaction ID; an adversarial or extremely delayed **second or later** old FC11 can remain intrinsically indistinguishable from a fresh FC11. One-frame quarantine is a bounded fail-closed improvement, **not** a mathematically complete late-reply disambiguation guarantee. The user should not infer that all L11/T04 field FAT is passed. A stricter physical read silence barrier or controlled session reset may be required for a vendor-specific field profile; neither is silently applied here because autonomous FC0, RTS/DTR toggles and hidden GI/command replay would be unsafe.

## Evidence matrix

- Synthetic golden vectors: single/A/B FC9 timeout -> late valid FC11 rejected -> next status accepted; foreign link address cannot consume the guard; new physical epoch discards old ambiguity state.
- Required gates: Windows Release, changed-source formatting, 19/19 protocol smoke, all xUnit projects, WPF 1,400-frame acceptance, CodeQL, dependency and hygiene on exact PR head, and post-merge master CI.
- **Not a physical test:** real SICAM A8000 at 1200bps, paired COM unplug/replug, multi-vendor NUC and 60/500/2000 FIFO/SOE reconciliation remain FIELD FAT pending.

No public release, website or GUI changes.
