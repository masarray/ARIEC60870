# ARIEC60870 Evidence Analyzer v3.6.7

This release focuses on safer field testing, clearer Smart Findings, and a smoother portable desktop experience for IEC-101/103/104 commissioning work.

## What is new

### Smarter IEC-101 dual-link redundancy behavior
- Faster failover now respects the response timeout configured by the user. If the timeout is set to 1000 ms, the redundancy evidence and failover decision use 1000 ms instead of silently widening the delay.
- Cascaded failover is improved. If Link A fails and the session moves to Link B, then Link B later times out, the controller now prioritizes standby recovery and can promote Link A again instead of staying stuck on the timed-out active link.
- Manual disconnect/stop actions are treated as operator actions, not field failures. Smart Findings no longer reports a healthy manual stop as a device or cable fault.
- Redundancy timeline wording is clearer for active link timeout, standby recovery, failover start, failover complete, and post-switch image refresh.

### Smart Findings that understand context better
- General Interrogation traffic is no longer mistaken for a control command.
- Smart Findings now separates real control-command issues from normal GI, Class 2 background scans, redundancy switchovers, and operator disconnects.
- Connection symptoms are classified with confidence, for example manual stop, serial port missing/busy, no slave response, TCP connection refused, remote close, network path down, and supervision timeout.
- Corrected configuration findings keep an audit trail in the current session instead of disappearing instantly, while a fresh session with the corrected configuration starts cleanly.

### Modern desktop experience
- Native WPF message boxes have been replaced with compact modern dialogs that match the ARIEC60870 desktop style.
- The Values workspace grid uses the available horizontal space better and avoids unnecessary wrapping in common columns.
- The main window height and left-rail spacing are adjusted so the Help button is not clipped.

### Lazy release update notification
- The app can quietly check for a newer GitHub release after startup.
- The check is delayed, low priority, timeout-limited, and silent when there is no internet connection.
- When a newer version exists, the app shows a small update button instead of interrupting testing with a popup.

## Recommended for
- IEC-101 dual-link redundancy FAT/SAT.
- Field tests that need faster failover proof with short timeout settings.
- Users who rely on Smart Findings and PDF reports as evidence handover material.
- Portable Windows users who want a cleaner single-EXE desktop package.

## Portable Windows package
Download the Windows x64 portable ZIP from this release, extract it, and run:

```text
ARIEC60870.exe
```

No installer is required. Keep the included `docs`, `samples`, `profiles`, `LICENSE`, `NOTICE`, and checksum files with the executable when sharing the package internally.

## Field note
For redundancy testing, use the Redundancy workspace timeline together with the exported report. A valid failover proof should show the active timeout/failure evidence, the promoted standby link, and the application image refresh after the switch.
