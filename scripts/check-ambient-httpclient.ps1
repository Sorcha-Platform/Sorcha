#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Ambient-HttpClient CI gate (issue #1311).
#
# `@inject HttpClient` in a Blazor component binds the AMBIENT client — the
# one the auth service itself uses, so wiring the bearer-token handler into
# it would be circular DI (see Program.cs in each host). It carries NO
# Authorization header. A component that injects it and calls an
# authenticated endpoint gets a silent 401 — often rendered as a convincing
# empty state rather than a visible error. #1165/#1166 (device pairing dead
# for every citizen), #1167 (F181 admin clients silently emptied), and #1310
# (PWA OpenID4VP direct_post) are the motivating incidents for this gate.
#
# This gate is FORWARD-ONLY — it is not a retroactive detector and would not
# have caught any of the three incidents above as they actually occurred:
# #1167 was a typed client registered without its auth handler in a .cs file
# (this gate only scans .razor for ambient injection); #1310's Present.razor
# already injected the ambient HttpClient before the fix and so would simply
# have been seeded onto the allowlist rather than flagged. What this gate
# does do is stop the *next* occurrence of the .razor ambient-injection shape
# from landing unnoticed.
#
# Scope limit: this gate scans `.razor` files for ambient `HttpClient`
# injection only. It does NOT cover a typed client registered in a `.cs` file
# without its auth message handler wired in (the #1167 class of bug) — that
# needs a different check (e.g. a source/DI-registration audit), not this one.
#
# To stop new ambient-client sites appearing unnoticed, this gate fails the
# build when any .razor file outside the allowlist (.ambient-httpclient-allowlist)
# injects the bare HttpClient type.
#
# The allowlist is a ratchet — it may only shrink, never grow. Each entry is
# either unaudited (follow-up work) or annotated as a deliberately anonymous
# call site. See scripts/check-no-snackbar.ps1 for the sibling gate this one
# is modelled on.
#
# Usage:
#   pwsh scripts/check-ambient-httpclient.ps1
#
# Exit codes:
#   0 — no violations
#   1 — ambient HttpClient injection found outside the allowlist

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.ambient-httpclient-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

# Load the allowlist — strip blank lines, comment-only lines, and trailing
# `# ...` annotations. Normalise to forward slashes so the comparison works
# on both Windows and Linux runners.
$allowed = @{}
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    if ($trimmed.StartsWith('#')) { continue }

    $hashIndex = $trimmed.IndexOf('#')
    if ($hashIndex -ge 0) { $trimmed = $trimmed.Substring(0, $hashIndex).Trim() }
    if ($trimmed.Length -eq 0) { continue }

    $allowed[$trimmed.Replace('\', '/')] = $true
}

# Ambient injection of the bare HttpClient type. A typed client is a
# different, named type (e.g. `@inject IDeviceBindingService Device`), so
# anchoring on the literal `HttpClient` token doesn't false-positive on those.
$patterns = @(
    '@inject\s+HttpClient\b',                    # Razor @inject directive
    '\[Inject\][^\r\n]*\bHttpClient\b\s+\w+\s*{\s*get'  # [Inject] property (code-behind style)
)

# Files to scan: any .razor under src/Apps/.
$scanRoot = "$repo/src/Apps"

$candidates = @()
if (Test-Path -LiteralPath $scanRoot) {
    $candidates = Get-ChildItem -Path $scanRoot -Recurse -Include '*.razor' -File -ErrorAction SilentlyContinue |
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

        # Skip comment-only lines (Razor @* block comments) so narrative
        # references don't trigger the gate.
        $trimmedForCommentCheck = $line.TrimStart()
        if ($trimmedForCommentCheck -match '^(//|@\*|\*)') { continue }

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

# Detect stale allowlist entries — files listed but no longer injecting the
# ambient HttpClient. These should be removed from the allowlist as part of
# the same PR that cleaned them up.
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
    Write-Host "FAIL: ambient HttpClient injection found in files not on the allowlist." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "The ambient HttpClient carries NO Authorization header — a call to an" -ForegroundColor Yellow
    Write-Host "authenticated endpoint through it silently 401s (#1165/#1166, #1167, #1310)."
    Write-Host "Use a typed client wired with BearerTokenHandler / the equivalent auth"
    Write-Host "message handler instead. See CLAUDE.md and issue #1311."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — files no longer injecting the ambient HttpClient:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .ambient-httpclient-allowlist in the same PR that cleaned them up."
    Write-Host "The allowlist is a ratchet — it may only shrink."
}

if ($failed) {
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: ambient-HttpClient gate passed. {0} files still on the allowlist." -f $remaining) -ForegroundColor Green
exit 0
