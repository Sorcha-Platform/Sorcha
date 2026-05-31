# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Clean-break gate for Feature 145 (Ledger-Derived Workflow Instances).
# Fails the build if the removed legacy constructs reappear.
#
# Usage: pwsh scripts/check-ledger-derived-clean-break.ps1
#
# Each pattern is enforced only once its replacement is green (US1 cutover). The mirror
# constructs are fully removed and ENFORCED. The remaining three are still present BY DESIGN
# and stay unenforced until their slice lands:
#   - ApplyInstanceStateChanges: retained for the presentation-completion path (US6).
#   - LocallyOwned: still a live peer-transport routing signal (sealer-selection refinement,
#     Feature 108 follow-up #1 / T017, is a separate change).
#   - NextActionId: the singular hint is retained as a projector fallback until the validator
#     carries the full RoutingDecision through the seal (T024).

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcRoot = Join-Path $repoRoot 'src'
$violations = @()

# Each forbidden construct: the regex, a human message, and whether it is enforced yet.
# T020 -> mirror; T015 -> imperative mutation; T016 -> dual-path branch;
# T017 -> topology heuristic; T005/T024 -> singular NextActionId hint.
# The InstanceMirrorReconstructor pattern matches CODE reintroduction (class / new / generic
# type argument) — NOT historical "replaces InstanceMirrorReconstructor" prose in comments.
$forbidden = @(
    @{ Pattern = '(class |new |<)InstanceMirrorReconstructor'; Message = 'Mirror reconstructor reintroduced (use InstanceProjector)';   Enforced = $true }
    @{ Pattern = 'IsReadOnlyMirror';                    Message = 'Mirror flag reintroduced (there is no mirror)';                       Enforced = $true }
    @{ Pattern = 'Create(Mirror|MirrorAsync)|UpdateMirrorAsync'; Message = 'Mirror write method reintroduced';                          Enforced = $true }
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
