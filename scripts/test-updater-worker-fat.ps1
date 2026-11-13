# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
# Windows-only live integration test of the staged worker, not just Inno CLI.
# Uses actual previous public release from the preceding installer CI step.
param([string]$PreviousVersion = '3.6.7')

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
  throw 'CI-only fixture: this script installs and uninstalls ArSee. Never run it on an engineer workstation.'
}
[xml]$props = Get-Content Directory.Build.props
$version = [string]$props.Project.PropertyGroup.Version
if ([version]$version -le [version]$PreviousVersion) {
  throw "Worker FAT requires current version > previous $PreviousVersion."
}

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
$source = Join-Path $env:RUNNER_TEMP 'ariec-old-release'
$currentSource = (Resolve-Path 'artifacts/passive-gui-publish/ARIEC60870.Desktop.exe').Path
$output = (Resolve-Path 'artifacts/installer').Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs/ARIEC60870'
$installedExe = Join-Path $installDir 'ARIEC60870.exe'
$stage = [IO.Path]::Combine($env:LOCALAPPDATA, 'ARIEC60870', 'updates', "v$version")
$planPath = Join-Path $stage 'install-plan.json'
$ackPath = Join-Path $stage 'startup-ack.json'
$donePath = Join-Path $stage 'completed.marker'
$log = Join-Path $stage 'worker-fat.txt'
$currentSetup = Join-Path $output "ARIEC60870-v$version-setup-win-x64.exe"
$oldSetup = Join-Path $output "ARIEC60870-v$PreviousVersion-setup-win-x64.exe"
$oldInstallLog = Join-Path $output 'UPDATER-FAT-OLD-INNO.log'
$oldApp = $null
$workerProcess = $null
$launched = $null
function Invoke-Bounded {
  param([string]$File, [string]$Args, [int]$TimeoutSec = 95)
  Write-Host "CI fixture: starting $(Split-Path $File -Leaf) (bounded $TimeoutSec s)"
  $process = Start-Process -FilePath $File -ArgumentList $Args -PassThru
  if (!$process.WaitForExit($TimeoutSec * 1000)) {
    Write-Warning "CI fixture installer timed out: PID $($process.Id)"
    Get-CimInstance Win32_Process | Where-Object {
      $_.Name -match '(?i)(setup|unins|ARIEC60870|ArSee)'
    } | Select-Object ProcessId,ParentProcessId,Name,CommandLine |
      Format-Table -AutoSize | Out-String | Write-Host
    if (Test-Path $oldInstallLog) {
      Write-Host 'Inno Setup diagnostic tail:'
      Get-Content $oldInstallLog -Tail 70
    } else {
      Write-Warning 'No Inno Setup log was created; possible pre-install prompt/early loader stall.'
    }
    # CI-only fixture cleanup; production worker NEVER kills installer processes.
    try { $process.Kill($true) } catch { }
    throw "CI fixture installer exceeded $TimeoutSec seconds: $File"
  }
  if ($process.ExitCode -ne 0) {
    if (Test-Path $oldInstallLog) { Get-Content $oldInstallLog -Tail 70 }
    throw "CI fixture process failed with exit $($process.ExitCode): $File"
  }
  Write-Host "CI fixture process exit 0: $(Split-Path $File -Leaf)"
}
try {
  # Small Inno canary distinguishes a broken Windows runner/Setup loader
  # from a large ArSee packaging problem before expensive version-cross FAT.
  $canaryInstall = Join-Path $env:LOCALAPPDATA 'Programs/ARIEC60870-CI-Canary'
  $canaryBinary = Join-Path $output 'ARIEC60870-inno-ci-direct.exe'
  $canaryLog = Join-Path $output 'UPDATER-FAT-CANARY-DIRECT-INNO.log'
  & $iscc "/DOutputRoot=$output" installer/ARIEC60870.CI-Canary.iss
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $canaryBinary)) {
    throw 'Minimal Inno Setup canary compilation failed.'
  }
  $canaryArgs = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /LOG="' + $canaryLog + '"'
  if (!(Test-Path (Join-Path $output 'ARIEC60870-inno-ci-direct-1.bin'))) {
    throw 'Direct Setup binary must have its separate data payload; compiler output incomplete.'
  }
  Invoke-Bounded -File $canaryBinary -Args $canaryArgs -TimeoutSec 40
  if (!(Test-Path (Join-Path $canaryInstall 'canary.txt'))) {
    throw 'Minimal Inno Setup canary installed no payload.'
  }
  $canaryUninstall = Join-Path $canaryInstall 'unins000.exe'
  Invoke-Bounded -File $canaryUninstall -Args '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -TimeoutSec 35
  Write-Host 'Direct Setup (UseSetupLdr=no) canary compiled, installed, and uninstalled successfully; bundled SetupLdr remains separately unqualified.'

  if (!(Test-Path (Join-Path $source 'ARIEC60870.exe'))) {
    # Dedicated worker FAT must not wait for the separate legacy installer
    # gate. Acquire and verify the previous immutable public release itself.
    New-Item -ItemType Directory -Force -Path $source | Out-Null
    $baseUrl = "https://github.com/masarray/ARIEC60870/releases/download/v$PreviousVersion"
    $oldZip = Join-Path $env:RUNNER_TEMP 'arsee-updater-previous.zip'
    $sumFile = Join-Path $env:RUNNER_TEMP 'arsee-updater-previous-sums.txt'
    curl.exe -fLsS --retry 3 --retry-delay 2 --max-time 180 -o $oldZip "$baseUrl/ARIEC60870-v$PreviousVersion-win-x64.zip"
    if ($LASTEXITCODE -ne 0) { throw 'Previous public release download failed.' }
    curl.exe -fLsS --retry 3 --retry-delay 2 --max-time 30 -o $sumFile "$baseUrl/SHA256SUMS.txt"
    if ($LASTEXITCODE -ne 0) { throw 'Previous release checksum download failed.' }
    $expectedLine = Get-Content $sumFile |
      Where-Object { $_.EndsWith("  ARIEC60870-v$PreviousVersion-win-x64.zip") } |
      Select-Object -First 1
    if (!$expectedLine) { throw 'Canonical previous ZIP digest missing.' }
    $previousDigest = ([regex]::Match($expectedLine, '^[0-9a-fA-F]{64}')).Value
    if (!$previousDigest -or
        (Get-FileHash $oldZip -Algorithm SHA256).Hash -ine $previousDigest) {
      throw 'Previous public release SHA-256 mismatch.'
    }
    Expand-Archive -Path $oldZip -DestinationPath $source -Force
  }
  if (!(Test-Path (Join-Path $source 'ARIEC60870.exe'))) {
    throw 'Verified previous release does not contain ARIEC60870.exe.'
  }
  if (!(Test-Path $currentSetup) -or !(Test-Path $currentSource)) {
    throw 'Current installer or tested WPF binary missing.'
  }

  & $iscc "/DAppVersion=$PreviousVersion" "/DSourceRoot=$source" "/DSourceExe=ARIEC60870.exe" "/DOutputRoot=$output" installer/ARIEC60870.iss
  if ($LASTEXITCODE -ne 0 -or !(Test-Path $oldSetup)) {
    throw 'Previous-version installer compilation failed.'
  }
  # /SP- is explicit for unattended runs, even if the Inno compiler's
  # current default already disables startup prompts. The full log proves
  # whether the loader, Restart Manager, or filesystem is the bottleneck.
  $oldArgs = '/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NORESTARTAPPLICATIONS /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /LOG="' + $oldInstallLog + '"'
  Invoke-Bounded -File $oldSetup -Args $oldArgs -TimeoutSec 95
  if (!(Test-Path $installedExe) -or
      (Get-Item $installedExe).VersionInfo.FileVersion -notlike "$PreviousVersion*") {
    throw 'Prior installed executable version is not genuine previous release.'
  }

  $userData = Join-Path $env:LOCALAPPDATA 'ARIEC60870'
  New-Item -ItemType Directory -Force -Path $userData | Out-Null
  $sentinel = Join-Path $userData 'updater-worker-preserve-ci.txt'
  'retain-user-evidence' | Set-Content $sentinel

  New-Item -ItemType Directory -Force -Path $stage | Out-Null
  $workerExe = Join-Path $stage 'ArSee.UpdateWorker.exe'
  Copy-Item $currentSource $workerExe -Force
  $installer = Join-Path $stage (Split-Path $currentSetup -Leaf)
  Copy-Item $currentSetup $installer -Force
  $size = (Get-Item $installer).Length
  $digest = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
  $oldApp = Start-Process -FilePath $installedExe -PassThru
  Start-Sleep -Seconds 3
  if ($oldApp.HasExited) { throw 'Old WPF app did not remain open.' }

  $plan = [ordered]@{
    Tag = "v$version"
    InstallerPath = $installer
    InstallerSha256 = $digest
    InstallerBytes = $size
    InstalledExecutable = $installedExe
    ParentPid = $oldApp.Id
    StageDirectory = $stage
  }
  $plan | ConvertTo-Json -Depth 3 | Set-Content $planPath -Encoding utf8
  # A failing worker must return its nonzero result/log in headless CI,
  # instead of showing an inaccessible Windows modal dialog.
  $env:ARSEE_UPDATER_CI_MODE = '1'
  $workerProcess = Start-Process -FilePath $workerExe -ArgumentList "--apply-release-update `"$planPath`"" -PassThru
  Start-Sleep -Seconds 2
  if (!$oldApp.CloseMainWindow()) {
    throw 'Old WPF application rejected normal operator close.'
  }
  if (!$oldApp.WaitForExit(30000)) {
    throw 'Old WPF application failed to finish normal close.'
  }
  if (!$workerProcess.WaitForExit(210000)) {
    throw 'Worker exceeded 210s. Do not kill the real installer while it might be installing.'
  }
  if ($workerProcess.ExitCode -ne 0) {
    $updateLog = Join-Path $userData 'updates/last-update.log'
    if (Test-Path $updateLog) { Get-Content $updateLog -Tail 12 }
    throw "Autonomous update worker exit $($workerProcess.ExitCode)"
  }
  if (!(Test-Path $ackPath) -or !(Test-Path $donePath)) {
    throw 'Worker did not receive first-render WPF acknowledgement and completion marker.'
  }
  $ack = Get-Content $ackPath -Raw | ConvertFrom-Json
  $launched = Get-Process -Id $ack.Pid -ErrorAction SilentlyContinue
  if (!$launched -or $launched.HasExited) {
    throw 'New WPF process is not running after success.'
  }
  if ($ack.FileVersion -notlike "$version*" -or
      $ack.Token -notmatch '^[0-9A-F]{48}$' -or
      (Get-Item $installedExe).VersionInfo.FileVersion -notlike "$version*") {
    throw 'Handshake, updated EXE version, or token did not match the release.'
  }
  if ((Get-Content $sentinel -Raw).Trim() -ne 'retain-user-evidence') {
    throw 'Staged self-update destroyed user-owned data.'
  }

  $registry = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Where-Object { (Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue).DisplayName -eq 'ArSee' })
  if ($registry.Count -ne 1 -or
      (Get-ItemProperty $registry[0].PsPath).DisplayVersion -ne $version) {
    throw 'Staged worker left stale or duplicate Inno uninstall registration.'
  }

  "PASS: verified old v$PreviousVersion installed WPF normal close -> independent staged worker -> Inno v$version -> actual new WPF ContentRendered/PID/token ACK -> intact user data and uninstall registry." |
    Set-Content $log -Encoding utf8
  Copy-Item $log (Join-Path $output 'UPDATER-WORKER-ACCEPTANCE.txt') -Force
}
finally {
  if (Test-Path (Join-Path $output 'UPDATER-FAT-CANARY-DIRECT-INNO.log')) {
    Write-Host 'CI Inno canary diagnostic tail:'
    Get-Content (Join-Path $output 'UPDATER-FAT-CANARY-INNO.log') -Tail 10
  }
  if (Test-Path $oldInstallLog) {
    Write-Host 'Old installer diagnostic tail:'
    Get-Content $oldInstallLog -Tail 12
  }
  $env:ARSEE_UPDATER_CI_MODE = $null
  if ($launched -and !$launched.HasExited) {
    try { $null = $launched.CloseMainWindow(); $null = $launched.WaitForExit(20000) } catch { }
  }
  if ($oldApp -and !$oldApp.HasExited) {
    try { $null = $oldApp.CloseMainWindow(); $null = $oldApp.WaitForExit(10000) } catch { }
  }
  # A CI-only cleanup may stop leaked fixture apps. Never do this on a field PC.
  if ($workerProcess -and !$workerProcess.HasExited) {
    Write-Warning 'Updater worker still active: leaving fixture installation untouched to avoid concurrent installer writes.'
  } else {
    $uninstall = Join-Path $installDir 'unins000.exe'
    if (Test-Path $uninstall) {
      Invoke-Bounded -File $uninstall -Args '/VERYSILENT /NORESTART'
    }
  }
}
