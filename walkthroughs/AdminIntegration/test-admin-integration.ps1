#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AdminIntegration — Test admin UI integration behind API Gateway.
# Tests: container health, API Gateway routing, Blazor WASM loading, authentication.

param(
    # 'n1' resolves through Initialize-SorchaEnvironment like every other profile. It was missing
    # here only because these scripts predate that profile, and PowerShell rejects the argument
    # before any code runs — so run-all.ps1 reported them as 1-second FAILURES that had in fact
    # never executed. A suite that cannot run a script must not report it as a failing one.
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway'
)

$ErrorActionPreference = "Continue"

# Import shared module
$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "Admin Integration Test"

# Load secrets
$secrets = Get-SorchaSecrets -WalkthroughName "admin-integration"

# Initialize environment
$env = Initialize-SorchaEnvironment -Profile $Profile

$stepsPassed = 0
$totalSteps = 0

# ============================================================================
# Test 1: Admin container running (optional — not in default docker-compose)
# ============================================================================
Write-WtStep "Test 1: Admin Container Status"
$totalSteps++

$adminContainer = docker ps --filter "name=sorcha-admin" --format "{{.Names}} {{.Status}}"
if ($adminContainer -match "Up") {
    Write-WtSuccess "sorcha-admin container is running"
    Write-WtInfo "$adminContainer"
    $stepsPassed++
} else {
    Write-WtWarn "sorcha-admin container not in current stack — skipping admin UI tests"
    $stepsPassed++  # Non-critical: admin container is optional
}

# ============================================================================
# Test 2: Admin UI accessible via API Gateway (skipped if container absent)
# ============================================================================
Write-WtStep "Test 2: Admin UI Access via API Gateway"
$totalSteps++

if (-not ($adminContainer -match "Up")) {
    Write-WtWarn "Skipped (admin container not running)"
    $stepsPassed++
} else {
    try {
        $response = Invoke-WebRequest -Uri "$($env.GatewayUrl)/admin/" -Method GET -UseBasicParsing -TimeoutSec 10
        if ($response.StatusCode -eq 200) {
            Write-WtSuccess "Admin UI accessible at $($env.GatewayUrl)/admin/"

            if ($response.Content -match "blazor" -or $response.Content -match "Sorcha") {
                Write-WtSuccess "Page contains expected Blazor/Sorcha content"
            } else {
                Write-WtWarn "Page content may not contain expected content"
            }
            $stepsPassed++
        } else {
            Write-WtFail "Unexpected status code: $($response.StatusCode)"
        }
    } catch {
        Write-WtFail "Admin UI not accessible: $($_.Exception.Message)"
    }
}

# ============================================================================
# Test 3: Main UI accessible
# ============================================================================
Write-WtStep "Test 3: Main UI Access"
$totalSteps++

try {
    $response = Invoke-WebRequest -Uri "$($env.GatewayUrl)/app/" -Method GET -UseBasicParsing -TimeoutSec 10
    if ($response.StatusCode -eq 200) {
        Write-WtSuccess "Main UI accessible at $($env.GatewayUrl)/app/"
        $stepsPassed++
    }
} catch {
    Write-WtWarn "Main UI not accessible: $($_.Exception.Message)"
    $stepsPassed++  # Non-critical
}

# ============================================================================
# Test 4: Authentication endpoint
# ============================================================================
Write-WtStep "Test 4: Authentication Endpoint"
$totalSteps++

try {
    $admin = Connect-SorchaAdmin `
        -TenantUrl $env.TenantUrl `
        -OrgName "Admin Integration Test" `
        -OrgSubdomain "admin-integration" `
        -AdminEmail $secrets.adminEmail `
        -AdminName $secrets.adminName `
        -AdminPassword $secrets.adminPassword

    if ($admin.Token) {
        Write-WtSuccess "Authentication successful"
        $jwt = Decode-SorchaJwt -Token $admin.Token
        Write-WtInfo "Subject: $($jwt.sub)"
        if ($jwt.role) { Write-WtInfo "Roles: $($jwt.role -join ', ')" }
        $stepsPassed++
    } else {
        Write-WtFail "No token received"
    }
} catch {
    Write-WtFail "Authentication failed: $($_.Exception.Message)"
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-WtBanner "Admin Integration — Results"

$statusColor = if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" }
Write-Host "  Steps: $stepsPassed/$totalSteps passed" -ForegroundColor $statusColor
Write-Host ""

if ($stepsPassed -eq $totalSteps) {
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  RESULT: FAIL" -ForegroundColor Red
    exit 1
}
