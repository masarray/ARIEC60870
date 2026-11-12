# Inno Setup Windows Installer — initial acceptance

The installer is compiled from the **same self-contained Windows x64 WPF
publish** used by the successful CI GUI smoke. Its stable AppId reuses the
existing per-user installation directory for in-place upgrades and registers
one uninstall entry. Normal installation offers an optional shortcut and
offers to launch the application after success.

## Build

Run the Windows CI job from the PR. The Inno output is
`ARIEC60870-vX.Y.Z-setup-win-x64.exe` plus `SHA256SUMS.txt`.
The CI gate installs silently, smoke-tests the installed WPF executable,
reinstalls it, checks a user-data sentinel, checks the uninstall entry and
uninstalls while retaining %LOCALAPPDATA%\\ARIEC60870.

## Safety and limitations

The installer uses Windows Restart Manager with `CloseApplications=yes` and
`RestartApplications=no`, **not** process force termination. The application
already handles passive monitor closure by cancelling the capture task and
waiting for journal finalization. A held COM port or rejected closure may
require operator interaction; automatic zero-downtime upgrade is **not**
proven. Never use unattended deployment against active plant-side operations.

Portable ZIP releases remain available. The update implementation on the updater PR now performs an **operator-approved**
GitHub stable release download, digest verification, normal capture-safe close,
independent staged worker/Inno installation and restart for **managed** installs;
portable folders are never silently modified. Published unsigned installers
are accepted only with deliberate operator consent, not silently. Full
production-hardening still requires Windows Authenticode publisher pinning,
interruption and rollback FAT and a verified real two-version update matrix.
See [self-update architecture](AUTO_UPDATE_ARCHITECTURE_20261113.md).

Windows CI now builds a temporary v3.6.7 installer from the **actual published
v3.6.7 Windows ZIP**, verifying the published SHA256SUMS first. The same
stable AppId then installs v3.6.8 over a running previous-version WPF binary.
It asserts current file/registry version, exactly one uninstall entry, retained
local data, and a graceful close of a running **simulated active Master** during
an in-place reinstall. The previous temporary installer is **not shipped**.

This is an integration test of an actual old binary and normal Windows
shutdown, not a substitute for 1200-baud physical serial FAT. Real COM handles,
electrical disconnects, queued commands, interrupted writes and OS-disk-full
recovery remain explicitly pending field tests.

## Windows shutdown ownership

When a standard Windows close is requested while an **active** IEC101/103/104
master is running, `MainWindow_ClosingPassive` (the existing shared window
Closing hook) first requests cancellation and closes active A/B transports. It
then awaits the Master session's completion signal, set **after** final session
UI state and transport disposal. If cancellation/finalization exceeds the
bounded deadline, it cancels the window close and informs the engineer.
This is the same safety policy for manual close and Restart Manager; the
installer does not force-kill processes. The parallel passive capture close
path continues to await journal flush before leaving.

The `--installer-master-close-smoke --ready-file PATH` argument is for Windows
CI only. It runs the genuine active Master against the in-process fixture,
with no physical COM/TCP device. The installer requests normal WPF shutdown.
This does not enable auto-connect after an update.
