REM Copyright 2026 Ari Sulistiono
REM SPDX-License-Identifier: Apache-2.0
@echo off
setlocal
cd /d "%~dp0"

REM Edit COM port, link address, common address, and mapping profile before connecting to a real relay.
REM v0.8 writes Markdown and JSON evidence reports with Value Viewer and Relay Event Log sections.

dotnet run --project src\ARIEC60870.Cli -- master --port COM1 --baud 9600 --link 1 --ca 1 --duration 30 --mapping samples\mapping-profiles\example-user-mapping.profile.json --report out\master-evidence.md --json out\master-evidence.json

endlocal
