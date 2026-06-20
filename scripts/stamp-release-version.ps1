# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Update-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][scriptblock]$Updater
    )

    if (-not (Test-Path $Path)) {
        throw "Required file not found: $Path"
    }

    $text = Get-Content $Path -Raw
    $updated = & $Updater $text
    if ($updated -eq $text) {
        Write-Host "No textual change required: $Path"
    }
    else {
        Set-Content -Path $Path -Value $updated -Encoding UTF8 -NoNewline
        Write-Host "Stamped $Path"
    }
}

Update-TextFile -Path 'Directory.Build.props' -Updater {
    param($text)
    $assemblyVersion = if ($Version -match '^(\d+\.\d+\.\d+)') { "$($Matches[1]).0" } else { "$Version.0" }
    $text = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    $text = [regex]::Replace($text, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>")
    $text = [regex]::Replace($text, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$assemblyVersion</FileVersion>")
    [regex]::Replace($text, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$Version+release</InformationalVersion>")
}

foreach ($html in @('site/index.html', 'docs/index.html')) {
    Update-TextFile -Path $html -Updater {
        param($text)
        $applicationVersion = '<meta name="application-version" content="' + $Version + '" />'
        $softwareVersion = '"softwareVersion":"' + $Version + '"'
        $text = [regex]::Replace($text, '<meta name="application-version" content="[^"]+"\s*/>', $applicationVersion)
        [regex]::Replace($text, '"softwareVersion"\s*:\s*"[^"]+"', $softwareVersion)
    }
}

foreach ($json in @('site/seo-manifest.json', 'docs/seo-manifest.json')) {
    Update-TextFile -Path $json -Updater {
        param($text)
        $versionLine = '"version": "' + $Version + '"'
        [regex]::Replace($text, '"version"\s*:\s*"[^"]+"', $versionLine)
    }
}

Write-Host "Release version metadata is aligned to $Version."
