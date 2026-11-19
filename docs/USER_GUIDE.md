# ArSee User Guide — single-source handbook index

ArSee is an open-source IEC 60870-5-101/103/104 protocol tester and analyzer for Windows engineers.

**Canonical instructional text:** [the shared English/Indonesian operating manual and IEC handbook](../site/learning/iec60870-handbook.json). The native Windows Help Center and the landing-page [English Wiki](../site/wiki.html) / [Indonesian Wiki](../site/id/wiki.html) render that same JSON. **Do not maintain a second independent copy of the how-to instructions in this Markdown file.**

## Read this first

The full operating guide includes the following chapters in English and Indonesian. Open the relevant chapter in the Windows Help Center or the Wiki; each one includes prerequisites, numbered actions, expected results, troubleshooting, and field-safety notes when applicable.

| Workflow | English Wiki deep link | What it teaches |
| --- | --- | --- |
| Getting started with ArSee | [Open](../site/wiki.html#learn-use-start) | Portable installation, native controls, built-in demo, first session |
| Setup, target and profile | [Open](../site/wiki.html#learn-use-setup) | Protocol mode, UART/COM, TCP/IP, address widths, project JSON, start/stop |
| IEC 101 active master | [Open](../site/wiki.html#learn-use-101) | Unbalanced serial link, Class 1/2, GI, slow-baud investigations |
| IEC 103 relay testing | [Open](../site/wiki.html#learn-use-103) | Serial relay access, FUN/INF signal mapping, event proofs |
| IEC 104 TCP/IP sessions | [Open](../site/wiki.html#learn-use-104) | Host/port, STARTDT, APCI sequence, GI, spontaneous reports |
| IEC 101 Dual Link | [Open](../site/wiki.html#learn-use-dual) | A/B setup, standby supervision, safe switchover and evidence |
| IEC 101 Passive Monitor | [Open](../site/wiki.html#learn-use-passive) | Physical RX-only taps, optional journaling, virtual inline |
| Analysis workspaces | [Open](../site/wiki.html#learn-use-views) | Raw Frames, Values, Events, archive search, Controls, GI, findings |
| Command Panel | [Open](../site/wiki.html#learn-use-commands) | GI, Read IOA, Timesync, SBO/DO, qualifiers and command lifecycle |
| Captures and reports | [Open](../site/wiki.html#learn-use-evidence) | Save Cap, Open Cap, Trace TXT, Events CSV, Report PDF, confidentiality |
| Troubleshooting and F1 | [Open](../site/wiki.html#learn-use-diagnose) | Failure layers, missing events, mapping, incomplete GI, Help navigation |

For Bahasa Indonesia, use the same chapter hashes under [site/id/wiki.html](../site/id/wiki.html). All chapters work offline when bundled with the portable ArSee application.

## Quick safe starting sequence

1. Obtain the portable Windows x64 ZIP from [ArSee Releases](https://github.com/masarray/arsee/releases/latest) and extract it to a local folder.
2. Run `ARIEC60870.exe`, open **Setup**, and choose the correct protocol profile and explicit **TARGET MODE** (real device or built-in demo).
3. Verify physical interface, connection parameters, and approved interoperability address widths.
4. Use **Connect**, or **Start Monitor** for physical RX-only passive mode; inspect **Raw Frames** before interpreting **Values**, **Events**, **GI** or **Controls**.
5. Select relevant rows and use **Save Cap** for a portable evidence subset, or open **Report** and export PDF after review.
6. Use **Disconnect / Stop Monitor** to end the session. Never send field controls without an approved safe test procedure.

For deeper operator detail, use the shared manual above. For licensed IEC normative compliance, follow the purchased standard and approved site interoperability profile. The ArSee manual is original educational documentation, not a conformance certificate.

## Maintainer rule

- Edit the operating instructions **only** in `site/learning/iec60870-handbook.json`.
- Run the `site/` → `docs/` mirror script after changes.
- Verify source, bilingual chapter parity, WPF local packaging, Wiki navigation, Release build, and screenshot acceptance before merge.
- This Markdown file is intentionally an index, not a divergent user-guide manuscript.
