#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Service-address config-key CI gate.
#
# A Sorcha service's base address is resolved through
# Sorcha.ServiceClients.Configuration.SorchaServiceAddresses, never by reading a config key
# literal at the call site.
#
# Why this is gated:
#
#   An audit found 19 distinct key spellings addressing 8 services. The Tenant Service alone had
#   four — ServiceClients:TenantService:Address, ServiceClients:Tenant:BaseAddress,
#   Services:TenantService:BaseAddress and Services:Tenant:Url — and six call sites each
#   hand-rolled a DIFFERENT fallback chain over them. Which key a deployment had to set therefore
#   depended on which client happened to resolve it, and a call site reading only one spelling
#   picks up nothing when the deployment sets another. Nothing fails loudly: the client just gets
#   null and falls back to a hardcoded default, so it silently talks to the wrong place (or to
#   localhost) instead of the configured one.
#
# SCOPE — the API Gateway's own `Services:{X}:Url` section is NOT covered.
#
#   That is the gateway's deliberate configuration namespace for its aggregation views
#   (health, OpenAPI, dashboard), it is what compose sets for that container, and
#   SorchaServiceAddresses already accepts it as a fallback spelling. Only the drifted
#   ServiceClients:* / *Service:Endpoint families are gated.
#
# Usage:
#   pwsh scripts/check-service-address-keys.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a gated address key read as a literal (and not allowlisted), or a stale allowlist entry

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.service-address-keys-allowlist'

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

# The canonical home — the resolver names every spelling by construction.
$canonicalFile = "$repo/src/Common/Sorcha.ServiceClients.Http/Configuration/SorchaServiceAddresses.cs".Replace('\', '/')

if (-not (Test-Path -LiteralPath $canonicalFile)) {
    Write-Error "Canonical resolver not found: $canonicalFile"
    exit 1
}

# Gated spellings. `Services:{X}:Url` is deliberately absent — see the SCOPE note above.
$literalPattern = '"(ServiceClients:[A-Za-z]+:(Address|BaseAddress)|Services:[A-Za-z]+Service:BaseAddress|[A-Za-z]+Service:Endpoint)"'

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
    Write-Host "FAIL: service-address config key read as a literal." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Resolve through the shared cascade instead:" -ForegroundColor Yellow
    Write-Host "  using Sorcha.ServiceClients.Configuration;"
    Write-Host "  SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant) ?? <your default>"
    Write-Host ""
    Write-Host "Reading one spelling means a deployment that sets another silently yields null and"
    Write-Host "the client falls back to a hardcoded address. See CLAUDE.md."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — no longer read a gated address key:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .service-address-keys-allowlist in the same PR. The allowlist may only shrink."
}

if ($failed) {
    exit 1
}

Write-Host ("OK: service-address key gate passed. {0} file(s) on the allowlist." -f $allowed.Count) -ForegroundColor Green
if ($allowed.Count -eq 0) {
    Write-Host "  Allowlist is empty — every gated address key resolves through SorchaServiceAddresses." -ForegroundColor Green
}
exit 0
