#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Presentation-dialect CI gate (Feature 181 — EUDI / HAIP conformance).
#
# The platform is migrating off the legacy OpenID4VP Presentation Exchange
# dialect onto DCQL, and off the legacy SD-JWT media type onto its successor:
#   - presentation_definition / input_descriptors -> dcql_query (DCQL)
#   - presentation_submission                     -> retired (DCQL responses
#     carry vp_token keyed by credential query id — no submission mapping)
#   - typ "vc+sd-jwt"                             -> "dc+sd-jwt" (writes only;
#     verify-side ACCEPTANCE of the legacy typ stays allowlisted for interop)
#
# To stop the migration regressing while it's in flight, this gate fails the
# build when any .cs / .razor / .json file under src/ outside the allowlist
# (.presentation-dialect-allowlist) contains one of the legacy tokens.
#
# The allowlist is a ratchet — it may only shrink, never grow. When it
# reaches zero entries the migration is complete and this script becomes
# a permanent guard.
#
# Usage:
#   pwsh scripts/check-presentation-dialect.ps1
#
# Exit codes:
#   0 — no violations
#   1 — legacy dialect token found outside the allowlist

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.presentation-dialect-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

# Load the allowlist — strip blank lines, comments, and trailing comments.
# Normalise to forward slashes so the comparison works on both Windows and
# Linux runners.
$allowed = @{}
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    if ($trimmed.StartsWith('#')) { continue }
    # Allow trailing "# which pattern(s)" annotations on entries.
    $hashIdx = $trimmed.IndexOf('#')
    if ($hashIdx -ge 0) { $trimmed = $trimmed.Substring(0, $hashIdx).Trim() }
    if ($trimmed.Length -eq 0) { continue }
    $allowed[$trimmed.Replace('\', '/')] = $true
}

# Legacy dialect tokens. The three Presentation Exchange tokens are banned
# outright once a file leaves the allowlist; the literal "vc+sd-jwt" typ is
# the same pattern class — writes must move to "dc+sd-jwt", verify-side
# acceptance sites stay on the allowlist deliberately.
$patterns = @(
    'presentation_definition',
    'input_descriptors',
    'presentation_submission',
    '"vc\+sd-jwt"'
)

# Files to scan: any .cs / .razor / .json under src/. Excludes build output
# (obj/bin) and worktree checkouts (.worktrees).
$srcRoot = Join-Path $repo 'src'
$candidates = Get-ChildItem -Path $srcRoot -Recurse -Include '*.cs', '*.razor', '*.json' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin|\.worktrees)[\\/]' }

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++

        # Skip comment-only lines so XML docs and inline comments that mention
        # the retired dialect (e.g. "Replaces presentation_submission") don't
        # trigger the gate. The DCQL migration explicitly documents what it
        # replaces; narrative references in prose aren't actual usage.
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
                        Pattern = $p
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

# Detect stale allowlist entries — files listed but no longer containing any
# legacy token. Warn (don't fail): the entry should be removed in the same PR
# that migrated the file, but a stale line must not block unrelated PRs.
$stale = @()
foreach ($entry in $allowed.Keys) {
    if (-not $matchedAllowed.ContainsKey($entry)) {
        $stale += $entry
    }
}

if ($stale.Count -gt 0) {
    Write-Host ""
    Write-Host "WARN: stale allowlist entries — files no longer contain any legacy dialect token:" -ForegroundColor Yellow
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .presentation-dialect-allowlist in the same PR that migrated them."
    Write-Host "The allowlist is a ratchet — it may only shrink."
}

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "FAIL: legacy presentation dialect token found in files not on the allowlist." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  [{2}]  {3}" -f $v.File, $v.Line, $v.Pattern, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "New code must use DCQL (dcql_query) instead of Presentation Exchange" -ForegroundColor Yellow
    Write-Host "(presentation_definition / input_descriptors / presentation_submission),"
    Write-Host "and mint SD-JWTs with typ ""dc+sd-jwt"" instead of ""vc+sd-jwt""."
    Write-Host "See specs/181-eudi-conformance/ and CLAUDE.md."
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: presentation-dialect gate passed. {0} files still on the allowlist." -f $remaining) -ForegroundColor Green
if ($remaining -eq 0) {
    Write-Host "  Allowlist is empty — the DCQL / dc+sd-jwt migration is complete." -ForegroundColor Green
}
exit 0
