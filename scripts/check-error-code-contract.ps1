#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Cross-boundary validation-code CI gate.
#
# Validation codes that cross a service boundary are declared once, in the shared
# Sorcha.Blueprint.Models contracts project:
#
#   Sorcha.Blueprint.Models.ValidationErrorCodes     (VAL_* that another project names)
#   Sorcha.Blueprint.Models.ValidationWarningCodes   (WARN_* that another project names)
#
# This gate fails when any code declared in those two classes is ALSO written as a raw string
# literal anywhere in src/.
#
# Why this is gated:
#
#   These codes are not merely logged — some are matched on. Blueprint Service's
#   RedisPresentationSealCoordinator compares the Validator's error code against "VAL_CHAIN_FORK"
#   to recognise "already sealed via another path" and dedupe silently (Feature 119). Two
#   independently-typed string literals give the compiler nothing to check: rename the producer's
#   literal and the consumer's comparison just stops matching. No build error, no exception, no
#   log line — a duplicate-submission path quietly stops being deduped.
#
# SCOPE — service-internal codes are deliberately NOT covered.
#
#   The Validator's ~70 internal codes (VAL_SCHEMA_*, VAL_STRUCT_*, VAL_PERM_*, …) are declared
#   and consumed inside the same file and carry no cross-boundary drift risk. Hoisting them would
#   be churn without safety. A code earns a place in the shared class — and therefore in this
#   gate — the moment a SECOND project needs to name it. This mirrors the convention already
#   recorded on ValidationWarningCodes.
#
# Usage:
#   pwsh scripts/check-error-code-contract.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a shared code written as a raw literal (and not allowlisted), or a stale allowlist entry

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.error-code-contract-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

$allowed = @{}
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    if ($trimmed.StartsWith('#')) { continue }
    $allowed[$trimmed.Replace('\', '/')] = $true
}

# The canonical homes. Declarations here are the source of truth, not violations.
$canonicalFiles = @(
    "$repo/src/Common/Sorcha.Blueprint.Models/ValidationErrorCodes.cs"
    "$repo/src/Common/Sorcha.Blueprint.Models/ValidationWarningCodes.cs"
) | ForEach-Object { $_.Replace('\', '/') }

foreach ($f in $canonicalFiles) {
    if (-not (Test-Path -LiteralPath $f)) {
        Write-Error "Canonical constants file not found: $f"
        exit 1
    }
}

# Derive the guarded set FROM the canonical files rather than restating it here — a hard-coded
# copy would be its own drift surface, which is the very thing this gate exists to prevent.
$codes = @()
foreach ($f in $canonicalFiles) {
    $text = Get-Content -LiteralPath $f -Raw
    $codes += [regex]::Matches($text, '=\s*"((?:VAL|WARN)_[A-Z0-9_]+)"\s*;') |
        ForEach-Object { $_.Groups[1].Value }
}
$codes = $codes | Sort-Object -Unique

if ($codes.Count -eq 0) {
    Write-Error "No shared validation codes found in the canonical files — the gate would be a no-op."
    exit 1
}

$literalPattern = '"(' + ($codes -join '|') + ')"'

$candidates = Get-ChildItem -Path "$repo/src" -Recurse -Include '*.cs', '*.razor' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $full = $file.FullName.Replace('\', '/')
    if ($canonicalFiles -contains $full) { continue }

    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++

        # Comments are documentation, not emission or matching sites.
        $lead = $line.TrimStart()
        if ($lead -match '^(//|/\*|\*)') { continue }

        if ($line -match $literalPattern) {
            $fileHasMatch = $true
            if (-not $allowed.ContainsKey($rel)) {
                $violations += [pscustomobject]@{
                    File    = $rel
                    Line    = $lineNum
                    Snippet = $line.Trim()
                }
            }
        }
    }

    if ($fileHasMatch -and $allowed.ContainsKey($rel)) {
        $matchedAllowed[$rel] = $true
    }
}

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
    Write-Host "FAIL: shared validation code written as a raw literal." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Reference the shared constant instead:" -ForegroundColor Yellow
    Write-Host "  using Sorcha.Blueprint.Models;"
    Write-Host "  ... ValidationErrorCodes.ChainFork / ValidationWarningCodes.ReviewLayoutUnknown"
    Write-Host ""
    Write-Host "These codes cross a service boundary and some are matched on by string. Two"
    Write-Host "independently-typed literals cannot be checked by the compiler. See CLAUDE.md."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — no longer carry a shared-code literal:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .error-code-contract-allowlist in the same PR. The allowlist may only shrink."
}

if ($failed) {
    exit 1
}

Write-Host ("OK: error-code contract gate passed. {0} shared code(s) guarded, {1} file(s) on the allowlist." -f $codes.Count, $allowed.Count) -ForegroundColor Green
if ($allowed.Count -eq 0) {
    Write-Host "  Allowlist is empty — every shared validation code resolves to a constant." -ForegroundColor Green
}
exit 0
