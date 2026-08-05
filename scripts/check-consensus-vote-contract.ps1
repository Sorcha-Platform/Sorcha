#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Consensus-vote contract CI gate.
#
# The consensus ledger types have exactly one home: Sorcha.Register.Models (under Consensus/).
# Consensus votes are persisted to the register, so their shape and VoteDecision's numeric values
# are a wire contract shared by the validator, the ledger, and every node that folds a docket.
#
# Guarded types: VoteDecision, ConsensusVote, RegisterSignature.
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
$canonicalDir = Join-Path $repoRoot 'src/Common/Sorcha.Register.Models/Consensus'
$guarded = @{
    'VoteDecision'      = 'enum'
    'ConsensusVote'     = 'class'
    'RegisterSignature' = 'class'
}
$allowlistPath = Join-Path $repoRoot '.consensus-vote-contract-allowlist'

foreach ($name in $guarded.Keys) {
    $file = Join-Path $canonicalDir "$name.cs"
    if (-not (Test-Path $file)) {
        Write-Host "[FAIL] Canonical $name declaration is missing: $file" -ForegroundColor Red
        Write-Host "       It is the single home for the type; it must not be moved without updating this gate."
        exit 1
    }
}

$allowlist = @()
if (Test-Path $allowlistPath) {
    $allowlist = Get-Content $allowlistPath |
        Where-Object { $_ -and -not $_.StartsWith('#') } |
        ForEach-Object { $_.Trim() }
}

# Any *declaration* of a guarded type outside its canonical file is a violation. Usages are fine —
# that is the whole point of having one home. Both `class`/`enum` and `record` forms count, so a
# re-declaration cannot slip through by changing the kind.
$canonicalPaths = @{}
foreach ($name in $guarded.Keys) {
    $canonicalPaths[$name] = (Resolve-Path (Join-Path $canonicalDir "$name.cs")).Path
}

$violations = @()
Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    ForEach-Object {
        $file = $_
        $rel = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        # The gRPC-generated ConsensusVote / VoteDecision are a separate protocol contract, not a
        # re-declaration of the ledger types; generated files live under obj/ and are excluded above.
        if ($allowlist -contains $rel) { return }
        foreach ($name in $guarded.Keys) {
            if ($file.FullName -eq $canonicalPaths[$name]) { continue }
            $kind = $guarded[$name]
            $pattern = "^\s*(public|internal)\s+(sealed\s+)?($kind|record)\s+$name\b"
            if (Select-String -Path $file.FullName -Pattern $pattern -Encoding utf8 -Quiet) {
                $violations += "$rel  (declares $name)"
            }
        }
    }
$violations = @($violations | Sort-Object -Unique)

if ($violations.Count -gt 0) {
    Write-Host "[FAIL] A consensus ledger type is declared outside its canonical home:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "         $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  VoteDecision, ConsensusVote and RegisterSignature each have ONE home, under'
    Write-Host '  src/Common/Sorcha.Register.Models/Consensus/. They are persisted ledger contracts.'
    Write-Host '  A second declaration is how a Reject silently became a different value once'
    Write-Host '  already — see the header of this script. Reference the canonical type instead.'
    exit 1
}

if (-not $Quiet) {
    Write-Host '[OK] VoteDecision, ConsensusVote and RegisterSignature each have exactly one declaration (Sorcha.Register.Models).' -ForegroundColor Green
}
exit 0
