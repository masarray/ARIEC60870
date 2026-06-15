# IEC-101 GI Audit — v1.8.3

## Problem observed

Not all profile signals appeared in Value Viewer after GI.

## Findings

The UI previously showed only points that had already produced a decoded value event. If a profile point had not yet arrived, it was invisible, making it hard to distinguish between:

- point not configured in the profile,
- point not returned by GI,
- point returned later by Class 2 scan,
- point filtered out as command-only,
- CA/IOA/type mismatch.

## Fix

Value Viewer now has expected profile placeholders for monitor points. Runtime values replace placeholders by IOA key.

## GI completeness rule

When ACTTERM / activation termination is observed, the app compares expected profile monitor IOAs with received IOAs. Missing points trigger an IEC-101 diagnostic warning with a sample of missing IOAs.

## Operational recommendation

If points remain missing:
1. Increase Max Class 1 drain.
2. Confirm ACTCON/ACTTERM sequence.
3. Confirm CA/IOA/type profile.
4. Verify whether the RTU returns the point during GI or only during cyclic/background Class 2.
5. Check ACD/DFC and timeout behaviour.
