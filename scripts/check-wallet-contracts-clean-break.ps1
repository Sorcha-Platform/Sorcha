#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Wallet-contracts consolidation CI gate.
#
# The canonical Wallet HTTP DTOs live in exactly one place — Sorcha.Wallet.Contracts.
# Historically they were hand-copied into the Wallet Service, the UI, the CLI, and the
# service clients, and the copies drifted (fields dropped, validation forked). This gate
# stops that regressing: it fails the build when any of the canonical type NAMES is
# re-declared as a class/record outside Sorcha.Wallet.Contracts.
#
# Guarded type names:
#   WalletDto, WalletAddressDto, AddressListResponse,
#   CreateWalletRequest, CreateWalletResponse,
#   SignTransactionRequest, SignTransactionResponse
#
# The allowlist (.wallet-contracts-allowlist) is a ratchet — it may only shrink, never
# grow. It currently carries the isolated Sorcha.Demo sample, whose bespoke shapes are a
# documented follow-up.
#
# Usage:
#   pwsh scripts/check-wallet-contracts-clean-break.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a canonical type re-declared outside Sorcha.Wallet.Contracts (and not allowlisted)

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.wallet-contracts-allowlist'

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

# Canonical type names, matched only where they are DECLARED (class/record), so that
# references, method params, and `using`s never trip the gate.
$typeNames = 'WalletDto|WalletAddressDto|AddressListResponse|CreateWalletRequest|CreateWalletResponse|SignTransactionRequest|SignTransactionResponse'
$declPattern = "\b(class|record)\s+($typeNames)\b"

# The canonical home — declarations here are the source of truth, not violations.
$contractsDir = "$repo/src/Common/Sorcha.Wallet.Contracts".Replace('\', '/')

$candidates = Get-ChildItem -Path "$repo/src", "$repo/tests" -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $full = $file.FullName.Replace('\', '/')
    if ($full.StartsWith($contractsDir)) { continue }

    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNum++
        $trimmedForCommentCheck = $line.TrimStart()
        if ($trimmedForCommentCheck -match '^(//|/\*|\*)') { continue }

        if ($line -match $declPattern) {
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
    Write-Host "FAIL: canonical Wallet DTO re-declared outside Sorcha.Wallet.Contracts." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "These DTOs have a single home: Sorcha.Wallet.Contracts.Models." -ForegroundColor Yellow
    Write-Host "Reference the canonical type instead of re-declaring it. See CLAUDE.md."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — no longer declare a guarded type:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .wallet-contracts-allowlist in the same PR. The allowlist may only shrink."
}

if ($failed) {
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: Wallet-contracts gate passed. {0} file(s) still on the allowlist." -f $remaining) -ForegroundColor Green
if ($remaining -eq 0) {
    Write-Host "  Allowlist is empty — every Wallet DTO now has a single canonical declaration." -ForegroundColor Green
}
exit 0
