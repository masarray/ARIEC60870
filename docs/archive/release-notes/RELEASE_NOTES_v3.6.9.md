# ArSee v3.6.9 — Windows x64 stable release

## What's new

ArSee now includes an **operator-approved automatic updater** for managed, per-user Windows installations. It checks the official stable GitHub Release, downloads the matching installer, verifies the file size and SHA-256 digest, then applies the update using an independent worker only after the application has closed normally.

The update worker backs up the previous installation and its Windows uninstall registration, refuses unsafe junction/symlink paths, checks free disk space, verifies the newly installed executable and Inno registration version, and waits for the new WPF window to acknowledge its first rendered frame. User profiles, exported reports, signal lists, and capture evidence remain outside the installed program directory.

**First-time adoption:** The already-public v3.6.8 installer was built before this updater shipped. Install **v3.6.9 once using the official Windows installer**; built-in updates then apply to future stable versions. Portable ZIP editions continue to use the official GitHub Releases download flow instead of replacing themselves.

## Operator safety

- Updates are never installed automatically while an IEC-101/103/104 session is active. Download can occur while capture continues, but replacement waits for normal safe closure.
- The newly opened application does not auto-connect, poll, issue commands or resume a physical serial/TCP IEC device session. The engineer remains in control.
- The updater refuses malformed version tags, non-official download origins, mismatched checksums, unsafe install targets, or insufficient staging/rollback disk space.
- The Windows installer is currently **unsigned**. Verify its origin and the published SHA256SUMS.txt. Hash verification is not equivalent to Authenticode publisher signing.
- This release does not claim validation against energized field equipment; protocol and WPF acceptance are bench/simulated.

## Release evidence

- Core, Master, Monitor, Reporting, Desktop and Repository .NET regression suites, plus IEC protocol smoke and Windows WPF packaging checks.
- Native Windows updater proof of v3.6.7→v3.6.8 staged worker, independent Inno setup and real WPF restart: [FAT CI run 43378979735](https://github.com/masarray/ARIEC60870/actions/runs/43378979735).
- Public v3.6.9 is gated on a **separate successful exact-source Windows FAT** against the published v3.6.8 Setup, to be checked by the release workflow before public assets are published.

Download official installer / portable package from [GitHub Releases](https://github.com/masarray/ARIEC60870/releases). Sources remain Apache-2.0 open source.
