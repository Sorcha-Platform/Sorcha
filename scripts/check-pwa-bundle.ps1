#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Feature 122 bundle-hygiene check.
#
# The Sorcha.Citizen.Wallet PWA must not bundle admin / designer
# assemblies. This script publishes the PWA, inspects the produced
# wwwroot/_framework/ assembly set, and fails CI when forbidden
# assemblies are present.
#
# Usage:
#   pwsh scripts/check-pwa-bundle.ps1
#   pwsh scripts/check-pwa-bundle.ps1 -PublishDir custom/path  # skip publish
#
# Exit codes:
#   0 — bundle is clean
#   1 — forbidden assembly present, OR required assembly missing

param(
    [string]$PublishDir = '',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pwaCsproj = Join-Path $repoRoot 'src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj'

if (-not $PublishDir) {
    $PublishDir = Join-Path $repoRoot 'src/Apps/Sorcha.Citizen.Wallet/bin/Release/net10.0/publish'
}

if (-not $SkipPublish) {
    Write-Host "[i] Publishing Sorcha.Citizen.Wallet (Release)..."
    & dotnet publish $pwaCsproj -c Release -o $PublishDir --nologo /p:CompressionEnabled=false | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
        exit 1
    }
}

$frameworkDir = Join-Path $PublishDir 'wwwroot/_framework'
if (-not (Test-Path $frameworkDir)) {
    Write-Error "Framework dir not found: $frameworkDir"
    exit 1
}

# Patterns we forbid in the PWA bundle.
$forbidden = @(
    'Z.Blazor.Diagrams',
    'Blazor.Diagrams.Core',
    'YamlDotNet',
    'Sorcha.UI.Core'
)

# Patterns we require to be present (sanity check that the new
# shared library actually lands in the bundle).
$required = @(
    'Sorcha.UI.Components.User'
)

$assemblies = Get-ChildItem -Path $frameworkDir -Filter '*.wasm' -ErrorAction SilentlyContinue
if (-not $assemblies) {
    # Newer SDKs sometimes still emit .dll under _framework; tolerate both.
    $assemblies = Get-ChildItem -Path $frameworkDir -Filter '*.dll' -ErrorAction SilentlyContinue
}
if (-not $assemblies) {
    Write-Error "No assemblies found under $frameworkDir"
    exit 1
}

$names = $assemblies | ForEach-Object { $_.BaseName }
$failures = @()

foreach ($pattern in $forbidden) {
    $hits = $names | Where-Object { $_ -like "*$pattern*" }
    if ($hits) {
        $failures += "FORBIDDEN: $pattern present as: $($hits -join ', ')"
    }
}

foreach ($pattern in $required) {
    $hits = $names | Where-Object { $_ -like "*$pattern*" }
    if (-not $hits) {
        $failures += "MISSING: required $pattern not present"
    }
}

if ($failures) {
    Write-Host ""
    Write-Host "[X] PWA bundle hygiene check FAILED:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Inspected $($assemblies.Count) assemblies under $frameworkDir"
    exit 1
}

Write-Host "[OK] PWA bundle hygiene check PASSED" -ForegroundColor Green
Write-Host "  Inspected $($assemblies.Count) assemblies under $frameworkDir"
Write-Host "  Forbidden patterns absent: $($forbidden -join ', ')"
Write-Host "  Required patterns present: $($required -join ', ')"
exit 0
