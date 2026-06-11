# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Performs a lightweight structural check on an ARIEC60870 portable release ZIP.
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
        "Start-ARIEC60870.bat",
        "README-PORTABLE.txt",
        "LICENSE",
        "NOTICE",
        "THIRD_PARTY_NOTICES.md",
        "app/ARIEC60870.Desktop.exe",
        "cli/ARIEC60870.Cli.exe",
        "docs/QUICK_START.md",
        "docs/TROUBLESHOOTING.md",
        "docs/VALIDATION_MATRIX.md",
        "samples/mapping-profiles/example-user-mapping.profile.json"
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

    Write-Host "Release package structure OK:" -ForegroundColor Green
    Write-Host "  $ResolvedPackage"
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item $TempRoot -Recurse -Force
    }
}
