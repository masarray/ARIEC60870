# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
param(
    [string]$BaseSha = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

# The historic repository has formatting debt in untouched files. A global
# dotnet format --verify-no-changes produced >1,200 unrelated WHITESPACE
# diagnostics on 2026-11-16. Gate *newly changed lines* without wholesale
# reformatting legacy engines or masking new whitespace regressions.
if ($BaseSha -notmatch '^[a-fA-F0-9]{40}$' -or $BaseSha -match '^0{40}$')
{
    Write-Host 'No trustworthy base SHA (manual/initial run). PR/push diff format gate not applicable.'
    return
}

git fetch --no-tags --depth=1 origin $BaseSha
if ($LASTEXITCODE -ne 0) { throw "Could not retrieve formatting comparison base $BaseSha." }

$diff = @(git -c core.quotePath=false diff --no-ext-diff --unified=0 $BaseSha HEAD -- '*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Unable to compare changed C# lines.' }

# Names and added new-file lines are parsed from the exact Git patch. Deleted
# lines never create new formatting violations. No generalized formatting of
# untouched legacy source or generated output is permitted in this gate.
$changed = @{}
$path = ''
$newLine = 0
foreach ($entry in $diff)
{
    $text = [string]$entry
    if ($text.StartsWith('+++ b/', [StringComparison]::Ordinal))
    {
        $path = $text.Substring(6).Replace('\', '/')
        continue
    }
    if ($text.StartsWith('+++ /dev/null', [StringComparison]::Ordinal))
    {
        $path = ''
        continue
    }
    if ($text -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@')
    {
        $newLine = [int]$Matches[1]
        continue
    }
    if ($text.StartsWith('+', [StringComparison]::Ordinal) -and
        !$text.StartsWith('+++', [StringComparison]::Ordinal))
    {
        if ($path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase))
        {
            if (!$changed.ContainsKey($path))
            {
                $changed[$path] = [System.Collections.Generic.HashSet[int]]::new()
            }
            [void]$changed[$path].Add($newLine)
        }
        $newLine++
    }
    elseif ($text.StartsWith(' ', [StringComparison]::Ordinal))
    {
        $newLine++
    }
}

$files = @($changed.Keys | Where-Object { Test-Path -LiteralPath $_ } | Sort-Object)
if ($files.Count -eq 0)
{
    Write-Host 'No added C# source lines; incremental format gate passed.'
    return
}

# Restrict formatter work to touched files for speed. The reported locations
# are then compared against *added lines*, so old pre-existing formatting debt
# in a touched 1,000-line source file does not block a safe isolated edit.
$messages = @(& dotnet format ARIEC60870.sln --verify-no-changes --no-restore --verbosity minimal --include @files 2>&1 |
    ForEach-Object { $_.ToString() })
$exitCode = $LASTEXITCODE
$failures = [System.Collections.Generic.List[string]]::new()
$diagnosticCount = 0
$oldDebt = 0
foreach ($message in $messages)
{
    if ($message -match '^(?<file>.+?\.cs)\((?<line>\d+),(?<column>\d+)\): error (?<code>[^:]+):')
    {
        $diagnosticCount++
        $relative = [IO.Path]::GetRelativePath($root,
            [IO.Path]::GetFullPath($Matches['file'])).Replace('\', '/')
        $lineNumber = [int]$Matches['line']
        if ($changed.ContainsKey($relative) -and $changed[$relative].Contains($lineNumber))
        {
            $failures.Add($message)
        }
        else
        {
            $oldDebt++
        }
    }
    elseif ($message -match '(?i)\berror\b|\bexception\b')
    {
        # Non-diagnostic tooling/config failure is never classified as old debt.
        $failures.Add($message)
    }
}

Write-Host ("Incremental C# format: {0} edited files; {1} formatter diagnostics; {2} outside added lines." -f
    $files.Count, $diagnosticCount, $oldDebt)

if ($exitCode -ne 0 -and $diagnosticCount -eq 0)
{
    $failures.Add("dotnet format exited $exitCode without parseable formatting diagnostics.")
}
if ($failures.Count -gt 0)
{
    foreach ($failure in $failures | Select-Object -First 30) { Write-Error $failure }
    throw "Incremental format gate rejected $($failures.Count) new errors."
}

Write-Host 'Incremental C# formatting gate PASSED. Untouched legacy whitespace debt is not silently treated as new.'
# dotnet format exits nonzero for known legacy violations outside edited lines.
# Explicitly return the validated gate status, not the last native exit code.
exit 0
