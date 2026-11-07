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

Portable ZIP releases remain available. The **existing** update checker
only notifies about newer GitHub releases and opens the release page. It
does **not** silently download or execute an unsigned installer. Full
production auto-update requires trusted Windows Authenticode signing,
pinned publisher identity, an independent staged helper, negative tests
for interruption and rollback, and a verified real-Windows upgrade matrix.
Unsigned CI artifacts are **test-only**, not autonomous-update payloads.

CI reinstall of the same version is an idempotence test; a genuine
previous-version to new-version upgrade and running-COM capture test
remain separate, explicitly pending FAT gates.
