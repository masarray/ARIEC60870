# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Real Windows legacy v3.6.17 installer -> new ArSee branding migration FAT.
.DESCRIPTION
  Runs only on a disposable GitHub-hosted Windows runner. Does NOT simulate
  prior installation with the new script: it installs the immutable published
  legacy Inno Setup, creates the observed two-shortcut regression state, and
  upgrades with the *exact current CI-built installer*.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CurrentSetup,
    [string]$EvidenceDir = 'artifacts/branding-migration-fat'
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
    $env:RUNNER_ENVIRONMENT -eq 'self-hosted') {
    throw 'Brand migration FAT is permitted on GitHub-hosted disposable Windows CI only.'
}
[xml]$properties = Get-Content Directory.Build.props
$version = [string]$properties.Project.PropertyGroup.Version
if ([version]$version -le [version]'3.6.17') {
    throw 'Migration requires a new version above legacy v3.6.17.'
}
$setup = (Resolve-Path $CurrentSetup).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDir)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$acceptance = Join-Path $evidence 'BRANDING-MIGRATION-ACCEPTANCE.txt'
$legacyDir = Join-Path $env:LOCALAPPDATA 'Programs\ARIEC60870'
$newDir = Join-Path $env:LOCALAPPDATA 'Programs\ArSee'
$legacyExe = Join-Path $legacyDir 'ARIEC60870.exe'
$newExe = Join-Path $legacyDir 'ArSee.exe'
$oldGroup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\ARIEC60870'
$newGroup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\ArSee'
$workspace = Join-Path $env:LOCALAPPDATA 'ARIEC60870'
$sentinel = Join-Path $workspace 'branding-migration-preserve.txt'
$oldInstaller = Join-Path $env:RUNNER_TEMP 'ARIEC60870-v3.6.17-setup-win-x64.exe'
$oldSums = Join-Path $env:RUNNER_TEMP 'ArSee-legacy-v3.6.17-SHA256SUMS.txt'
$official = 'https://github.com/masarray/arsee/releases/download/v3.6.17'
$uninstalled = $false

function Record([string]$message) {
    $line = (Get-Date -Format o) + ' ' + $message
    Write-Host $line
    Add-Content $acceptance $line -Encoding UTF8
}
function Invoke-Bounded([string]$file, [string[]]$arguments, [int]$seconds) {
    $child = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    try {
        if (!$child.WaitForExit($seconds * 1000)) {
            # No blind installer kill followed by a race against an in-progress install.
            throw "Installer/uninstaller exceeds $seconds seconds: $file. Manual runner cleanup required."
        }
        if ($child.ExitCode -ne 0) { throw "Installer/uninstaller exited $($child.ExitCode): $file" }
    } finally { $child.Dispose() }
}
function Get-ArSeeRegistrations {
    @(
        Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Where-Object {
            (Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue).DisplayName -eq 'ArSee'
        }
    )
}
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NORESTARTAPPLICATIONS')
try {
    if ((Test-Path $legacyDir) -or (Test-Path $newDir) -or (Get-ArSeeRegistrations).Count -gt 0) {
        throw 'Refuse to alter a runner that already has an ArSee installation.'
    }
    if (Test-Path $sentinel) { throw 'Refuse to overwrite existing user-data sentinel.' }
    $expectedNew = "ArSee-v$version-setup-win-x64.exe"
    if ((Split-Path $setup -Leaf) -ne $expectedNew) {
        throw "Current installer filename is not the expected $expectedNew"
    }
    $manifest = Join-Path (Split-Path $setup -Parent) 'SHA256SUMS.txt'
    if (!(Test-Path $manifest)) { throw 'Same-run current installer checksums missing.' }
    $entry = @(Get-Content $manifest | Where-Object { $_.EndsWith("  $expectedNew") })
    if ($entry.Count -ne 1) { throw 'Missing or ambiguous current installer SHA256 line.' }
    $sha = ([regex]::Match($entry[0], '^[0-9a-fA-F]{64}')).Value
    if (!$sha -or (Get-FileHash $setup -Algorithm SHA256).Hash -ine $sha) {
        throw 'Current same-run installer SHA256 mismatch.'
    }
    Record "PASS: exact current installer $expectedNew matches CI-built SHA256."

    curl.exe -fLsS --retry 3 --retry-delay 2 --max-time 180 -o $oldInstaller "$official/ARIEC60870-v3.6.17-setup-win-x64.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download published v3.6.17 installer.' }
    curl.exe -fLsS --retry 3 --retry-delay 2 --max-time 45 -o $oldSums "$official/SHA256SUMS.txt"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download published v3.6.17 checksums.' }
    $oldName = Split-Path $oldInstaller -Leaf
    $oldEntry = @(Get-Content $oldSums | Where-Object { $_.EndsWith("  $oldName") })
    if ($oldEntry.Count -ne 1) { throw 'Published legacy SHA256 line missing.' }
    $oldHash = ([regex]::Match($oldEntry[0], '^[0-9a-fA-F]{64}')).Value
    if (!$oldHash -or (Get-FileHash $oldInstaller -Algorithm SHA256).Hash -ine $oldHash) {
        throw 'Published legacy installer SHA256 mismatch.'
    }
    Record 'PASS: genuine immutable v3.6.17 installer checksum verified.'

    Invoke-Bounded $oldInstaller $arguments 150
    if (!(Test-Path $legacyExe) -or
        (Get-Item $legacyExe).VersionInfo.FileVersion -notlike '3.6.17*') {
        throw 'Original v3.6.17 installation missing at documented legacy path.'
    }
    if ((Get-ArSeeRegistrations).Count -ne 1) { throw 'Previous version has duplicate uninstall registrations.' }
    Record 'PASS: installed genuine old Windows EXE, same AppId and uninstall record.'

    # Recreate the reported post-upgrade two-shortcut condition using real .lnk files.
    New-Item -ItemType Directory -Force $oldGroup,$workspace | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    foreach ($name in @('ARIEC60870','ArSee')) {
        $link = $shell.CreateShortcut((Join-Path $oldGroup "$name.lnk"))
        $link.TargetPath = $legacyExe
        $link.Save()
    }
    'retain-user-evidence' | Set-Content $sentinel -Encoding utf8
    Record 'PASS: seeded two real legacy Start Menu shortcuts and app-data sentinel.'

    Invoke-Bounded $setup $arguments 180
    if (!(Test-Path $newExe)) { throw 'ArSee.exe missing from upgraded installation.' }
    if ((Get-Item $newExe).VersionInfo.FileVersion -notlike "$version*") {
        throw 'Upgraded ArSee.exe does not contain current release version.'
    }
    if (Test-Path $legacyExe) { throw 'Legacy ARIEC60870.exe remains beside ArSee.exe.' }
    if (!(Test-Path (Join-Path $newGroup 'ArSee.lnk'))) {
        throw 'New ArSee Start Menu shortcut was not installed.'
    }
    foreach ($link in @('ARIEC60870.lnk','ArSee.lnk')) {
        if (Test-Path (Join-Path $oldGroup $link)) {
            throw "Legacy Start Menu shortcut $link was not removed."
        }
    }
    if ((Get-ArSeeRegistrations).Count -ne 1) { throw 'Upgrade produced duplicate ArSee uninstall entries.' }
    $registry = (Get-ArSeeRegistrations)[0]
    if ((Get-ItemProperty $registry.PSPath).DisplayVersion -ne $version) {
        throw 'ArSee uninstall record reports an unexpected version.'
    }
    if ((Get-Content $sentinel -Raw).Trim() -ne 'retain-user-evidence') {
        throw 'Upgrade modified user-owned application data.'
    }
    $smoke = Start-Process -FilePath $newExe -ArgumentList '--passive-gui-smoke' -PassThru -Wait
    if ($smoke.ExitCode -ne 0) { throw "Upgraded ArSee WPF smoke failed: $($smoke.ExitCode)" }
    Record 'PASS: one ArSee.exe, one ArSee shortcut/group, no legacy shortcut/EXE, one uninstall registration, WPF smoke, intact user data.'
    Record 'NOTE: historical install directory intentionally reused for safe same-AppId in-place upgrade. Fresh installations use Programs/ArSee.'

    $uninstall = Join-Path $legacyDir 'unins000.exe'
    if (!(Test-Path $uninstall)) { throw 'Uninstaller missing after upgrade.' }
    Invoke-Bounded $uninstall @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') 150
    $uninstalled = $true
    if ((Test-Path $newExe) -or (Test-Path $legacyExe) -or (Get-ArSeeRegistrations).Count -gt 0) {
        throw 'Uninstall did not remove the application or uninstall registration.'
    }
    if ((Get-Content $sentinel -Raw).Trim() -ne 'retain-user-evidence') {
        throw 'Uninstaller deleted user-owned data.'
    }
    Record 'PASS: uninstall removes executable/registration and preserves user workspace.'
}
catch {
    Record ('FAIL: ' + $_.Exception.Message)
    throw
}
finally {
    if (!$uninstalled -and (Test-Path (Join-Path $legacyDir 'unins000.exe'))) {
        Record 'NOTE: CI migration failure, attempting safe runner cleanup.'
        try { Invoke-Bounded (Join-Path $legacyDir 'unins000.exe') @('/VERYSILENT','/NORESTART') 90 }
        catch { Record ('WARNING: CI runner cleanup incomplete: ' + $_.Exception.Message) }
    }
}
