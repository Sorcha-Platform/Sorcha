#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# RegisterMongoDB — MongoDB integration test.
# Verifies MongoDB connectivity, Docker containers, and Register Service health.

param(
    # NO 'n1' — this reads MongoDB through `docker exec sorcha-mongodb` on the LOCAL host. On a
    # remote node the database is only reachable over ssh (see the EncryptionAtRest walkthrough,
    # which does exactly that). Adding 'n1' would fail at the first docker exec.
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway'
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "RegisterMongoDB — Integration Test"

$stepsPassed = 0
$totalSteps = 0

# ============================================================================
# Test 1: MongoDB Container
# ============================================================================
Write-WtStep "Test 1: MongoDB Container"
$totalSteps++

try {
    $mongoTest = docker exec sorcha-mongodb mongosh --quiet --eval "db.adminCommand('ping')" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-WtSuccess "MongoDB is running and responding"
        $stepsPassed++
    } else {
        Write-WtFail "MongoDB not responding"
    }
} catch {
    Write-WtFail "MongoDB container not found. Run: docker-compose up -d"
}

# ============================================================================
# Test 2: Register Service Health
# ============================================================================
Write-WtStep "Test 2: Register Service Health"
$totalSteps++

$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck

try {
    $null = Invoke-RestMethod -Uri "$($env.RegisterUrl)/health" -Method GET -TimeoutSec 10 -UseBasicParsing
    Write-WtSuccess "Register Service is healthy"
    $stepsPassed++
} catch {
    Write-WtFail "Register Service health check failed"
    Write-WtInfo "Check logs: docker-compose logs register-service"
}

# ============================================================================
# Test 3: Docker Compose Services
# ============================================================================
Write-WtStep "Test 3: Docker Compose Services"
$totalSteps++

$runningServices = docker-compose ps --services --filter "status=running" 2>$null
$requiredServices = @("register-service", "mongodb")
$allRunning = $true

foreach ($svc in $requiredServices) {
    if ($runningServices -match $svc) {
        Write-WtSuccess "$svc is running"
    } else {
        Write-WtFail "$svc is not running"
        $allRunning = $false
    }
}

if ($allRunning) { $stepsPassed++ }

# ============================================================================
# Test 4: Register API (authenticated)
# ============================================================================
Write-WtStep "Test 4: Register API Access"
$totalSteps++

try {
    $secrets = Get-SorchaSecrets -WalkthroughName "register-mongodb"
    $admin = Connect-SorchaAdmin `
        -TenantUrl $env.TenantUrl `
        -OrgName "Register MongoDB Test" `
        -OrgSubdomain "register-mongodb" `
        -AdminEmail $secrets.adminEmail `
        -AdminName $secrets.adminName `
        -AdminPassword $secrets.adminPassword

    $registers = Invoke-SorchaApi -Method GET `
        -Uri "$($env.RegisterUrl)/registers" `
        -Headers $admin.Headers

    $count = if ($registers -is [array]) { $registers.Count } else { 0 }
    Write-WtSuccess "Register API accessible ($count registers)"
    $stepsPassed++
} catch {
    Write-WtFail "Register API access failed: $($_.Exception.Message)"
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-WtBanner "RegisterMongoDB — Results"
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
