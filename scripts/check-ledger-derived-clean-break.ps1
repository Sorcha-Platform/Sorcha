# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Clean-break gate for Feature 145 (Ledger-Derived Workflow Instances).
# Fails the build if the removed legacy constructs reappear.
#
# Usage: pwsh scripts/check-ledger-derived-clean-break.ps1
#
# SKELETON (T003): the patterns below are declared but NOT yet enforced — the
# constructs they forbid are removed incrementally across US1-US6, so enforcing
# now would fail against code that is still mid-migration. T040 activates each
# pattern (flips $enforced = $true) once its replacement is green, and wires this
# into CI. Until then this script exits 0 so it can live in the tree.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcRoot = Join-Path $repoRoot 'src'
$violations = @()

# Each forbidden construct: the regex, a human message, and whether it is enforced yet.
# T020 -> mirror; T015 -> imperative mutation; T016 -> dual-path branch;
# T017 -> topology heuristic; T005/T024 -> singular NextActionId hint.
$forbidden = @(
    @{ Pattern = 'InstanceMirrorReconstructor';        Message = 'Mirror reconstructor reintroduced (use InstanceProjector)';           Enforced = $false }
    @{ Pattern = 'IsReadOnlyMirror';                    Message = 'Mirror flag reintroduced (there is no mirror)';                       Enforced = $false }
    @{ Pattern = 'Create(Mirror|MirrorAsync)|UpdateMirrorAsync'; Message = 'Mirror write method reintroduced';                          Enforced = $false }
    @{ Pattern = 'ApplyInstanceStateChanges';           Message = 'Imperative instance-state mutation reintroduced (advance via projection)'; Enforced = $false }
    @{ Pattern = '\bLocallyOwned\b';                    Message = 'Dual submit branch (LocallyOwned) reintroduced (single async path)';  Enforced = $false }
    @{ Pattern = '\bNextActionId\b';                    Message = 'Singular NextActionId hint reintroduced (use RoutingDecision)';       Enforced = $false }
)

$searchFiles = Get-ChildItem -Path $srcRoot -Recurse -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

foreach ($rule in $forbidden) {
    if (-not $rule.Enforced) { continue }
    $matches = $searchFiles | Select-String -Pattern $rule.Pattern
    if ($matches -and $matches.Count -gt 0) {
        $violations += $rule
        Write-Host "X VIOLATION: $($rule.Message)" -ForegroundColor Red
        foreach ($m in $matches | Select-Object -First 10) {
            Write-Host "   $($m.Path):$($m.LineNumber): $($m.Line.Trim())" -ForegroundColor DarkGray
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "Clean-break check FAILED with $($violations.Count) violation type(s)." -ForegroundColor Red
    Write-Host "These constructs were removed in feature 145 and must not return." -ForegroundColor Red
    exit 1
}

$enforcedCount = ($forbidden | Where-Object { $_.Enforced }).Count
Write-Host "OK Clean-break check passed ($enforcedCount/$($forbidden.Count) patterns enforced)." -ForegroundColor Green
exit 0
