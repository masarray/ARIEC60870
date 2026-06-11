REM Copyright 2026 Ari Sulistiono
REM SPDX-License-Identifier: Apache-2.0
@echo off
setlocal
cd /d "%~dp0"

if "%~1"=="" (
  echo Usage:
  echo   ANALYZE_LOG.bat "D:\path\to\program.log"
  pause
  exit /b 1
)

set INPUT=%~1
set REPORT=%~n1.report.md
set JSON=%~n1.report.json

dotnet run --project src\ARIEC60870.Cli -- "%INPUT%" --report "%REPORT%" --json "%JSON%"

echo.
echo Report written:
echo   %REPORT%
echo   %JSON%
pause
