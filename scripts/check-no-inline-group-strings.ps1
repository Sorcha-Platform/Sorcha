#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Phase 7 / US5 (Feature 118) — CI grep gate enforcing the
# "construct hub group strings only via *HubGroups builders" rule.
# Spec FR-013, FR-014.
#
# Scans the SignalR-relevant C# surface for inline `$"wallet:{`, `$"register:{`,
# `$"user:{`, `$"org:{`, `$"instance:{`, `$"system:{` interpolations and fails
# the build if any are found OUTSIDE the allowed locations:
#   - *HubGroups.cs (the builder definitions themselves)
#   - tests/** (test fixtures and assertions)
#
# Files in scope: any C# file under src/Services/*/Hubs/, src/Services/*/NotificationService*.cs,
# src/Services/*/*Bridge*.cs. Restricting scope avoids false positives in Redis-key prefix
# constants and other data-shape strings that happen to share a prefix.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)

# Files to scan — any .cs under these globs, excluding obj/bin/Migrations.
$scanGlobs = @(
    "$repo/src/Services/*/Hubs/*.cs",
    "$repo/src/Services/*/Services/Implementation/Notification*.cs",
    "$repo/src/Services/*/Services/Implementation/*Bridge*.cs",
    "$repo/src/Services/*/Services/RegisterEventBridge*.cs",
    "$repo/src/Services/*/Services/Implementation/*HubBridge*.cs"
)

$candidates = @()
foreach ($glob in $scanGlobs) {
    $candidates += Get-ChildItem -Path $glob -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '\\(obj|bin|Migrations)\\' -and
        $_.Name -notlike '*HubGroups.cs'
    }
}

# Patterns that indicate an inline SignalR group string. Each is anchored to the `$"` prefix.
$patterns = @(
    '\$"wallet:\{',
    '\$"register:\{',
    '\$"user:\{',
    '\$"org:\{',
    '\$"instance:\{',
    '\$"system:\{'
)

$violations = @()
foreach ($file in $candidates) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName)
    $lineNum = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++
        # Skip lines that look like log templates (no `$` interpolation prefix; just placeholder text).
        if ($line -match '_logger\.Log' -and $line -notmatch '\$"') { continue }
        foreach ($p in $patterns) {
            if ($line -match $p) {
                $violations += [pscustomobject]@{
                    File    = $rel
                    Line    = $lineNum
                    Snippet = $line.Trim()
                }
            }
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host "OK: zero inline group-string literals found in production code." -ForegroundColor Green
    exit 0
}

Write-Host "FAIL: inline SignalR group-string literals detected. Use the matching *HubGroups builder instead." -ForegroundColor Red
Write-Host ""
foreach ($v in $violations) {
    Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Allowed locations:"
Write-Host "  - *HubGroups.cs (builder definitions)"
Write-Host "  - tests/** (test fixtures)"
Write-Host ""
Write-Host "Spec: specs/118-notifications-architecture/spec.md FR-013, FR-014."
exit 1
