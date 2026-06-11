# Polling Fairness and Signal Editor Audit — v1.7.2

## Polling audit

IEC-101 serial polling cannot be judged by the configured interval alone. At low baudrate, especially 1200 bps, the effective scan cycle depends on:

- baudrate and serial framing bits;
- FT1.2 fixed/variable frame overhead;
- link address size;
- COT/CA/IOA size;
- slave turnaround time;
- Class 1 event pressure;
- GI follow-up drain;
- background Class 2 payload size.

The application now logs a practical Class 2 scan estimate at session start and raises a finding if the configured interval is unrealistically aggressive.

## Scheduler policy

Priority remains:

1. operator command;
2. GI follow-up drain;
3. Class 1 event drain;
4. Class 2 background scan.

The new rule is that normal event drain must not starve Class 2. After a bounded number of Class 1 frames, the scheduler yields to Class 2 if background scan is due. GI follow-up remains stricter because station image completeness depends on ACTTERM/NO DATA/limit.

## Signal list editor

The previous Signal List workspace was read-only. v1.7.2 adds a field editor so the bundled PLN/PUSERTIF seed can be copied, corrected, and reused for global projects. The editor preserves state maps so digital labels such as OFF/ON, OPEN/CLOSED, LOCAL/REMOTE, and AUTO/MANUAL remain readable after saving.

## Remaining roadmap

- IEC-104 state-machine validator for t1/t2/t3/k/w enforcement.
- Command feedback matrix viewer.
- IEC-103 FUN/INF mapping editor.
- Full slave simulator project inside the same solution.
