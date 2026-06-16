# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Performs a structural check on an ARIEC60870 user release ZIP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"
$ResolvedPackage = (Resolve-Path $PackagePath).Path
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ariec60870-package-check-" + [System.Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null
    Expand-Archive -Path $ResolvedPackage -DestinationPath $TempRoot -Force

    $Required = @(
        "ARIEC60870.exe",
        "README_RELEASE.txt",
        "README.md",
        "CHANGELOG.md",
        "LICENSE",
        "NOTICE",
        "THIRD_PARTY_NOTICES.md",
        "docs/USER_GUIDE.md",
        "docs/QUICK_START.md",
        "docs/TROUBLESHOOTING.md",
        "docs/VALIDATION_MATRIX.md",
        "docs/RELEASE_PACKAGING.md",
        "samples/mapping-profiles/example-user-mapping.profile.json",
        "profiles/utility_fat_iec10x_default_profile.json"
    )

    $Missing = @()
    foreach ($Item in $Required) {
        $Path = Join-Path $TempRoot $Item
        if (-not (Test-Path $Path)) {
            $Missing += $Item
        }
    }

    if ($Missing.Count -gt 0) {
        Write-Error ("Package is missing required files:`n" + ($Missing -join "`n"))
    }

    $BatchFiles = Get-ChildItem $TempRoot -Recurse -File | Where-Object { $_.Extension -ieq ".bat" }
    if ($BatchFiles) {
        Write-Error ("User release package must not include batch launchers:`n" + ($BatchFiles.FullName -join "`n"))
    }

    $ExeFiles = Get-ChildItem $TempRoot -Recurse -File -Filter "*.exe"
    if ($ExeFiles.Count -ne 1 -or $ExeFiles[0].Name -ne "ARIEC60870.exe") {
        Write-Error ("User release package must contain exactly one executable named ARIEC60870.exe. Found:`n" + ($ExeFiles.FullName -join "`n"))
    }

    Write-Host "Release package structure OK:" -ForegroundColor Green
    Write-Host "  $ResolvedPackage"
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item $TempRoot -Recurse -Force
    }
}
