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

$RuntimeFiles = @(
    '.nojekyll',
    '404.html',
    'download.html',
    'faq.html',
    'googlec34c43149eef6100.html',
    'humans.txt',
    'index.html',
    'llms.txt',
    'protocol-coverage.html',
    'quick-start.html',
    'robots.txt',
    'script.js',
    'seo-manifest.json',
    'site.webmanifest',
    'sitemap.xml',
    'styles.css',
    'troubleshooting.html'
)

foreach ($File in $RuntimeFiles) {
    $Source = Join-Path $SiteRoot $File
    if (Test-Path $Source) {
        Copy-Item -Path $Source -Destination (Join-Path $DocsRoot $File) -Force
    }
}

$DocsAssets = Join-Path $DocsRoot 'assets'
if (Test-Path $DocsAssets) {
    Remove-Item -Path $DocsAssets -Recurse -Force
}
Copy-Item -Path (Join-Path $SiteRoot 'assets') -Destination $DocsAssets -Recurse -Force

Set-Content -Path (Join-Path $DocsRoot '.pages-compatibility-mirror') -Value @(
    'This directory contains a generated GitHub Pages compatibility mirror copied from site/.',
    'Keep site/ as the canonical source and refresh this mirror after site changes.'
) -Encoding UTF8

Write-Host 'GitHub Pages /docs compatibility mirror refreshed from site/.'
