REM Copyright 2026 Ari Sulistiono
REM SPDX-License-Identifier: Apache-2.0
@echo off
setlocal
cd /d "%~dp0"
echo Starting ARIEC60870 WPF Master Tester...
dotnet run --project src\ARIEC60870.Desktop\ARIEC60870.Desktop.csproj
pause
