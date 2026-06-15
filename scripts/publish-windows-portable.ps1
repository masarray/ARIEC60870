# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Builds Windows x64 release packages for ARIEC60870.

.DESCRIPTION
  Creates a Windows release ZIP containing the WPF desktop app, CLI tools,
  sample mapping profiles, license files, and quick-start documents. By default
  the script produces the portable multi-file package. Use -SingleFile to create
  a package where each executable is published as a single-file self-contained app.

.EXAMPLE
  pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5

.EXAMPLE
  pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5 -SingleFile
#>
[CmdletBinding()]
param(
    [string]$Version = "3.6.5",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$FrameworkDependent,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ArtifactRoot = Join-Path $RepoRoot "artifacts"
$ReleaseRoot = Join-Path $ArtifactRoot "release"
$PackageFlavor = if ($SingleFile) { "singlefile" } else { "portable" }
$StagingRoot = Join-Path $ReleaseRoot "ARIEC60870-v$Version-$Runtime-$PackageFlavor"
$DesktopOut = Join-Path $StagingRoot "app"
$CliOut = Join-Path $StagingRoot "cli"
$PackageZip = Join-Path $ReleaseRoot "ARIEC60870-v$Version-$Runtime-$PackageFlavor.zip"
$ChecksumFile = Join-Path $ReleaseRoot "SHA256SUMS.txt"
$SelfContained = if ($FrameworkDependent) { "false" } else { "true" }
$PublishSingleFile = if ($SingleFile) { "true" } else { "false" }

Write-Host "ARIEC60870 Windows release packaging" -ForegroundColor Cyan
Write-Host "Repository      : $RepoRoot"
Write-Host "Version         : $Version"
Write-Host "Runtime         : $Runtime"
Write-Host "Configuration   : $Configuration"
Write-Host "Package flavor  : $PackageFlavor"
Write-Host "Self-contained  : $SelfContained"
Write-Host "Single-file exe : $PublishSingleFile"

Push-Location $RepoRoot
try {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
    if (Test-Path $StagingRoot) {
        Remove-Item $StagingRoot -Recurse -Force
    }
    if (Test-Path $PackageZip) {
        Remove-Item $PackageZip -Force
    }

    New-Item -ItemType Directory -Force -Path $DesktopOut, $CliOut | Out-Null

    dotnet restore ARIEC60870.sln
    dotnet build ARIEC60870.sln --configuration $Configuration --no-restore -p:Version=$Version

    if (-not $SkipTests) {
        dotnet run --project tests/ARIEC60870.Protocol.Tests/ARIEC60870.Protocol.Tests.csproj --configuration $Configuration --no-build
    }

    dotnet publish src/ARIEC60870.Desktop/ARIEC60870.Desktop.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained:$SelfContained `
        -p:Version=$Version `
        -p:PublishSingleFile=$PublishSingleFile `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $DesktopOut

    dotnet publish src/ARIEC60870.Cli/ARIEC60870.Cli.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained:$SelfContained `
        -p:Version=$Version `
        -p:PublishSingleFile=$PublishSingleFile `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $CliOut

    $DocsOut = Join-Path $StagingRoot "docs"
    $SamplesOut = Join-Path $StagingRoot "samples"
    $ProfilesOut = Join-Path $StagingRoot "profiles"
    New-Item -ItemType Directory -Force -Path $DocsOut, $SamplesOut, $ProfilesOut | Out-Null

    Copy-Item LICENSE, NOTICE, THIRD_PARTY_NOTICES.md, README.md, CHANGELOG.md -Destination $StagingRoot -Force
    Copy-Item docs/QUICK_START.md, docs/TROUBLESHOOTING.md, docs/VALIDATION_MATRIX.md, docs/RELEASE_PACKAGING.md -Destination $DocsOut -Force
    Copy-Item samples/mapping-profiles -Destination $SamplesOut -Recurse -Force
    if (Test-Path profiles) {
        Copy-Item profiles/* -Destination $ProfilesOut -Recurse -Force
    }

    $LaunchDesktop = @"
@echo off
setlocal
cd /d "%~dp0app"
start "ARIEC60870" "ARIEC60870.Desktop.exe"
"@
    Set-Content -Path (Join-Path $StagingRoot "Start-ARIEC60870.bat") -Value $LaunchDesktop -Encoding ASCII

    $CliHelp = @"
@echo off
setlocal
cd /d "%~dp0cli"
"ARIEC60870.Cli.exe" --help
pause
"@
    Set-Content -Path (Join-Path $StagingRoot "Open-CLI-Help.bat") -Value $CliHelp -Encoding ASCII

    $PortableReadme = @"
ARIEC60870 v$Version Windows $PackageFlavor package

Start desktop app:
  Start-ARIEC60870.bat

Open CLI help:
  Open-CLI-Help.bat

Included folders:
  app\       Windows desktop application
  cli\       Command-line tools
  docs\      Quick start, troubleshooting, validation matrix, packaging notes
  samples\   Example user mapping profile
  profiles\  Neutral IEC-101/104 example point profile

Recommended first check:
  1. Start the desktop app.
  2. Open Setup.
  3. Select IEC-103 serial, IEC-101 serial, or IEC-104 TCP/IP.
  4. Configure COM/TCP endpoint, link/common address, timeout, GI, and mapping.
  5. Start the session.
  6. Review Operator Evidence, Value Viewer, Event Log, Frame Trace, Diagnostics, and Report Preview.
  7. Export evidence after the session.

Do not share exported evidence externally before reviewing project/customer names,
communication settings, mapping labels, and raw protocol evidence.
"@
    Set-Content -Path (Join-Path $StagingRoot "README-PORTABLE.txt") -Value $PortableReadme -Encoding UTF8

    Compress-Archive -Path (Join-Path $StagingRoot "*") -DestinationPath $PackageZip -CompressionLevel Optimal

    $Hash = Get-FileHash -Algorithm SHA256 $PackageZip
    $ChecksumLine = "{0}  {1}" -f $Hash.Hash.ToLowerInvariant(), (Split-Path $PackageZip -Leaf)
    if (Test-Path $ChecksumFile) {
        $Existing = Get-Content $ChecksumFile | Where-Object { $_ -notmatch [regex]::Escape((Split-Path $PackageZip -Leaf)) }
        @($Existing + $ChecksumLine) | Set-Content -Path $ChecksumFile -Encoding ASCII
    } else {
        $ChecksumLine | Set-Content -Path $ChecksumFile -Encoding ASCII
    }

    Write-Host "Package created:" -ForegroundColor Green
    Write-Host "  $PackageZip"
    Write-Host "Checksum:" -ForegroundColor Green
    Get-Content $ChecksumFile | Write-Host
}
finally {
    Pop-Location
}
