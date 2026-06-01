# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Clean-break gate for Feature 145 (Ledger-Derived Workflow Instances).
# Fails the build if the removed legacy constructs reappear.
#
# Usage: pwsh scripts/check-ledger-derived-clean-break.ps1
#
# Each pattern is enforced only once its replacement is green. The mirror constructs, the singular
# NextActionId hint, and the LocallyOwned dual-submit branch are removed and ENFORCED. One remains
# unenforced BY DESIGN until its slice lands:
#   - ApplyInstanceStateChanges: retained for the presentation-completion path until the US6 cleanup
#     deletes it (US6 increments 2-3 retired its INVOCATION; removal-follows-proven-replacement).
# LocallyOwned (T016/T034): the submit no longer branches on register ownership — it returns 202 on
# local data-validation + a CARRIER-AWARE fan-out (only peers that carry the register; no seed/
# topology dial), and nothing waits for sealing. The pattern is scoped to the code forms so it does
# NOT catch prose that documents the removal.

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
    # Scoped to code forms (property access `.LocallyOwned`, named-arg/assignment `LocallyOwned:`/`=`,
    # record param `bool LocallyOwned`) with word boundaries so it catches reintroduction WITHOUT
    # flagging prose that documents the removal, nor the unrelated UI `IsLocallyOwned` ownership field.
    @{ Pattern = '(\.LocallyOwned\b|\bLocallyOwned\s*[:=]|bool LocallyOwned\b)'; Message = 'Dual submit branch (LocallyOwned) reintroduced (single async carrier-aware path)'; Enforced = $true }
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
