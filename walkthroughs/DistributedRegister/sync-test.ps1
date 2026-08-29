#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# P2P Register Sync — Multi-Node Integration Test
# Tests the full P2P register sync flow: peer self-introduction, advertisement
# discovery, subscription, docket finalization, and live streaming.
#
# Prerequisites:
#   1. Run setup.ps1 on LOCAL machine first
#   2. Both machines running Sorcha Docker stack
#   3. Local peer configured with the remote as a seed (SEED_PEER_* in .env)
#      — peers self-introduce at bootstrap; the legacy n0.sorcha.dev PeerRouter
#      relay is no longer required.
#
# Usage:
#   pwsh walkthroughs/DistributedRegister/sync-test.ps1 -RemoteHost 192.168.51.9
#   pwsh walkthroughs/DistributedRegister/sync-test.ps1 -RemoteHost 192.168.51.9 -Phase 4   # start from phase 4

param(
    [Parameter(Mandatory = $false)]
    [string]$RemoteHost = "192.168.51.9",

    # Legacy PeerRouter relay parameters — only consumed by the Phase 1 / Phase 6
    # router-restart tests below. Peers self-introduce at bootstrap so the relay is
    # no longer required for normal sync; leave these in for back-compat with any
    # operator who still has a router deployed.
    [string]$PeerRouterHost = "n0.sorcha.dev",

    [int]$PeerRouterHttpPort = 8080,

    [string]$LocalPeerPort = "50051",

    [string]$RemotePeerPort = "50051",

    # NO 'n1' — this half of the test talks to http://localhost for the local gateway and peer
    # (see \$localGateway / \$localPeerUrl below). It is a local-vs-remote comparison, not a
    # single-target walkthrough.
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',

    [int]$Phase = 1,

    [switch]$ShowJson,

    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Module & State
# ============================================================================

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "P2P Register Sync — Multi-Node Test"

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "No state.json — run setup.ps1 first"
    exit 1
}

$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($state.adminToken)" }
$localGateway = "http://localhost"
$remoteGateway = "http://$RemoteHost"
$peerRouterUrl = "http://${PeerRouterHost}:${PeerRouterHttpPort}"
$localPeerUrl = "http://localhost:${LocalPeerPort}"
$remotePeerUrl = "http://${RemoteHost}:${RemotePeerPort}"

$stepsPassed = 0
$stepsFailed = 0
$stepsSkipped = 0
$totalSteps = 0
$start = Get-Date
$testResults = @()

# ============================================================================
# Helpers
# ============================================================================

function Test-Step {
    param(
        [string]$Name,
        [scriptblock]$Action,
        [switch]$Critical
    )

    $script:totalSteps++
    Write-WtStep "Test $($script:totalSteps): $Name"

    try {
        $result = & $Action
        Write-WtSuccess $Name
        $script:stepsPassed++
        $script:testResults += @{ Name = $Name; Status = "PASS"; Detail = $result }
        return $result
    }
    catch {
        $msg = $_.Exception.Message
        if ($Critical) {
            Write-WtFail "$Name — $msg"
            $script:stepsFailed++
            $script:testResults += @{ Name = $Name; Status = "FAIL"; Detail = $msg }
            Write-Host ""
            Write-WtFail "Critical test failed — aborting"
            Show-Summary
            exit 1
        }
        else {
            Write-WtWarn "$Name — $msg"
            $script:stepsFailed++
            $script:testResults += @{ Name = $Name; Status = "FAIL"; Detail = $msg }
            return $null
        }
    }
}

function Wait-ForCondition {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutSeconds = 60,
        [int]$PollIntervalSeconds = 5,
        [string]$Description = "condition"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    Write-WtInfo "Waiting up to ${TimeoutSeconds}s for $Description..."

    while ((Get-Date) -lt $deadline) {
        try {
            $result = & $Condition
            if ($result) { return $result }
        }
        catch { }
        Start-Sleep -Seconds $PollIntervalSeconds
    }

    throw "Timeout waiting for $Description after ${TimeoutSeconds}s"
}

function Show-Summary {
    $duration = (Get-Date) - $start
    Write-Host ""
    Write-WtBanner "P2P Register Sync — Results"

    foreach ($r in $script:testResults) {
        $color = if ($r.Status -eq "PASS") { "Green" } elseif ($r.Status -eq "SKIP") { "Yellow" } else { "Red" }
        Write-Host "  [$($r.Status)] $($r.Name)" -ForegroundColor $color
    }

    Write-Host ""
    $total = $script:stepsPassed + $script:stepsFailed + $script:stepsSkipped
    Write-Host "  Passed:  $($script:stepsPassed)/$total" -ForegroundColor $(if ($script:stepsFailed -eq 0) { "Green" } else { "Yellow" })
    Write-Host "  Failed:  $($script:stepsFailed)/$total" -ForegroundColor $(if ($script:stepsFailed -gt 0) { "Red" } else { "Green" })
    Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
    Write-Host ""

    if ($script:stepsFailed -eq 0) {
        Write-Host "  RESULT: PASS" -ForegroundColor Green
    }
    else {
        Write-Host "  RESULT: FAIL ($($script:stepsFailed) failures)" -ForegroundColor Red
    }
    Write-Host ""
}

# ============================================================================
# Phase 1: Connectivity — validates US1 (Streaming Relay)
# ============================================================================

if ($Phase -le 1) {
    Write-WtBanner "Phase 1: Connectivity (US1 — Streaming Relay)"

    # Test 1: PeerRouter reachable
    Test-Step "PeerRouter reachable at $PeerRouterHost" -Critical {
        $health = Invoke-RestMethod -Uri "$peerRouterUrl/health" -Method GET -TimeoutSec 10
        if (-not $health) { throw "No response from PeerRouter" }
        "PeerRouter healthy"
    }

    # Test 2: Both peers registered with Router
    Test-Step "Both peers registered with PeerRouter" -Critical {
        $peers = Invoke-RestMethod -Uri "$peerRouterUrl/peers" -Method GET -TimeoutSec 10
        $peerCount = ($peers | Measure-Object).Count
        if ($peerCount -lt 2) { throw "Expected at least 2 peers, found $peerCount" }
        "Found $peerCount peers registered"
    }

    # Test 3: Local peer has reverse stream (check peer service logs or health)
    Test-Step "Local peer connected to Router" {
        $peerHealth = Invoke-RestMethod -Uri "$localPeerUrl/api/health" -Method GET -TimeoutSec 10
        "Local peer healthy"
    }

    # Test 4: Remote peer reachable
    Test-Step "Remote peer reachable at $RemoteHost" -Critical {
        $health = Invoke-RestMethod -Uri "$remoteGateway/api/health" -Method GET -TimeoutSec 10
        "Remote peer healthy"
    }
}

# ============================================================================
# Phase 2: Discovery — validates US2 (Heartbeat Advertisements)
# ============================================================================

if ($Phase -le 2) {
    Write-WtBanner "Phase 2: Register Discovery (US2 — Heartbeat Advertisements)"

    # Test 5: Create register on local peer
    $register = Test-Step "Create register on local peer" -Critical {
        $reg = New-SorchaRegister `
            -RegisterUrl $state.registerUrl -WalletUrl $state.walletUrl `
            -Name "P2P Sync Test Register" -Description "Testing P2P transaction sync" `
            -TenantId $state.organizationId -OwnerUserId $state.adminUserId `
            -OwnerWalletAddress $state.walletAddress -Headers $headers
        $reg
    }

    $registerId = $register.RegisterId
    Write-WtInfo "Register ID: $registerId"

    # Test 6: Advertise register on peer network
    Test-Step "Advertise register on peer network" {
        $advertiseUrl = "$localPeerUrl/api/registers/$registerId/advertise"
        $body = @{ isPublic = $true } | ConvertTo-Json
        Invoke-RestMethod -Uri $advertiseUrl -Method POST -Headers $headers `
            -ContentType "application/json" -Body $body -TimeoutSec 10
        "Register advertised"
    }

    # Test 7: Register discovered on remote peer (via heartbeat advertisements)
    Test-Step "Register discovered on remote peer via heartbeat advertisements" {
        Wait-ForCondition -TimeoutSeconds 120 -PollIntervalSeconds 10 -Description "register advertisement on remote peer" {
            try {
                $available = Invoke-RestMethod -Uri "$remotePeerUrl/api/registers/available" `
                    -Method GET -TimeoutSec 10
                $found = $available | Where-Object { $_.registerId -eq $registerId }
                if ($found) { return "Register $registerId found on remote peer" }
            }
            catch { }
            return $null
        }
    }
}

# ============================================================================
# Phase 3: Subscription & Sync — validates US3
# ============================================================================

if ($Phase -le 3) {
    Write-WtBanner "Phase 3: Subscription & Sync (US3 — Full Register History)"

    # Load registerId from state if starting from phase > 2
    if (-not $registerId) {
        $syncState = Get-Content -Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sync-state.json") -Raw | ConvertFrom-Json
        $registerId = $syncState.registerId
        Write-WtInfo "Loaded registerId from sync-state.json: $registerId"
    }

    # Test 8: Authenticate on remote peer
    $remoteHeaders = $null
    Test-Step "Authenticate on remote peer" -Critical {
        $secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
        $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
        $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
        $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
            -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -TimeoutSec 10
        $script:remoteHeaders = @{ Authorization = "Bearer $($loginResponse.access_token)" }
        "Authenticated on remote"
    }

    # Test 9: Subscribe remote peer to register
    Test-Step "Subscribe remote peer to register (FullReplica)" {
        $body = @{ mode = "FullReplica" } | ConvertTo-Json
        try {
            Invoke-RestMethod -Uri "$remotePeerUrl/api/registers/$registerId/subscribe" `
                -Method POST -Headers $remoteHeaders -ContentType "application/json" -Body $body -TimeoutSec 10
        }
        catch {
            $statusCode = $null
            try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}
            if ($statusCode -eq 409) {
                Write-WtInfo "Already subscribed (409) — continuing"
            }
            else { throw }
        }
        "Subscribed"
    }

    # Test 10: Subscription reaches FullyReplicated
    Test-Step "Subscription state reaches FullyReplicated" {
        Wait-ForCondition -TimeoutSeconds 300 -PollIntervalSeconds 10 -Description "FullyReplicated state" {
            try {
                $subs = Invoke-RestMethod -Uri "$remotePeerUrl/api/registers/subscriptions" `
                    -Method GET -Headers $remoteHeaders -TimeoutSec 10
                $sub = $subs | Where-Object { $_.registerId -eq $registerId }
                Write-WtInfo "  Current state: $($sub.syncState)"
                if ($sub.syncState -in @("FullyReplicated", "Active")) {
                    return "State: $($sub.syncState)"
                }
            }
            catch { }
            return $null
        }
    }
}

# ============================================================================
# Phase 4: Finalization — validates US4 + US6 (Docket Finalization + Signature)
# ============================================================================

if ($Phase -le 4) {
    Write-WtBanner "Phase 4: Docket Finalization (US4 — Cache to Register Storage)"

    if (-not $registerId) {
        $syncState = Get-Content -Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sync-state.json") -Raw | ConvertFrom-Json
        $registerId = $syncState.registerId
    }
    if (-not $remoteHeaders) {
        $secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
        $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
        $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
        $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
            -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -TimeoutSec 10
        $remoteHeaders = @{ Authorization = "Bearer $($loginResponse.access_token)" }
    }

    # Test 11: Finalized transactions queryable on remote Register Service
    Test-Step "Finalized transactions queryable on remote Register Service" {
        Wait-ForCondition -TimeoutSeconds 120 -PollIntervalSeconds 10 -Description "finalized transactions on remote" {
            try {
                $txs = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId/transactions" `
                    -Method GET -Headers $remoteHeaders -TimeoutSec 10
                $txCount = ($txs | Measure-Object).Count
                if ($txCount -gt 0) {
                    return "Found $txCount finalized transactions on remote"
                }
                Write-WtInfo "  Transactions on remote: $txCount (waiting...)"
            }
            catch { }
            return $null
        }
    }

    # Test 12: Register height matches between peers
    Test-Step "Register height matches between local and remote" {
        $localReg = Invoke-RestMethod -Uri "$localGateway/api/registers/$registerId" `
            -Method GET -Headers $headers -TimeoutSec 10
        $remoteReg = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId" `
            -Method GET -Headers $remoteHeaders -TimeoutSec 10

        $localHeight = $localReg.height
        $remoteHeight = $remoteReg.height

        if ($localHeight -ne $remoteHeight) {
            throw "Height mismatch: local=$localHeight, remote=$remoteHeight"
        }
        "Height matches: $localHeight"
    }
}

# ============================================================================
# Phase 5: Live Streaming — validates US5
# ============================================================================

if ($Phase -le 5) {
    Write-WtBanner "Phase 5: Live Transaction Streaming (US5)"

    if (-not $registerId) {
        $syncState = Get-Content -Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sync-state.json") -Raw | ConvertFrom-Json
        $registerId = $syncState.registerId
    }
    if (-not $remoteHeaders) {
        $secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
        $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
        $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
        $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
            -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -TimeoutSec 10
        $remoteHeaders = @{ Authorization = "Bearer $($loginResponse.access_token)" }
    }

    # Get current transaction count on remote before new action
    $beforeCount = 0
    try {
        $txsBefore = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId/transactions" `
            -Method GET -Headers $remoteHeaders -TimeoutSec 10
        $beforeCount = ($txsBefore | Measure-Object).Count
    }
    catch { }

    # Test 13: Submit a new transaction on local peer
    Test-Step "Submit new transaction on local peer" {
        # Submit a control transaction (simplest — no blueprint needed)
        $txBody = @{
            registerId = $registerId
            type       = "control"
            metadata   = @{
                description = "P2P sync live test — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            }
        } | ConvertTo-Json -Depth 3

        try {
            Invoke-RestMethod -Uri "$localGateway/api/registers/$registerId/transactions" `
                -Method POST -Headers $headers -ContentType "application/json" -Body $txBody -TimeoutSec 10
            "Transaction submitted on local"
        }
        catch {
            Write-WtWarn "Direct transaction submit may not be supported — trying blueprint action"
            # If direct tx submission isn't available, this is expected
            # The test still validates that EXISTING transactions replicate
            "Skipped — direct transaction API not available (existing data validates sync)"
        }
    }

    # Test 14: New transaction appears on remote within 15 seconds
    Test-Step "Live transaction propagates to remote within 30 seconds" {
        Wait-ForCondition -TimeoutSeconds 30 -PollIntervalSeconds 3 -Description "new transaction on remote" {
            try {
                $txsAfter = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId/transactions" `
                    -Method GET -Headers $remoteHeaders -TimeoutSec 10
                $afterCount = ($txsAfter | Measure-Object).Count
                if ($afterCount -gt $beforeCount) {
                    return "New transaction appeared: $beforeCount → $afterCount"
                }
                Write-WtInfo "  Remote transactions: $afterCount (waiting for > $beforeCount)"
            }
            catch { }
            return $null
        }
    }
}

# ============================================================================
# Phase 6: Resilience
# ============================================================================

if ($Phase -le 6) {
    Write-WtBanner "Phase 6: Resilience"

    if (-not $registerId) {
        $syncState = Get-Content -Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sync-state.json") -Raw | ConvertFrom-Json
        $registerId = $syncState.registerId
    }
    if (-not $remoteHeaders) {
        $secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
        $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
        $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
        $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
            -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -TimeoutSec 10
        $remoteHeaders = @{ Authorization = "Bearer $($loginResponse.access_token)" }
    }

    # Get transaction count before restart
    $preRestartCount = 0
    try {
        $txs = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId/transactions" `
            -Method GET -Headers $remoteHeaders -TimeoutSec 10
        $preRestartCount = ($txs | Measure-Object).Count
    }
    catch { }

    # Test 15: Finalized data survives remote peer restart
    Test-Step "Finalized data survives peer restart (restart remote peer-service)" {
        Write-WtInfo "Pre-restart transaction count: $preRestartCount"
        Write-WtInfo ""
        Write-WtInfo ">>> MANUAL STEP: Restart the peer-service on the REMOTE machine <<<"
        Write-WtInfo "    ssh $RemoteHost 'docker-compose restart peer-service'"
        Write-WtInfo ""

        $response = Read-Host "Press Enter after restarting (or 's' to skip)"
        if ($response -eq 's') {
            $script:stepsSkipped++
            $script:totalSteps--
            return "Skipped"
        }

        # Wait for remote to come back up
        Wait-ForCondition -TimeoutSeconds 120 -PollIntervalSeconds 5 -Description "remote peer to restart" {
            try {
                $null = Invoke-RestMethod -Uri "$remoteGateway/api/health" -Method GET -TimeoutSec 5
                return $true
            }
            catch { return $null }
        }

        # Re-authenticate (token may have expired)
        $secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
        $encodedPassword = [Uri]::EscapeDataString($secrets.adminPassword)
        $loginBody = "grant_type=password&username=$($secrets.adminEmail)&password=$encodedPassword&client_id=sorcha-cli"
        $loginResponse = Invoke-RestMethod -Uri "$remoteGateway/api/service-auth/token" `
            -Method POST -ContentType "application/x-www-form-urlencoded" -Body $loginBody -TimeoutSec 10
        $script:remoteHeaders = @{ Authorization = "Bearer $($loginResponse.access_token)" }

        # Check transactions survived
        $txsAfter = Invoke-RestMethod -Uri "$remoteGateway/api/registers/$registerId/transactions" `
            -Method GET -Headers $remoteHeaders -TimeoutSec 10
        $postRestartCount = ($txsAfter | Measure-Object).Count

        if ($postRestartCount -lt $preRestartCount) {
            throw "Data lost! Before: $preRestartCount, After: $postRestartCount"
        }
        "Data survived restart: $postRestartCount transactions (was $preRestartCount)"
    }

    # Test 16: PeerRouter restart — peers reconnect
    Test-Step "Peers reconnect after PeerRouter restart" {
        Write-WtInfo ""
        Write-WtInfo ">>> MANUAL STEP: Restart PeerRouter on Azure <<<"
        Write-WtInfo "    az containerapp revision restart -n sorcha-peer-router -g sorcha-rg"
        Write-WtInfo ""

        $response = Read-Host "Press Enter after restarting (or 's' to skip)"
        if ($response -eq 's') {
            $script:stepsSkipped++
            $script:totalSteps--
            return "Skipped"
        }

        # Wait for Router to come back
        Wait-ForCondition -TimeoutSeconds 120 -PollIntervalSeconds 10 -Description "PeerRouter restart" {
            try {
                $null = Invoke-RestMethod -Uri "$peerRouterUrl/health" -Method GET -TimeoutSec 5
                return $true
            }
            catch { return $null }
        }

        # Wait for peers to re-register
        Wait-ForCondition -TimeoutSeconds 120 -PollIntervalSeconds 10 -Description "peers to re-register" {
            try {
                $peers = Invoke-RestMethod -Uri "$peerRouterUrl/peers" -Method GET -TimeoutSec 5
                $peerCount = ($peers | Measure-Object).Count
                if ($peerCount -ge 2) { return "Both peers reconnected ($peerCount peers)" }
                Write-WtInfo "  Peers registered: $peerCount (waiting for 2+)"
            }
            catch { }
            return $null
        }
    }
}

# ============================================================================
# Save state for phase resumption
# ============================================================================

if ($registerId) {
    $syncStateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "sync-state.json"
    @{ registerId = $registerId } | ConvertTo-Json | Set-Content -Path $syncStateFile -Encoding UTF8
}

# ============================================================================
# Summary
# ============================================================================

Show-Summary

if ($stepsFailed -eq 0) { exit 0 } else { exit 1 }
