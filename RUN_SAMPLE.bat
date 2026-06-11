REM Copyright 2026 Ari Sulistiono
REM SPDX-License-Identifier: Apache-2.0
@echo off
setlocal
cd /d "%~dp0"

echo Building ARIEC60870...
dotnet build ARIEC60870.sln
if errorlevel 1 (
  echo.
  echo Build failed. Please check that .NET 8 SDK is installed.
  pause
  exit /b 1
)

echo.
echo Running sample analysis...
dotnet run --project src\ARIEC60870.Cli -- samples\sample_iec103_trace.log --report out\sample-report.md --json out\sample-report.json

echo.
echo Output:
echo   out\sample-report.md
echo   out\sample-report.json
pause
