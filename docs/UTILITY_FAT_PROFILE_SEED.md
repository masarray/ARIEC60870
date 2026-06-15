# Utility FAT IEC-101/104 Example Profile

```text
profiles/utility_fat_iec10x_default_profile.json
docs/profiles/utility_fat_iec10x_default_profile.json
```

This file is a neutral, editable IEC-101/104 IOA point profile for demonstrating gateway and RTU communication test workflows. It is not an approved utility standard, vendor database, or contractual FAT document.

Use it as a starting point only. Before live testing, replace names, CA/IOA values, command points, feedback points, timing expectations, and pass/fail criteria with the approved project signal list and test procedure.

## What the profile demonstrates

- IOA point naming for IEC-101/104 evidence screens.
- Digital and analog point grouping.
- Command-to-feedback relationship examples.
- Test scenario metadata for GI, command, SOE, timestamp, and monitoring checks.
- A safe pattern for keeping mapping data user-owned instead of hard-coded in the application.

## Public repository rule

Bundled profiles must stay sanitized and generic. Do not commit customer names, station names, IP addresses, real project IOA lists, or confidential FAT forms.
