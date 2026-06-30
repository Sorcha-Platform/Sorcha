#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Demo — entry script.
# Bootstraps a sysadmin session against a local Docker stack, then delegates
# to Invoke-AiasDemo in AiasDemo.psm1 to provision the full AIAS authority.
#
# Usage:
#   pwsh demos/AIAS/run-demo.ps1
#   pwsh demos/AIAS/run-demo.ps1 -BaseUrl http://localhost
#   pwsh demos/AIAS/run-demo.ps1 -SkipHealthCheck
#
# Prerequisites: Docker stack running — docker-compose up -d
# See demos/AIAS/DEMO.md for the full operator runbook.

param(
    [string]$BaseUrl         = "http://localhost",
    [string]$AdminEmail      = "admin@sorcha.local",
    [string]$AdminPassword   = "Dev_Pass_2025!",
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'

# ── import AiasDemo module (pulls in SorchaWalkthrough transitively) ─────────
Import-Module (Join-Path $PSScriptRoot "AiasDemo.psm1") -Force -DisableNameChecking

$api = "$BaseUrl/api"

# ── optional health check ────────────────────────────────────────────────────
if (-not $SkipHealthCheck) {
    Write-WtInfo "Checking API Gateway ($BaseUrl/api/health)..."
    try {
        $null = Invoke-WebRequest -Uri "$BaseUrl/api/health" -UseBasicParsing -TimeoutSec 10
        Write-WtSuccess "API Gateway healthy"
    } catch {
        Write-WtWarn "Health check failed — stack may still be starting. Proceeding anyway."
    }
}

# ── bootstrap sysadmin session ───────────────────────────────────────────────
Write-WtStep "sysadmin login ($AdminEmail)"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $api `
    -AdminEmail $AdminEmail `
    -AdminPassword $AdminPassword
Write-WtSuccess "sysadmin connected (org: $($sysAdmin.OrganizationId))"

# ── delegate to Invoke-AiasDemo ──────────────────────────────────────────────
$result = Invoke-AiasDemo `
    -BaseUrl $BaseUrl `
    -SysAdminHeaders $sysAdmin.Headers

Write-WtInfo "Organization:  $($result.organizationId)"
Write-WtInfo "Register:      $($result.registerId)"
Write-WtInfo "Blueprint:     $($result.blueprintId)"
Write-WtInfo "Wallet:        $($result.walletAddress)"
Write-WtInfo "Agent config:  $($result.agentConfigPath)"
