# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$SiteRoot = Join-Path $RepoRoot 'site'
$DocsRoot = Join-Path $RepoRoot 'docs'

if (-not (Test-Path (Join-Path $SiteRoot 'index.html'))) {
    throw "Canonical site folder is missing: $SiteRoot"
}

$Preserve = @(
    'archive',
    'profiles',
    'assets',
    'ARCHITECTURE.md',
    'AUTOTEST_ASSESSMENT.md',
    'CLASS1_CLASS2_AUDIT.md',
    'CLEAN_ROOM_POLICY.md',
    'DESKTOP_ARCHITECTURE_CLEANUP.md',
    'DIAGNOSTICS_POLICY.md',
    'EVENT_LOG_POLICY.md',
    'GITHUB_PAGES_DEPLOYMENT.md',
    'GITHUB_REPOSITORY_HYGIENE.md',
    'GITHUB_SECURITY_AUTOMATION.md',
    'GITHUB_SEO.md',
    'IEC101_DUAL_LINK_FAT_CHECKLIST.md',
    'IEC101_DUAL_LINK_REDUNDANCY.md',
    'IEC101_DUAL_LINK_WORKSPACE.md',
    'IEC101_NACK_COMMAND_IOA_AUDIT.md',
    'IEC101_SINGLE_CONNECTION_REFERENCE_STUDY.md',
    'MAPPING_PROFILE_SCHEMA.md',
    'MASTER_POLLING_POLICY.md',
    'NATIVE_PDF_ENGINE.md',
    'OUTPUT_AND_PERFORMANCE_POLICY.md',
    'PRODUCT_BENCHMARK_AND_STRATEGY.md',
    'PRODUCT_REDESIGN_AUDIT.md',
    'PROJECT_STRUCTURE_FINAL.md',
    'PUBLIC_RELEASE_AUDIT.md',
    'QUICK_START.md',
    'REBRANDING_TO_ARIEC60870.md',
    'RELEASE_NOTES.md',
    'RELEASE_PACKAGING.md',
    'RESPONSIVE_LAYOUT_POLICY.md',
    'ROADMAP.md',
    'SLAVE_SIMULATOR_STRATEGY.md',
    'SLAVE_SIMULATOR_USER_GUIDE.md',
    'TESTING_STRATEGY.md',
    'TROUBLESHOOTING.md',
    'USER_GUIDE.md',
    'UTILITY_FAT_PROFILE_SEED.md',
    'VALIDATION_MATRIX.md',
    'README.md'
)

Get-ChildItem -Path $DocsRoot -Force | Where-Object { $Preserve -notcontains $_.Name } | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $SiteRoot '*') -Destination $DocsRoot -Recurse -Force

Set-Content -Path (Join-Path $DocsRoot '.pages-compatibility-mirror') -Value @(
    'This directory contains a generated GitHub Pages compatibility mirror copied from site/.',
    'Keep site/ as the canonical source and refresh this mirror after site changes.'
) -Encoding UTF8

Write-Host 'GitHub Pages /docs compatibility mirror refreshed from site/.'
