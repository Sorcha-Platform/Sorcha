#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Derivation-context CI gate.
#
# Every Sorcha key-derivation context string ("sorcha:docket-signing", "sorcha:register-control", …)
# has exactly one home: Sorcha.Wallet.Contracts.Constants.SorchaDerivationPaths. That assembly is a
# zero-dependency leaf, so every consumer can reference it — services, CLI, Blazor UI, and the WASM
# wallet PWA alike.
#
# Why this is gated rather than left to review:
#
#   A mistyped derivation context DOES NOT THROW. It derives a different — but perfectly valid — key.
#   The failure surfaces far from the typo and silently: a wrong "sorcha:docket-signing" yields a
#   validator whose signing key no longer matches its own roster entry, RegisterMonitoringBootstrap
#   never enrols the register, and dockets simply stop sealing. There is no stack trace to follow.
#
# Historically the constants lived in Sorcha.Wallet.Portable, which depends on Sorcha.Cryptography
# and therefore P/Invokes libsodium — unloadable under browser-wasm. Services that could not take
# that dependency hand-copied the literals instead, and a second constants class
# (Sorcha.CitizenWallet.Abstractions.Constants.DerivationContexts) was hand-mirrored for the PWA.
# Moving the constants to the leaf assembly removed the reason to copy; this gate removes the option.
#
# The gate ignores comments — illustrative prose such as
#   /// <param name="derivationPath">… a Sorcha system path like "sorcha:register-attestation"</param>
# is documentation, not a call site, and carries no drift risk.
#
# Tests are deliberately OUT OF SCOPE: a test asserting the literal wire value is exactly what pins
# the constant, so `SorchaDerivationPaths.DocketSigning.Should().Be("sorcha:docket-signing")` must
# keep saying the quoted string.
#
# Usage:
#   pwsh scripts/check-derivation-contexts.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a raw derivation-context literal outside the canonical constants file (and not allowlisted),
#       or a stale allowlist entry

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.derivation-contexts-allowlist'

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

# The canonical home — declarations here are the source of truth, not violations.
$canonicalFile = "$repo/src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs".Replace('\', '/')

if (-not (Test-Path -LiteralPath $canonicalFile)) {
    Write-Error "Canonical constants file not found: $canonicalFile"
    exit 1
}

# Derive the context list FROM the canonical file rather than restating it here. A hard-coded copy
# would be its own drift surface: adding a constant to SorchaDerivationPaths without remembering to
# add it here would silently drop it out of the gate — the exact failure mode this script exists to
# prevent.
$canonicalText = Get-Content -LiteralPath $canonicalFile -Raw
$contexts = [regex]::Matches($canonicalText, '=\s*"sorcha:([a-z0-9\-]+)"\s*;') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

if ($contexts.Count -eq 0) {
    Write-Error "No 'sorcha:*' constants found in $canonicalFile — the gate would be a no-op."
    exit 1
}

$literalPattern = '"sorcha:(' + ($contexts -join '|') + ')"'

$candidates = Get-ChildItem -Path "$repo/src" -Recurse -Include '*.cs', '*.razor' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $full = $file.FullName.Replace('\', '/')
    if ($full -eq $canonicalFile) { continue }

    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++

        # Comments are documentation, not call sites.
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
    Write-Host "FAIL: raw derivation-context literal outside SorchaDerivationPaths." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Use the constant instead — e.g. SorchaDerivationPaths.DocketSigning." -ForegroundColor Yellow
    Write-Host "  using Sorcha.Wallet.Contracts.Constants;"
    Write-Host ""
    Write-Host "A mistyped context does not throw; it derives a DIFFERENT valid key, and dockets"
    Write-Host "silently stop sealing. See CLAUDE.md 'Derivation contexts'."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — no longer carry a raw context literal:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .derivation-contexts-allowlist in the same PR. The allowlist may only shrink."
}

if ($failed) {
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: derivation-context gate passed. {0} file(s) on the allowlist." -f $remaining) -ForegroundColor Green
if ($remaining -eq 0) {
    Write-Host "  Allowlist is empty — every derivation context resolves to SorchaDerivationPaths." -ForegroundColor Green
}
exit 0
