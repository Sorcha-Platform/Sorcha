#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Feature 135 (EUDI credential-format & unified trust) clean-break CI gate.
#
# Feature 135 removed two pre-feature trust mechanisms and replaced them with the
# unified ITrustEvaluator + trust-source model:
#   - HaipPresentationVerifier._trustedRoots + AddTrustedRoot(...) — the verifier's
#     static trusted-root list. Replaced by the x509-tenant / trustlist trust sources
#     over ITenantTrustAnchorProvider / ITrustListProvider.
#   - CredentialRequirement.AcceptedIssuers — the flat issuer allowlist. Replaced by
#     CredentialRequirement.TrustPolicy. (Removal is compile-enforced; this gate guards
#     the trusted-root API, which is grep-only.)
#
# The clean break ships with NO compatibility shims, so these symbols must never
# reappear in src/. This gate fails the build if they do.
#
# NOTE: seven unrelated presentation-request / verifier DTOs carry their OWN
# `AcceptedIssuers` property (Wallet PresentationRequest, HAIP VerifierModels, UI
# PresentationAdminModels, etc.) — those are intentionally retained, so this gate
# does NOT match `AcceptedIssuers`. The removed CredentialRequirement member is
# enforced by the compiler.
#
# Usage:   pwsh scripts/check-trust-clean-break.ps1
# Exit:    0 — clean; 1 — a forbidden symbol reappeared.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$srcRoot = Join-Path $repo 'src'

# Forbidden symbols (regex) — the retired trusted-root API.
$forbidden = @(
    @{ Pattern = 'AddTrustedRoot\s*\('; Why = 'retired — use the x509-tenant / trustlist trust sources' },
    @{ Pattern = '_trustedRoots';        Why = 'retired — trusted roots come from ITenantTrustAnchorProvider' }
)

$violations = [System.Collections.Generic.List[string]]::new()

$files = Get-ChildItem -LiteralPath $srcRoot -Recurse -File -Include *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($rule in $forbidden) {
            if ($lines[$i] -match $rule.Pattern) {
                $rel = $file.FullName.Substring($repo.Length + 1)
                $violations.Add("$rel`:$($i + 1): '$($rule.Pattern)' — $($rule.Why)")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Feature 135 clean-break gate FAILED — retired trust symbols reappeared:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Feature 135 clean-break gate passed — no retired trust symbols in src/." -ForegroundColor Green
exit 0
