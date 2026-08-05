#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Consensus-vote contract CI gate.
#
# VoteDecision has exactly one home: Sorcha.Register.Models (src/Common/Sorcha.Register.Models/
# Consensus/VoteDecision.cs). Consensus votes are persisted to the register, so the enum's numeric
# values are a wire contract shared by the validator, the ledger, and every node that folds a docket.
#
# Why this is gated rather than left to review:
#
#   It was declared TWICE, with INCOMPATIBLE values, in two assemblies that reference each other:
#
#     Sorcha.Validator.Core.Validators.VoteDecision   -> Approve=1, Reject=2, Abstain=3
#     Sorcha.Validator.Service.Models.VoteDecision    -> Reject=0,  Approve=1   (no Abstain)
#
#   Approve happened to agree. Reject did not: 2 versus 0. Both were in scope in the same assembly
#   and were told apart only by namespace qualification (`Models.VoteDecision.Approve`), so nothing
#   in the type system objected. A value crossing between them numerically — a cast, an int
#   round-trip, a serialized payload — turned a Reject into something else, SILENTLY, on the code
#   path that decides whether a docket seals. There is no stack trace for that.
#
# Feature 187 (#1371) consolidated both into the canonical declaration and gave zero to a deliberate
# `Unspecified` sentinel, so that `default(VoteDecision)` can never read as a real Approve or Reject.
#
# Tests are deliberately OUT OF SCOPE: a test asserting the numeric wire value is exactly what pins
# the contract (see VoteDecisionContractTests), so it must be allowed to name the values.
#
# The allowlist (.consensus-vote-contract-allowlist) is a ratchet: it may only SHRINK. Never add an
# entry to make a build pass — fix the declaration instead.

param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$canonical = Join-Path $repoRoot 'src/Common/Sorcha.Register.Models/Consensus/VoteDecision.cs'
$allowlistPath = Join-Path $repoRoot '.consensus-vote-contract-allowlist'

if (-not (Test-Path $canonical)) {
    Write-Host "[FAIL] Canonical VoteDecision declaration is missing: $canonical" -ForegroundColor Red
    Write-Host "       It is the single home for the enum; it must not be moved without updating this gate."
    exit 1
}

$allowlist = @()
if (Test-Path $allowlistPath) {
    $allowlist = Get-Content $allowlistPath |
        Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { $_.Trim() }
}

# Any *declaration* of VoteDecision outside the canonical file is a violation. Usages are fine —
# that is the whole point of having one home.
$pattern = '^\s*(public|internal)\s+enum\s+VoteDecision\b'

$violations = @()
Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    Where-Object { $_.FullName -ne (Resolve-Path $canonical).Path } |
    ForEach-Object {
        $rel = $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        # The gRPC-generated VoteDecision is a separate protocol contract, not a re-declaration of
        # the ledger enum; generated files live under obj/ and are already excluded above.
        if ($allowlist -contains $rel) { return }
        $hit = Select-String -Path $_.FullName -Pattern $pattern -Encoding utf8 -Quiet
        if ($hit) { $violations += $rel }
    }

if ($violations.Count -gt 0) {
    Write-Host "[FAIL] VoteDecision is declared outside its canonical home:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "         $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  VoteDecision has ONE home: src/Common/Sorcha.Register.Models/Consensus/VoteDecision.cs'
    Write-Host '  Its values are a persisted ledger contract. A second declaration is how a Reject'
    Write-Host '  silently became a different value once already — see the header of this script.'
    Write-Host '  Reference the canonical enum instead of re-declaring it.'
    exit 1
}

if (-not $Quiet) {
    Write-Host '[OK] VoteDecision has exactly one declaration (Sorcha.Register.Models).' -ForegroundColor Green
}
exit 0
