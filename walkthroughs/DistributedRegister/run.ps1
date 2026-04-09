#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# DistributedRegister — Run
# Create register on local, verify replication to remote peer.

param(
    [string]$RemoteHost = "192.168.51.9",
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "DistributedRegister — Run"

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($state.adminToken)" }
$remoteGateway = "http://$RemoteHost"

$stepsPassed = 0
$totalSteps = 0
$start = Get-Date

# Step 1: Create register on local
Write-WtStep "Step 1: Create Register (Local)"
$totalSteps++
try {
    $register = New-SorchaRegister `
        -RegisterUrl $state.registerUrl -WalletUrl $state.walletUrl `
        -Name "Distributed Test Register" -Description "Testing cross-machine replication" `
        -TenantId $state.organizationId -OwnerUserId $state.adminUserId `
        -OwnerWalletAddress $state.walletAddress -Headers $headers
    Write-WtSuccess "Register created locally: $($register.RegisterId)"

    # Public org subscription (local node) so consumer users see the register.
    if ($state.tenantUrl) {
        $null = Add-SorchaPublicOrgSubscription `
            -TenantUrl $state.tenantUrl `
            -RegisterId $register.RegisterId `
            -RegisterName "Distributed Test Register" `
            -SysAdminHeaders $headers
    } else {
        Write-WtWarn "state.tenantUrl not set — re-run setup.ps1 to enable Public org subscription"
    }

    $stepsPassed++
} catch {
    Write-WtFail "Local register creation failed: $($_.Exception.Message)"
    exit 1
}

# Step 2: Check peer connectivity
Write-WtStep "Step 2: Check Remote Peer"
$totalSteps++
try {
    $null = Invoke-RestMethod -Uri "$remoteGateway/api/health" -Method GET -TimeoutSec 10 -UseBasicParsing
    Write-WtSuccess "Remote peer is reachable at $remoteGateway"
    $stepsPassed++
} catch {
    Write-WtFail "Remote peer not reachable at $remoteGateway"
    Write-WtInfo "Ensure the remote Sorcha stack is running with peer seeding configured"
}

# Step 3: Authenticate on remote
Write-WtStep "Step 3: Authenticate on Remote"
$totalSteps++
try {
    $secrets = Get-SorchaSecrets -WalkthroughName "dist-register"
    $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
    $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
    $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
        -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -UseBasicParsing
    $remoteToken = $loginResponse.access_token
    $remoteHeaders = @{ Authorization = "Bearer $remoteToken" }
    Write-WtSuccess "Authenticated on remote"
    $stepsPassed++
} catch {
    Write-WtWarn "Remote auth failed (org may not exist on remote) — skipping remote checks"
    $remoteHeaders = $null
}

# Step 4: Verify replication
Write-WtStep "Step 4: Verify Replication on Remote"
$totalSteps++
if ($remoteHeaders) {
    Write-WtInfo "Waiting 30s for replication..."
    Start-Sleep -Seconds 30

    try {
        $remoteRegister = Invoke-RestMethod `
            -Uri "$remoteGateway/api/registers/$($register.RegisterId)" `
            -Method GET -Headers $remoteHeaders -UseBasicParsing

        Write-WtSuccess "Register replicated to remote: $($remoteRegister.name)"
        $stepsPassed++
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($statusCode -eq 404) {
            Write-WtWarn "Register not yet replicated (404) — may need more time or peer config"
        } else {
            Write-WtFail "Remote verification failed: $($_.Exception.Message)"
        }
    }
} else {
    Write-WtWarn "Skipping remote verification (no remote auth)"
    $stepsPassed++  # Non-critical when remote unavailable
}

# Summary
$duration = (Get-Date) - $start
Write-Host ""
Write-WtBanner "DistributedRegister — Results"
$statusColor = if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" }
Write-Host "  Register: $($register.RegisterId)" -ForegroundColor White
Write-Host "  Steps: $stepsPassed/$totalSteps" -ForegroundColor $statusColor
Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

if ($stepsPassed -eq $totalSteps) { Write-Host "  RESULT: PASS" -ForegroundColor Green; exit 0 }
else { Write-Host "  RESULT: FAIL" -ForegroundColor Red; exit 1 }
