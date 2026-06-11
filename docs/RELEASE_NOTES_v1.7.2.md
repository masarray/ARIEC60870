# ARIEC60870 v1.7.2 — Polling Fairness + Signal List Editor + Product Icon Pass

This release locks the next product direction after the operator UX pass: the desktop app must be readable for field engineers, honest about low-baud IEC-101 timing, and editable without touching JSON by hand.

## Added

- **Signal List Editor** for IEC-101/104 IOA databases.
  - Add Row
  - Del Row
  - Duplicate
  - Load List
  - Save List
  - Save As
  - Validate
  - Save & Apply
- Editable IOA point fields:
  - CA
  - IOA
  - Type ID
  - signal name
  - group
  - type/role
  - unit
  - scale
  - expected Class
  - expected COT
  - command policy
  - feedback IOA binding
  - state map
  - mnemonic
  - bay
  - description
- Product-wide **IEC 60870 icon** for the WPF application shell and landing-page favicon assets.
- Class 2 scan feasibility note when starting serial IEC-101/103 sessions.
- Post-run IEC-101 finding when the configured Class 2 interval is below the estimated physical serial throughput.

## Changed

- IEC-101 Class 1 drain now has a fairness guard: normal ACD/event drain yields back to Class 2 scan when background polling is due after a bounded number of Class 1 frames.
- GI follow-up drain remains stricter: it still drains until ACTTERM, NO DATA, cancellation, or limit, because GI completeness is more important than Class 2 fairness during startup image acquisition.
- Scrollbar rail and thumb styling was made wider and more visible to avoid tiny WPF thumbs during high-volume evidence sessions.

## Why this matters

At 1200 bps, a configured 100 ms scan interval is not a guaranteed measurement refresh. The physical serial link, FT1.2 request/response overhead, turnaround time, and Class 1 pressure can make the effective scan much slower. This release makes that limitation visible and reduces measurement starvation caused by continuous Class 1 event drain.

## Validation

Sandbox validation performed:

- XAML XML parse
- project XML parse
- manifest JSON parse
- C# brace balance
- ZIP integrity

Full `dotnet build` still needs to be run on a Windows/.NET SDK machine.
