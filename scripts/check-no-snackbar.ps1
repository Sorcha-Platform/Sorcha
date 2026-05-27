#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Snackbar retirement CI gate.
#
# The Sorcha UI is migrating off MudBlazor's Snackbar (toast popups) onto
# two replacement surfaces:
#   - IInlineFeedback / InlineResult.razor — page-scoped, in-place banner
#     for the actor's own action feedback.
#   - The durable inbox (Feature 118) — server-side writers fan workflow
#     events into the bell drawer at MainLayout.razor.
#
# To stop the migration regressing while it's in flight, this gate fails
# the build when any C# / Razor file outside the allowlist (.snackbar-allowlist)
# contains a `Snackbar.Add(` call or an `ISnackbar` injection / parameter.
#
# The allowlist is a ratchet — it may only shrink, never grow. When it
# reaches zero entries the MudSnackbarProvider mount is removed (Phase 7
# of the retirement plan) and this script becomes a permanent guard.
#
# Usage:
#   pwsh scripts/check-no-snackbar.ps1
#
# Exit codes:
#   0 — no violations
#   1 — Snackbar reference found outside the allowlist

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.snackbar-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

# Load the allowlist — strip blank lines and comments. Normalise to forward
# slashes so the comparison works on both Windows and Linux runners.
$allowed = @{}
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    if ($trimmed.StartsWith('#')) { continue }
    $allowed[$trimmed.Replace('\', '/')] = $true
}

# Patterns that indicate a Snackbar dependency. Each is anchored to a
# token boundary so we don't false-positive on type members named
# "SnackbarAddon" or similar.
$patterns = @(
    'Snackbar\.Add\(',          # ISnackbar.Add(...) call
    '\bISnackbar\b',            # field, parameter, or [Inject] declaration
    '@inject\s+ISnackbar\b'     # Razor @inject directive
)

# Files to scan: any .razor or .cs under src/Apps/. The Sorcha.UI.Components.User
# library + the two host apps (Sorcha.UI.Web*, Sorcha.Wallet.Pwa) cover all
# known Snackbar consumers; restricting scope keeps the scan fast and avoids
# false positives in Service / test code that doesn't render UI.
$scanRoots = @(
    "$repo/src/Apps/Sorcha.UI",
    "$repo/src/Apps/Sorcha.Wallet.Pwa"
)

$candidates = @()
foreach ($root in $scanRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $candidates += Get-ChildItem -Path $root -Recurse -Include '*.razor', '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
}

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++

        # Skip comment-only lines so XML docs and inline comments that mention
        # ISnackbar (e.g. "replacement for ISnackbar") don't trigger the gate.
        # The retirement plan explicitly documents the replacement surface;
        # narrative references to the old API in prose aren't actual usage.
        # Covered shapes:
        #   C# // line comment, /// XML doc, /* block, * continuation, */ end
        #   Razor @* block-comment line
        $trimmedForCommentCheck = $line.TrimStart()
        if ($trimmedForCommentCheck -match '^(//|/\*|\*|@\*)') { continue }

        foreach ($p in $patterns) {
            if ($line -match $p) {
                $fileHasMatch = $true
                if (-not $allowed.ContainsKey($rel)) {
                    $violations += [pscustomobject]@{
                        File    = $rel
                        Line    = $lineNum
                        Snippet = $line.Trim()
                    }
                }
                break
            }
        }
    }

    if ($fileHasMatch -and $allowed.ContainsKey($rel)) {
        $matchedAllowed[$rel] = $true
    }
}

# Detect stale allowlist entries — files listed but no longer containing
# any Snackbar reference. These should be removed from the allowlist as
# part of the same PR that cleaned them up.
$stale = @()
foreach ($entry in $allowed.Keys) {
    if (-not $matchedAllowed.ContainsKey($entry)) {
        $stale += $entry
    }
}

$failed = $false

if ($violations.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: Snackbar reference found in files not on the allowlist." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "New code must not depend on ISnackbar." -ForegroundColor Yellow
    Write-Host "Use IInlineFeedback for actor's-own-action feedback, or write a"
    Write-Host "server-side inbox entry for workflow results. See CLAUDE.md."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — files no longer contain any Snackbar reference:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .snackbar-allowlist in the same PR that cleaned them up."
    Write-Host "The allowlist is a ratchet — it may only shrink."
}

if ($failed) {
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: Snackbar gate passed. {0} files still on the allowlist." -f $remaining) -ForegroundColor Green
if ($remaining -eq 0) {
    Write-Host "  Allowlist is empty — Phase 7 (unmount MudSnackbarProvider) is unblocked." -ForegroundColor Green
}
exit 0
