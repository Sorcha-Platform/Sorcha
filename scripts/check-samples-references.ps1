#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Platform-vs-consumer boundary gate (F127 / boundary doc 2026-05-15).
#
# Enforces: samples/**/*.csproj may ProjectReference src/Apps/Sorcha.UI/
# only via Sorcha.UI.Components.User. Everything else under
# src/Apps/Sorcha.UI/ is internal-only and must not be referenced by
# sample consumers (they should consume only the published surface).
#
# Exit codes:
#   0  no violations found
#   1  one or more forbidden references found
#   2  no sample csproj files found (fail-fast; if samples/ is gone the
#      gate has lost its referent and should not silently pass)

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

$ErrorActionPreference = "Stop"

$samplesDir = Join-Path $RepoRoot "samples"
if (-not (Test-Path $samplesDir)) {
    Write-Error "samples/ directory not found at $samplesDir"
    exit 2
}

$projects = Get-ChildItem -Path $samplesDir -Filter "*.csproj" -Recurse
if (-not $projects) {
    Write-Error "No *.csproj files found under $samplesDir. Gate has no input — failing fast."
    exit 2
}

# Anything inside src/Apps/Sorcha.UI/ is internal-only EXCEPT this one.
# Matched on the path-segment so we tolerate forward- or backslash paths and
# case-insensitive Windows filesystems.
$allowedAssembly = "Sorcha.UI.Components.User"
$forbiddenPathRoot = "src/Apps/Sorcha.UI/"
$forbiddenPathRootBack = "src\Apps\Sorcha.UI\"

$violations = @()

foreach ($csproj in $projects) {
    $relative = $csproj.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
    $content = Get-Content -LiteralPath $csproj.FullName -Raw

    # Match every <ProjectReference Include="..." />.
    $matches = [regex]::Matches($content, '<ProjectReference\s+Include="([^"]+)"')

    foreach ($m in $matches) {
        $include = $m.Groups[1].Value
        $normalised = $include -replace '\\', '/'

        $touchesUi = $normalised -match [regex]::Escape($forbiddenPathRoot) -or `
                     $include -match [regex]::Escape($forbiddenPathRootBack)

        if ($touchesUi) {
            $isAllowed = ($normalised -match [regex]::Escape("$allowedAssembly.csproj"))
            if (-not $isAllowed) {
                $violations += [pscustomobject]@{
                    Project   = $relative
                    Reference = $include
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: forbidden ProjectReference(s) detected in samples/" -ForegroundColor Red
    Write-Host ""
    Write-Host "Samples may reference Sorcha.UI.Components.User only — anything else under src/Apps/Sorcha.UI/ is internal-only." -ForegroundColor Red
    Write-Host "See samples/README.md and docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  $($v.Project)") -ForegroundColor Red
        Write-Host ("    forbidden reference: $($v.Reference)") -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}

Write-Host "OK — samples/ contains no forbidden references into src/Apps/Sorcha.UI/." -ForegroundColor Green
exit 0
