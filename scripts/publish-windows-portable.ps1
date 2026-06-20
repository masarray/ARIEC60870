# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Builds the user-facing Windows x64 single-file EXE release package for ARIEC60870.

.DESCRIPTION
  Creates one public Windows ZIP for normal users:

    ARIEC60870-vX.Y.Z-win-x64.zip

  The package contains ARIEC60870.exe at the package root, short first-run
  instructions, documentation, neutral sample profiles, and license files. It
  intentionally does not create start batch files or multiple executable choices.

  By default the script runs the protocol smoke test and the full xUnit
  regression suite before publishing. Use -SkipTests only for local packaging
  experiments after CI has already passed.

.EXAMPLE
  pwsh ./scripts/publish-windows-portable.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "3.6.7",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ArtifactRoot = Join-Path $RepoRoot "artifacts"
$ReleaseRoot = Join-Path $ArtifactRoot "release"
$TestResultsRoot = Join-Path $ArtifactRoot "test-results"
$StagingRoot = Join-Path $ReleaseRoot "ARIEC60870-v$Version-$Runtime"
$PublishOut = Join-Path $ArtifactRoot "publish-desktop-$Runtime"
$PackageZip = Join-Path $ReleaseRoot "ARIEC60870-v$Version-$Runtime.zip"
$ChecksumFile = Join-Path $ReleaseRoot "SHA256SUMS.txt"
$SelfContained = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "ARIEC60870 Windows single-file release packaging" -ForegroundColor Cyan
Write-Host "Repository      : $RepoRoot"
Write-Host "Version         : $Version"
Write-Host "Runtime         : $Runtime"
Write-Host "Configuration   : $Configuration"
Write-Host "Self-contained  : $SelfContained"
Write-Host "User package    : single desktop EXE, no start batch files"

Push-Location $RepoRoot
try {
    New-Item -ItemType Directory -Force -Path $ReleaseRoot, $TestResultsRoot | Out-Null
    foreach ($Path in @($StagingRoot, $PublishOut)) {
        if (Test-Path $Path) { Remove-Item $Path -Recurse -Force }
    }
    if (Test-Path $PackageZip) { Remove-Item $PackageZip -Force }

    New-Item -ItemType Directory -Force -Path $StagingRoot, $PublishOut | Out-Null

    dotnet restore ARIEC60870.sln
    dotnet build ARIEC60870.sln --configuration $Configuration --no-restore -p:Version=$Version -p:TreatWarningsAsErrors=true

    if (-not $SkipTests) {
        Write-Host "Running protocol smoke test..." -ForegroundColor Cyan
        dotnet run --project tests/ARIEC60870.Protocol.Tests/ARIEC60870.Protocol.Tests.csproj --configuration $Configuration --no-build

        Write-Host "Running full xUnit regression suite..." -ForegroundColor Cyan
        dotnet test ARIEC60870.sln --configuration $Configuration --no-build --logger "trx;LogFileName=release-package.trx" --results-directory $TestResultsRoot
    }

    dotnet publish src/ARIEC60870.Desktop/ARIEC60870.Desktop.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained:$SelfContained `
        -p:Version=$Version `
        -p:TreatWarningsAsErrors=true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $PublishOut

    $PublishedExe = Join-Path $PublishOut "ARIEC60870.Desktop.exe"
    if (-not (Test-Path $PublishedExe)) {
        Write-Error "Published desktop executable was not found: $PublishedExe"
    }

    Copy-Item $PublishedExe -Destination (Join-Path $StagingRoot "ARIEC60870.exe") -Force

    $DocsOut = Join-Path $StagingRoot "docs"
    $SamplesOut = Join-Path $StagingRoot "samples"
    $ProfilesOut = Join-Path $StagingRoot "profiles"
    New-Item -ItemType Directory -Force -Path $DocsOut, $SamplesOut, $ProfilesOut | Out-Null

    Copy-Item LICENSE, NOTICE, THIRD_PARTY_NOTICES.md, README.md, CHANGELOG.md -Destination $StagingRoot -Force
    Copy-Item docs/USER_GUIDE.md, docs/QUICK_START.md, docs/TROUBLESHOOTING.md, docs/VALIDATION_MATRIX.md, docs/RELEASE_PACKAGING.md -Destination $DocsOut -Force
    Copy-Item samples/mapping-profiles -Destination $SamplesOut -Recurse -Force
    if (Test-Path profiles) {
        Copy-Item profiles/* -Destination $ProfilesOut -Recurse -Force
    }

    $ReleaseReadme = @"
ARIEC60870 v$Version Windows x64 release

Start the application:
  Double-click ARIEC60870.exe

First run:
  1. Open Setup.
  2. Select IEC-101 serial, IEC-103 serial, or IEC-104 TCP/IP.
  3. Enter the approved project/device communication settings.
  4. Start the session.
  5. Review Operator Evidence, Value Viewer, Event Log, Frame Trace, Diagnostics, and Report.
  6. Use Report > Export PDF when the evidence is ready.

Included folders:
  docs\      User guide, quick start, troubleshooting, validation matrix, packaging notes
  samples\   Neutral example user mapping profile
  profiles\  Neutral IEC-101/104 example point profile

Before sharing exported reports:
  Review project names, endpoint settings, mapping labels, and protocol evidence.
"@
    Set-Content -Path (Join-Path $StagingRoot "README_RELEASE.txt") -Value $ReleaseReadme -Encoding UTF8

    $Unexpected = Get-ChildItem $StagingRoot -Recurse -File | Where-Object { $_.Extension -ieq ".bat" }
    if ($Unexpected) {
        Write-Error ("Batch launchers are not allowed in the user release package:`n" + ($Unexpected.FullName -join "`n"))
    }

    $ExeFiles = Get-ChildItem $StagingRoot -Recurse -File -Filter "*.exe"
    if ($ExeFiles.Count -ne 1 -or $ExeFiles[0].Name -ne "ARIEC60870.exe") {
        Write-Error ("The user release package must contain exactly one executable named ARIEC60870.exe. Found:`n" + ($ExeFiles.FullName -join "`n"))
    }

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
