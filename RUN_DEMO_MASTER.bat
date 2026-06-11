REM Copyright 2026 Ari Sulistiono
REM SPDX-License-Identifier: Apache-2.0
@echo off
setlocal
cd /d "%~dp0"
echo Running ARIEC60870 master against the built-in simulated relay with sample user mapping profile...
echo Duration is 45 seconds so pickup, trip, auto reset, and repeat cycle can be observed.
dotnet run --project src\ARIEC60870.Cli -- master --simulate --duration 45 --mapping samples\mapping-profiles\example-user-mapping.profile.json --report out\demo-master-evidence.md --json out\demo-master-evidence.json
pause
