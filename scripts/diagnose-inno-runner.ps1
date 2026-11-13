# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
# Non-destructive native installer diagnostic: one synthetic LICENSE-only
# Inno Setup canary. Never installs or uninstalls ArSee itself.
param([int]$TimeoutSeconds = 25, [ValidatePattern('^[a-z0-9-]{1,24}

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
  throw 'This diagnostic may only run on a disposable Windows GitHub Actions runner.'
}
if ($TimeoutSeconds -lt 5 -or $TimeoutSeconds -gt 45) {
  throw 'Installer diagnostic timeout must be between 5 and 45 seconds.'
}

$source = (Resolve-Path '.').Path
$output = Join-Path $env:RUNNER_TEMP "arsee-inno-runtime-probe-$RunLabel"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
$exe = Join-Path $output 'ARIEC60870-inno-ci-canary.exe'
$log = Join-Path $output 'inno-canary.log'
$diagnostic = Join-Path $output 'diagnostic.txt'
$start = Get-Date
$proc = $null
$status = 'not-started'
try {
  if (!(Test-Path $iscc)) { throw "Missing Inno Setup compiler: $iscc" }
  & $iscc "/DOutputRoot=$output" installer/ARIEC60870.CI-Canary.iss |
    Out-File -FilePath (Join-Path $output 'compile.txt') -Encoding utf8
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $exe)) {
    throw 'The simple Inno canary could not compile.'
  }
  $args = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /LOG="' + $log + '"'
  $proc = Start-Process -FilePath $exe -ArgumentList $args -PassThru
  $status = if ($proc.WaitForExit($TimeoutSeconds * 1000)) {
    "completed-exit-$($proc.ExitCode)"
  } else { 'prelog-timeout' }

  if ($status -eq 'prelog-timeout') {
    'Executable did not exit before the bounded deadline.' | Out-File $diagnostic
    'Github-hosted runner/session evidence:' | Out-File $diagnostic -Append
    "UserInteractive: $([Environment]::UserInteractive); SESSIONNAME: $env:SESSIONNAME; runner user: $env:USERNAME" |
      Out-File $diagnostic -Append
    "PowerShell session: $([Diagnostics.Process]::GetCurrentProcess().SessionId); Inno parent session: $($proc.SessionId)" |
      Out-File $diagnostic -Append
    "Inno start: $start; log exists: $(Test-Path $log)" | Out-File $diagnostic -Append
    Get-CimInstance Win32_Process |
      Where-Object { $_.ProcessId -eq $proc.Id -or $_.ParentProcessId -eq $proc.Id -or $_.Name -match '(?i)^setup.*\.tmp$' } |
      Select-Object ProcessId, ParentProcessId, SessionId, Name, CommandLine |
      Format-List | Out-String | Out-File $diagnostic -Append
    try {
      $proc.Refresh()
      $proc.Threads | Select-Object Id, ThreadState, WaitReason |
        Format-Table -AutoSize | Out-String | Out-File $diagnostic -Append
    } catch { "Threads unavailable: $($_.Exception.Message)" | Out-File $diagnostic -Append }
    foreach ($eventLog in @(
      'Microsoft-Windows-CodeIntegrity/Operational',
      'Microsoft-Windows-AppLocker/EXE and DLL',
      'Microsoft-Windows-Windows Defender/Operational',
      'System')) {
      try {
        $events = @(Get-WinEvent -FilterHashtable @{
          LogName=$eventLog; StartTime=$start.AddSeconds(-10)
        } -MaxEvents 80 -ErrorAction Stop |
          Where-Object { $_.Message -match '(?i)(block|deny|setup|inno|dll|access|timeout|failed)' } |
          Select-Object -First 12 TimeCreated,Id,ProviderName,Message)
        "EVENT LOG $eventLog (matching rows: $($events.Count))" | Out-File $diagnostic -Append
        $events | Format-List | Out-String | Out-File $diagnostic -Append
      } catch {
        "EVENT LOG $eventLog unavailable: $($_.Exception.Message)" | Out-File $diagnostic -Append
      }
    }
  } else {
    "Inno canary $status; log exists $(Test-Path $log)" | Out-File $diagnostic
  }
} finally {
  if ($proc -and !$proc.HasExited) {
    # Only the tiny test canary is killed on the disposable CI runner.
    try { $proc.Kill($true); $proc.WaitForExit(5000) | Out-Null } catch { }
  }
  if (!(Test-Path $diagnostic)) {
    "Inno canary status $status" | Out-File $diagnostic
  }
  Write-Host "Inno native diagnostic result: $status"
  Get-Content $diagnostic -TotalCount 90 | Write-Host
}
# Preserve the distinction between a source failure and a blocked runner:
# the workflow step explicitly continues on failure, but the evidence file
# and job summary always say whether native installer execution was proven.
if ($status -ne 'completed-exit-0') {
  throw "Windows runner's tiny Inno canary did not complete successfully: $status"
}
)][string]$RunLabel = 'initial')

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
  throw 'This diagnostic may only run on a disposable Windows GitHub Actions runner.'
}
if ($TimeoutSeconds -lt 5 -or $TimeoutSeconds -gt 45) {
  throw 'Installer diagnostic timeout must be between 5 and 45 seconds.'
}

$source = (Resolve-Path '.').Path
$output = Join-Path $env:RUNNER_TEMP 'arsee-inno-runtime-probe'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
$exe = Join-Path $output 'ARIEC60870-inno-ci-canary.exe'
$log = Join-Path $output 'inno-canary.log'
$diagnostic = Join-Path $output 'diagnostic.txt'
$start = Get-Date
$proc = $null
$status = 'not-started'
try {
  if (!(Test-Path $iscc)) { throw "Missing Inno Setup compiler: $iscc" }
  & $iscc "/DOutputRoot=$output" installer/ARIEC60870.CI-Canary.iss |
    Out-File -FilePath (Join-Path $output 'compile.txt') -Encoding utf8
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $exe)) {
    throw 'The simple Inno canary could not compile.'
  }
  $args = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /LOG="' + $log + '"'
  $proc = Start-Process -FilePath $exe -ArgumentList $args -PassThru
  $status = if ($proc.WaitForExit($TimeoutSeconds * 1000)) {
    "completed-exit-$($proc.ExitCode)"
  } else { 'prelog-timeout' }

  if ($status -eq 'prelog-timeout') {
    'Executable did not exit before the bounded deadline.' | Out-File $diagnostic
    'Github-hosted runner/session evidence:' | Out-File $diagnostic -Append
    "UserInteractive: $([Environment]::UserInteractive); SESSIONNAME: $env:SESSIONNAME; runner user: $env:USERNAME" |
      Out-File $diagnostic -Append
    "PowerShell session: $([Diagnostics.Process]::GetCurrentProcess().SessionId); Inno parent session: $($proc.SessionId)" |
      Out-File $diagnostic -Append
    "Inno start: $start; log exists: $(Test-Path $log)" | Out-File $diagnostic -Append
    Get-CimInstance Win32_Process |
      Where-Object { $_.ProcessId -eq $proc.Id -or $_.ParentProcessId -eq $proc.Id -or $_.Name -match '(?i)^setup.*\.tmp$' } |
      Select-Object ProcessId, ParentProcessId, SessionId, Name, CommandLine |
      Format-List | Out-String | Out-File $diagnostic -Append
    try {
      $proc.Refresh()
      $proc.Threads | Select-Object Id, ThreadState, WaitReason |
        Format-Table -AutoSize | Out-String | Out-File $diagnostic -Append
    } catch { "Threads unavailable: $($_.Exception.Message)" | Out-File $diagnostic -Append }
    foreach ($eventLog in @(
      'Microsoft-Windows-CodeIntegrity/Operational',
      'Microsoft-Windows-AppLocker/EXE and DLL',
      'Microsoft-Windows-Windows Defender/Operational',
      'System')) {
      try {
        $events = @(Get-WinEvent -FilterHashtable @{
          LogName=$eventLog; StartTime=$start.AddSeconds(-10)
        } -MaxEvents 80 -ErrorAction Stop |
          Where-Object { $_.Message -match '(?i)(block|deny|setup|inno|dll|access|timeout|failed)' } |
          Select-Object -First 12 TimeCreated,Id,ProviderName,Message)
        "EVENT LOG $eventLog (matching rows: $($events.Count))" | Out-File $diagnostic -Append
        $events | Format-List | Out-String | Out-File $diagnostic -Append
      } catch {
        "EVENT LOG $eventLog unavailable: $($_.Exception.Message)" | Out-File $diagnostic -Append
      }
    }
  } else {
    "Inno canary $status; log exists $(Test-Path $log)" | Out-File $diagnostic
  }
} finally {
  if ($proc -and !$proc.HasExited) {
    # Only the tiny test canary is killed on the disposable CI runner.
    try { $proc.Kill($true); $proc.WaitForExit(5000) | Out-Null } catch { }
  }
  if (!(Test-Path $diagnostic)) {
    "Inno canary status $status" | Out-File $diagnostic
  }
  Write-Host "Inno native diagnostic result: $status"
  Get-Content $diagnostic -TotalCount 90 | Write-Host
}
# Preserve the distinction between a source failure and a blocked runner:
# the workflow step explicitly continues on failure, but the evidence file
# and job summary always say whether native installer execution was proven.
if ($status -ne 'completed-exit-0') {
  throw "Windows runner's tiny Inno canary did not complete successfully: $status"
}

# A second canary in the same job must not inherit the first installation.
# This cleanup touches only the synthetic dedicated CI fixture.
if ($status -eq 'completed-exit-0') {
  $uninstaller = Join-Path $env:LOCALAPPDATA 'Programs/ARIEC60870-CI-Canary/unins000.exe'
  if (!(Test-Path $uninstaller)) { throw 'Inno canary reported success without an installed uninstaller.' }
  $remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
  if (!$remove.WaitForExit(25000)) {
    try { $remove.Kill($true) } catch { }
    throw 'Synthetic CI canary uninstall timed out.'
  }
  if ($remove.ExitCode -ne 0) { throw "Synthetic CI canary uninstall returned $($remove.ExitCode)." }
}
