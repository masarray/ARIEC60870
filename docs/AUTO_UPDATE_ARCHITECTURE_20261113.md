# ArSee controlled self-update — design and qualification (2026-11-13)

## Scope / release contract

Applies to the **installed per-user Inno Setup** distribution of Windows x64 ArSee. The portable ZIP is deliberately not overwritten in place (it may live on a USB drive, read-only folder or shared path); its notification links to the official installer. This prevents ambiguous duplicate installations or mutation of arbitrary user directories.

The existing v3.6.8 stable release is **not** retroactively made self-updating. These changes take effect in a newly published release containing this code. No stable release has been published by this PR.

## Operator experience

1. Lightweight, delayed (25s) stable-release discovery via the fixed `masarray/ARIEC60870` GitHub API endpoint; no update on equal/older versions, draft or prerelease. A one-time **Update available** dialog is offered only if the station is idle; active IEC101/103/104 master, RX-only passive capture or replay gets an unobtrusive update button.
2. One user-approved click starts the verified installer download while the existing application continues to run. Active IEC master, passive monitoring or replay means **download now; install on normal exit**, never an automatic interruption on transfer completion. The compact button reports progress and then **Ready · install on exit**. A window close during the download cancels the transfer and launches no worker. Transient errors are retried with bounded backoff and leave the installed application untouched.
3. The download is streamed to `%LOCALAPPDATA%/ARIEC60870/updates/vX.Y.Z`, checked against the release's **exact** asset URL/name, declared file length, `SHA256SUMS.txt` and (if present) the GitHub asset `sha256:` digest. A mismatch fails closed.
4. Only after complete verification does WPF request its existing normal shutdown. The IEC active/passive finalization gate owns termination; if capture/transport cannot safely exit, no installation is started.
5. A copy of the already-running self-contained executable, outside the installation folder, waits for the parent PID to exit. It verifies the target against Inno's HKCU InstallLocation and refuses to install if another copy of that installed ArSee EXE is still running. It backs up the installed directory and invokes the per-user Inno Setup installer with a diagnostic log and visible progress; it does not order Restart Manager to close other apps, force-close any process, or reboot Windows.
6. Worker verifies installer exit status and resulting EXE version, then reopens the newly installed ArSee with a **one-time 192-bit token**. The new WPF `MainWindow.ContentRendered` writes a small acknowledgement containing PID, version and token. The worker does **not** declare success until the PID/token/version handshake matches. If the installer fails or the new app exits before this handshake, it verifies and attempts restoration of the previous executable and launches the former app. If the new app remains running but never confirms WPF readiness, the worker **does not overwrite running files**; it retains the backup and reports uncertainty. A local diagnostic is saved to `updates/last-update.log`. Successful staging/backup files are pruned; the locked worker copy is removed on the next normal startup.

User profiles, signal lists, stored capture/evidence and setup preferences in `%LOCALAPPDATA%/ARIEC60870` are never part of the installation backup/restore operation.

## Trust and limitations (do not advertise as unconditionally fool-proof)

- The current public releases are **unsigned**. This code authenticates the official HTTPS GitHub repository and matches the GitHub-hosted checksum evidence, with explicit operator approval before execution. This is not equivalent to Windows Authenticode publisher authentication, and cannot withstand compromise of the upstream repository/release credentials. A future signed release should validate a **pinned publisher identity** before installation. GitHub provenance/SBOM are not a substitute for runtime signature verification.
- A successful Inno exit, EXE version check and real-WPF startup ACK are **not** full transactional proof of Windows registry, shell association or machine crash recovery. The worker's backup best-effort restores files, but cannot guarantee repairing registry if a partial setup has already altered it. Interrupted power, full disks, AV/file locks and physical COM-port closure require independent Windows/FAT tests. Do not silently retry a partially running installer.
- The updater does **not** auto-reconnect to, command or poll a physical IEC device following restart. Normal setup/session controls remain operator owned.
- If Inno is still running after the bounded deadline, the worker deliberately does not race it by restoring files or starting another installation.
- The update checker is best effort and non-blocking. Offline/rate-limited GitHub checks do not impact protocol sessions.

## Automated negative contract

`ReleaseUpdateServiceTests` cover stable/draft naming rejects, exact download origin and path, checksum-to-file binding, hash and length rejection, and refusal to mutate a portable directory. Windows CI now **selectively triggers** a bounded staged-worker FAT when updater files change: it installs a real prior published binary, verifies graceful WPF exit, runs the new independent worker with a checksummed local test installer, proves new WPF PID/token/version acknowledgement, verifies one uninstall record and retained user data, and then cleans the CI installation. This complements the older direct-Inno prior-version and active-Master shutdown acceptance; it still cannot prove power-loss, antivirus interference or actual field COM cleanup.

## Windows qualification remaining before labeling production-hardened

Perform an end-to-end **new stable vN -> next stable vN+1** test using *two published GitHub release assets* on a standard Windows user account, including running active master and passive capture, offline and interrupted download, malformed checksum, running installer timeout, low disk, AV lock, old-app backup restoration, update app reopening, and cleanup. Use a simulated/bench device only for automated acceptance, not an unapproved energized station.


## Storage and rollback safety (13 November follow-up)

The updater checks writable volume capacity **before** downloading the installer and copying the independent worker. The worker checks both the backup volume and the target installation volume before starting Inno Setup, keeping a 128 MiB reserve on each check. These checks reduce (but cannot eliminate) out-of-space failure because available space may change during installation. The backup file inventory visits each nested directory explicitly and rejects reparse-point files, directory links and junctions rather than silently omitting them or following them outside the application tree. When a preflight fails, the existing ArSee installation is left in place.

**Windows CI limitation:** The targeted real Inno worker FAT currently fails even its isolated tiny, no-SetupLdr, file-only Inno canary before any installer log can be written (observed on GitHub-hosted Windows runners). This is an unverified installer execution environment, not successful FAT evidence. Source-level WPF/desktop/protocol validation is independent; a green build cannot substitute for verified installation, restart and rollback on Windows. Do not publish the feature as fully qualified merely because compilation, static analysis and unit tests pass.
