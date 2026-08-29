#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# McpServerBasics — Quick connectivity test.
# Verifies Docker, MCP image, services, and authentication work.

param(
    # NO 'n1' — this walkthrough runs the MCP server as a LOCAL container and checks local
    # Docker before anything else ('Docker not running' → then `docker compose` against the
    # local stack). It cannot target a remote node as-is. Making it n1-capable means skipping
    # the container steps for remote profiles, which is a real change, not a ValidateSet edit.
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway'
)

$ErrorActionPreference = "Stop"

# Import shared module
$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "MCP Server — Quick Test"

# Load secrets
$secrets = Get-SorchaSecrets -WalkthroughName "mcp-server"

$stepsPassed = 0
$totalSteps = 0

# Test 1: Docker
Write-WtStep "Test 1: Docker"
$totalSteps++
try {
    $null = docker ps 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-WtSuccess "Docker is running"
        $stepsPassed++
    } else {
        Write-WtFail "Docker not running"
    }
} catch {
    Write-WtFail "Docker not available: $($_.Exception.Message)"
}

# Test 2: MCP Server image
Write-WtStep "Test 2: MCP Server Image"
$totalSteps++
$imageExists = docker images sorcha/mcp-server:latest --format "{{.Repository}}" 2>$null
if ($imageExists -match "sorcha/mcp-server") {
    Write-WtSuccess "MCP Server image exists"
    $stepsPassed++
} else {
    Write-WtWarn "MCP Server image not built — building..."
    docker-compose build mcp-server
    Write-WtSuccess "Image built"
    $stepsPassed++
}

# Test 3: Services running
Write-WtStep "Test 3: Services Running"
$totalSteps++
$runningServices = docker-compose ps --services --filter "status=running" 2>$null
if ($runningServices -match "tenant-service") {
    Write-WtSuccess "Sorcha services are running"
    $stepsPassed++
} else {
    Write-WtFail "Services not running — start with: docker-compose up -d"
}

# Test 4: Authentication
Write-WtStep "Test 4: Authentication"
$totalSteps++
try {
    $env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck
    $admin = Connect-SorchaAdmin `
        -TenantUrl $env.TenantUrl `
        -OrgName "MCP Server Demo" `
        -OrgSubdomain "mcp-server" `
        -AdminEmail $secrets.adminEmail `
        -AdminName $secrets.adminName `
        -AdminPassword $secrets.adminPassword

    $jwt = Decode-SorchaJwt -Token $admin.Token
    Write-WtSuccess "Authenticated: $($jwt.sub)"
    $stepsPassed++
} catch {
    Write-WtFail "Authentication failed: $($_.Exception.Message)"
}

# Summary
Write-Host ""
Write-WtBanner "MCP Server Test — Results"
$statusColor = if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" }
Write-Host "  Steps: $stepsPassed/$totalSteps passed" -ForegroundColor $statusColor
Write-Host ""

if ($stepsPassed -eq $totalSteps) {
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    Write-WtInfo "Run full walkthrough: pwsh walkthroughs/McpServerBasics/get-token-and-run-mcp.ps1"
    exit 0
} else {
    Write-Host "  RESULT: FAIL" -ForegroundColor Red
    exit 1
}
