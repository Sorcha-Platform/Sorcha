#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# `.Produces<object>` RATCHET.
#
# An endpoint declaring `.Produces<object>` names no response type, and a named type is the only
# thing a static check can compare a client DTO against. While one endpoint is untyped, NOTHING in
# this repo can verify that the DTO binding it agrees with what it sends - not OpenAPI, not
# scripts/check-mcp-routes.ps1 (which proves only that the URL is mapped), and not
# scripts/check-mcp-response-shapes.ps1, whose entire job this is.
#
# WHY THIS IS GATED
#
#   GET /api/registers/ declared `.Produces<object>` and returned an inline lambda's
#   Results.Ok(registers). RegisterSummaryInfo binds that body and had drifted from it in two ways
#   at once - Status typed `string` against an enum the Register Service writes as the integer 1,
#   and a TenantId the server has never sent at all. Both were silent: the first threw and the
#   client catch-all returned an empty list, the second bound nothing. Two consumers reported
#   "0 registers" against a node holding five, for weeks, with every gate green (#1613).
#
#   Typing that one endpoint is what made the defect visible to a gate. Proven, not assumed: with
#   .Produces<IEnumerable<Register>> the shape gate FAILS naming RegisterSummaryInfo.Status; revert
#   it to .Produces<object> with the same defect present and the gate goes blind and PASSES.
#
#   So this list is not a style preference. Every line on it is an endpoint whose response shape no
#   check in this repo can see.
#
# HOW TO REMOVE A LINE (the only correct direction)
#
#   Declare what the handler already returns. It is metadata only - no behaviour change, no wire
#   change, and the handler itself needs no edit:
#
#       .Produces<object>(StatusCodes.Status200OK)
#    -> .Produces<IEnumerable<Sorcha.Register.Models.Register>>(StatusCodes.Status200OK)
#
#   Then lower this file's count in the same PR. A count that no longer matches FAILS in BOTH
#   directions: too many is a regression, too few is a stale entry that must be tightened. That is
#   what makes it a ratchet rather than a list of excuses.
#
# Usage:
#   pwsh scripts/check-produces-object.ps1            # gate
#   pwsh scripts/check-produces-object.ps1 -Report    # print current counts (to seed/update entries)
#
# Exit codes:
#   0 - every file's count matches its allowlisted count, and no un-allowlisted file has any
#   1 - a new or increased use, a stale (too-high) entry, or an extraction floor breach

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch]$Report
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.produces-object-allowlist'
$servicesRoot = Join-Path $repo 'src/Services'

if (-not (Test-Path -LiteralPath $servicesRoot)) {
    Write-Error "Required source tree not found: $servicesRoot"
    exit 1
}

# Non-vacuity floor. A broken scanner must FAIL, never read as a clean gate - this repo has been
# bitten by exactly that. Lower it only when the real count genuinely falls below it, which is the
# day this gate has nearly finished its job.
$MinTotalScanned = 20

# .Produces<object> and .Produces<object?>, with or without a status argument.
$pattern = '\.\s*Produces\s*<\s*object\s*\??\s*>'

$counts = @{}
$total = 0

$files = Get-ChildItem -Path $servicesRoot -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    Sort-Object FullName

foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    if ([string]::IsNullOrEmpty($text)) { continue }

    # Whole-line comments dropped so a commented-out example cannot inflate a count.
    $lines = $text -split "`n"
    $kept = foreach ($line in $lines) { if ($line -match '^\s*//') { '' } else { $line } }
    $clean = $kept -join "`n"

    $n = @([regex]::Matches($clean, $pattern)).Count
    if ($n -eq 0) { continue }

    $rel = $f.FullName.Replace('\', '/').Substring($repo.Replace('\', '/').Length).TrimStart('/')
    $counts[$rel] = $n
    $total += $n
}

if ($Report) {
    Write-Host "Current .Produces<object> counts ($total across $($counts.Count) file(s)):" -ForegroundColor Cyan
    foreach ($k in ($counts.Keys | Sort-Object)) { Write-Host ("{0}|{1}" -f $k, $counts[$k]) }
    exit 0
}

if ($total -lt $MinTotalScanned) {
    Write-Host "FAIL: only $total .Produces<object> occurrence(s) found (floor $MinTotalScanned)." -ForegroundColor Red
    Write-Host "Either the scanner has stopped working - in which case it reports nothing and reads as a" -ForegroundColor Red
    Write-Host "pass - or the real count has genuinely fallen this far and the floor should be lowered" -ForegroundColor Red
    Write-Host "deliberately in the same PR. Do not lower it to make a build pass." -ForegroundColor Red
    exit 1
}

$allowed = @{}
if (Test-Path -LiteralPath $allowlistPath) {
    foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
        $trimmed = ($line -split '#')[0].Trim()
        if ($trimmed.Length -eq 0) { continue }
        $parts = $trimmed -split '\|'
        if ($parts.Count -ne 2) {
            Write-Host "FAIL: malformed allowlist line (expected '<path>|<count>'): $trimmed" -ForegroundColor Red
            exit 1
        }
        $allowed[$parts[0].Trim()] = [int]$parts[1].Trim()
    }
}
else {
    Write-Host "WARN: allowlist not found at $allowlistPath - treating as empty." -ForegroundColor Yellow
}

$newFiles = @()
$increased = @()
$stale = @()

foreach ($k in ($counts.Keys | Sort-Object)) {
    if (-not $allowed.ContainsKey($k)) { $newFiles += "$k|$($counts[$k])"; continue }
    if ($counts[$k] -gt $allowed[$k]) {
        $increased += "$k : allowlisted $($allowed[$k]), found $($counts[$k])"
    }
    elseif ($counts[$k] -lt $allowed[$k]) {
        $stale += "$k : allowlisted $($allowed[$k]), found $($counts[$k]) - lower it to $($counts[$k])"
    }
}

foreach ($k in ($allowed.Keys | Sort-Object)) {
    if (-not $counts.ContainsKey($k)) { $stale += "$k : allowlisted $($allowed[$k]), found 0 - remove this line" }
}

$failed = $false

if ($newFiles.Count -gt 0 -or $increased.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: new or increased use of .Produces<object> - the response shape becomes invisible." -ForegroundColor Red
    Write-Host ""
    foreach ($n in $newFiles) { Write-Host "  NEW FILE       $n" -ForegroundColor Yellow }
    foreach ($i in $increased) { Write-Host "  INCREASED      $i" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Declare the type the handler already returns - it is metadata only, and it is what lets"
    Write-Host "scripts/check-mcp-response-shapes.ps1 compare a client DTO against the body it binds."
    Write-Host "An untyped endpoint is how #1613 stayed invisible while two consumers reported 0 registers"
    Write-Host "against a node holding five."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries - these files now have FEWER than allowlisted:" -ForegroundColor Red
    foreach ($s in $stale) { Write-Host "  - $s" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Tighten .produces-object-allowlist in the same PR. The ratchet may only shrink, and a"
    Write-Host "count left too high silently buys back room for a regression."
}

if ($failed) { exit 1 }

Write-Host ("OK: produces-object ratchet held. {0} .Produces<object> occurrence(s) across {1} file(s), all at or below their allowlisted counts." -f $total, $counts.Count) -ForegroundColor Green
Write-Host "  Each one is an endpoint whose response shape no check in this repo can see. The list may only shrink." -ForegroundColor Green
exit 0
