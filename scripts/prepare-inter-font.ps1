# Copyright 2026 Ari Sulistiono
# SPDX-License-Identifier: Apache-2.0
# Official Inter font binary is acquired from its upstream SIL-OFL source at build time.
# Never rely on an operator-installed Windows font, and fail closed on source drift.
$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '../src/ARIEC60870.Desktop/Assets/Fonts/Inter.ttf'
$expectedBlob = '047c92f6e2212473dc436020afed689527076d44'
function Get-GitBlobSha([string] $path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $prefix = [System.Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)" + [char]0)
    $buffer = [byte[]]::new($prefix.Length + $bytes.Length)
    [System.Buffer]::BlockCopy($prefix, 0, $buffer, 0, $prefix.Length)
    [System.Buffer]::BlockCopy($bytes, 0, $buffer, $prefix.Length, $bytes.Length)
    # Windows PowerShell 5.1 uses .NET Framework, which has neither SHA1.HashData
    # nor Convert.ToHexString. Keep the identical pinned Git-blob SHA-1 check
    # on both Windows PowerShell 5.1 and PowerShell 7 / .NET 8.
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $digest = $sha1.ComputeHash($buffer)
        return [System.BitConverter]::ToString($digest).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha1.Dispose()
    }
}
if (Test-Path $target) {
    if ((Get-GitBlobSha $target) -eq $expectedBlob) { return }
    throw 'Embedded Inter.ttf differs from the pinned upstream Git blob. Do not ship an unverified font.'
}
$dir = Split-Path $target -Parent
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$tmp = "$target.download"
try {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/google/fonts/main/ofl/inter/Inter%5Bopsz%2Cwght%5D.ttf' -OutFile $tmp -MaximumRedirection 5
    if ((Get-GitBlobSha $tmp) -ne $expectedBlob) {
        throw 'Downloaded Inter font hash mismatch; the pinned upstream font version changed.'
    }
    Move-Item $tmp $target
} finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
}
