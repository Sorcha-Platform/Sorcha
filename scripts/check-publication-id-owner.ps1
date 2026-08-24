#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Publication-id ownership gate (Feature 195).
#
# A blueprint definition's identity is the id of the transaction that published it:
#
#   publicationTxId = SHA-256("sorcha:blueprint-publication:v1" 0x1F registerId 0x1F blueprintId
#                             0x1F canonicalDefinitionJson)
#
# ONE PRODUCER. Only the Register Service mints that id. Everything else READS it — the Blueprint
# Service records what the publish call returns, recovery reads real transaction ids, instance
# creation reads the published store, and a starting action reads the instance's pin.
#
# Why this is gated rather than left to review:
#
#   The formula this replaced had FOUR homes — the Register Service, twice in the Blueprint Service,
#   and hand-rewritten a fifth time in a test that existed to guard it. All four existed only because
#   the published-blueprint store never recorded the transaction id it was published as. A second
#   producer does not throw: it computes a plausible id that disagrees with the ledger's, and the
#   consequence surfaces far away as a definition that cannot be resolved.
#
#   This is the same hazard class as the derivation contexts (CLAUDE.md §15) and the cross-boundary
#   validation codes (§16): a value one project mints and another names.
#
# Scope notes:
#
#   * The TYPE lives in Sorcha.Blueprint.Models — a shared leaf — because the golden-vector test must
#     be able to reach it. Placement cannot express the restriction, so the CALLERS are gated instead.
#   * `ComputeFromDefinition` is the canonicalise-then-hash convenience overload and is gated
#     identically.
#   * RECOVERY is a legitimate caller and is allowlisted: it VERIFIES rather than mints, recomputing
#     the id from the bytes it received and comparing to the transaction's own id. That check is the
#     whole reason the identity is self-anchoring, so forbidding it would remove the property the
#     feature exists to add.
#   * Tests are out of scope. A test asserting the construction is what pins it.
#
# Usage:
#   pwsh scripts/check-publication-id-owner.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a call to BlueprintPublicationId.Compute/ComputeFromDefinition outside the permitted set,
#       or a stale allowlist entry

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.publication-id-owner-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

# Repo-relative paths permitted to call the producer, one per line. May only SHRINK.
$allowed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) { continue }
    [void]$allowed.Add(($trimmed -replace '/', [IO.Path]::DirectorySeparatorChar))
}

# The canonical file itself declares the members, so it is never a violation.
$canonicalFile = Join-Path $repo 'src\Common\Sorcha.Blueprint.Models\Canonical\BlueprintPublicationId.cs'

$pattern = 'BlueprintPublicationId\s*\.\s*(Compute|ComputeFromDefinition)\s*\('

$violations = New-Object System.Collections.Generic.List[string]
$seenAllowed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

$files = Get-ChildItem -LiteralPath (Join-Path $repo 'src') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

foreach ($file in $files) {
    if ($file.FullName -ieq $canonicalFile) { continue }

    $rel = $file.FullName.Substring($repo.Length + 1)

    # Strip line comments so illustrative prose in XML docs is not a call site. Block comments are
    # not stripped — a commented-out call is dead code that should be deleted, not allowlisted.
    $lines = Get-Content -LiteralPath $file.FullName
    $index = 0
    foreach ($line in $lines) {
        $index++
        $code = $line -replace '//.*$', ''
        if ($code -notmatch $pattern) { continue }

        if ($allowed.Contains($rel)) {
            [void]$seenAllowed.Add($rel)
            continue
        }

        $violations.Add("$rel($index): $($line.Trim())")
    }
}

$stale = @($allowed | Where-Object { -not $seenAllowed.Contains($_) })

if ($violations.Count -eq 0 -and $stale.Count -eq 0) {
    Write-Host "publication-id-owner gate: OK ($($seenAllowed.Count) permitted call site(s))."
    exit 0
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host 'PUBLICATION-ID OWNERSHIP VIOLATION' -ForegroundColor Red
    Write-Host ''
    Write-Host 'A blueprint definition''s publication id has ONE producer: the Register Service.'
    Write-Host 'Everything else reads the value it returns. Minting a second one does not throw — it'
    Write-Host 'computes a plausible id that disagrees with the ledger''s, and the failure surfaces far'
    Write-Host 'away as a definition that cannot be resolved.'
    Write-Host ''
    foreach ($v in $violations) { Write-Host "  $v" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'If the caller genuinely VERIFIES rather than mints (recovery does), add its'
    Write-Host 'repo-relative path to .publication-id-owner-allowlist with a comment saying why.'
}

if ($stale.Count -gt 0) {
    Write-Host ''
    Write-Host 'STALE ALLOWLIST ENTRIES (the allowlist may only shrink):' -ForegroundColor Yellow
    foreach ($s in $stale) { Write-Host "  $s" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'These paths no longer call the producer. Remove them.'
}

exit 1
