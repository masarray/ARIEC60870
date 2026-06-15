# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Generates a lightweight SPDX 2.3 JSON dependency SBOM for release artifacts.

.DESCRIPTION
  This script intentionally avoids external tooling so the release workflow can
  always produce a deterministic dependency inventory. It scans project files for
  NuGet PackageReference entries and emits a compact SPDX JSON document.

  The generated file is not a substitute for a deep binary composition scanner,
  but it gives release consumers a machine-readable dependency baseline attached
  to every GitHub Release.
#>
[CmdletBinding()]
param(
    [string]$Version = "3.6.5",
    [string]$OutputPath = "artifacts/release/ARIEC60870-sbom.spdx.json"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$OutputFullPath = Join-Path $RepoRoot $OutputPath
$OutputDirectory = Split-Path $OutputFullPath -Parent
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packageMap = [ordered]@{}
$projectFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\|/bin/|/obj/' }
foreach ($project in $projectFiles) {
    [xml]$xml = Get-Content $project.FullName
    foreach ($reference in $xml.Project.ItemGroup.PackageReference) {
        if ($null -eq $reference) { continue }
        $name = [string]$reference.Include
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $versionValue = [string]$reference.Version
        if ([string]::IsNullOrWhiteSpace($versionValue)) { $versionValue = 'NOASSERTION' }
        $key = "$name@$versionValue"
        if (-not $packageMap.Contains($key)) {
            $packageMap[$key] = [ordered]@{
                name = $name
                SPDXID = 'SPDXRef-Package-' + (($name + '-' + $versionValue) -replace '[^A-Za-z0-9.-]', '-')
                versionInfo = $versionValue
                downloadLocation = "https://www.nuget.org/packages/$name"
                filesAnalyzed = $false
                licenseConcluded = 'NOASSERTION'
                licenseDeclared = 'NOASSERTION'
                copyrightText = 'NOASSERTION'
                externalRefs = @(
                    [ordered]@{
                        referenceCategory = 'PACKAGE-MANAGER'
                        referenceType = 'purl'
                        referenceLocator = "pkg:nuget/$name@$versionValue"
                    }
                )
            }
        }
    }
}

$rootPackage = [ordered]@{
    name = 'ARIEC60870 Protocol Lab'
    SPDXID = 'SPDXRef-Package-ARIEC60870'
    versionInfo = $Version
    downloadLocation = 'https://github.com/masarray/ARIEC60870/releases'
    filesAnalyzed = $false
    licenseConcluded = 'Apache-2.0'
    licenseDeclared = 'Apache-2.0'
    copyrightText = 'Copyright 2026 Ari Sulistiono'
}

$relationships = @()
foreach ($pkg in $packageMap.Values) {
    $relationships += [ordered]@{
        spdxElementId = 'SPDXRef-Package-ARIEC60870'
        relationshipType = 'DEPENDS_ON'
        relatedSpdxElement = $pkg.SPDXID
    }
}

$created = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$document = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "ARIEC60870-$Version-dependency-sbom"
    documentNamespace = "https://github.com/masarray/ARIEC60870/sbom/$Version/" + [guid]::NewGuid().ToString('N')
    creationInfo = [ordered]@{
        created = $created
        creators = @( 'Tool: ARIEC60870 generate-sbom-lite.ps1', 'Organization: MasArray' )
    }
    packages = @($rootPackage) + @($packageMap.Values)
    relationships = $relationships
}

$document | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputFullPath -Encoding UTF8
Write-Host "SBOM created: $OutputFullPath"
Write-Host "NuGet PackageReference count: $($packageMap.Count)"
