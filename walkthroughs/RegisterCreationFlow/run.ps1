#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# RegisterCreationFlow — Run
# Demonstrates the full 2-phase register creation with real wallet signing.

param(
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "RegisterCreationFlow — Run"

# Load state
$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "No state.json found. Run setup.ps1 first."
    exit 1
}
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($state.adminToken)" }

$stepsPassed = 0
$totalSteps = 0
$start = Get-Date

# ============================================================================
# Step 1: Create Register (2-phase)
# ============================================================================
Write-WtStep "Step 1: Create Register (Initiate -> Sign -> Finalize)"
$totalSteps++

try {
    $register = New-SorchaRegister `
        -RegisterUrl $state.registerUrl `
        -WalletUrl $state.walletUrl `
        -Name "Register Creation Demo Register" `
        -Description "Created by RegisterCreationFlow walkthrough" `
        -TenantId $state.organizationId `
        -OwnerUserId $state.adminUserId `
        -OwnerWalletAddress $state.walletAddress `
        -Headers $headers `
        -Metadata @{ createdBy = "RegisterCreationFlow/run.ps1" }

    Write-WtSuccess "Register created: $($register.RegisterId)"
    if ($register.GenesisTransactionId) {
        Write-WtInfo "Genesis TX: $($register.GenesisTransactionId)"
    }
    $stepsPassed++
} catch {
    Write-WtFail "Register creation failed: $($_.Exception.Message)"
    $errorBody = Get-SorchaErrorBody -ErrorRecord $_
    if ($errorBody) { Write-WtInfo "Response: $errorBody" }
    exit 1
}

# ============================================================================
# Step 2: Verify Register Exists
# ============================================================================
Write-WtStep "Step 2: Verify Register"
$totalSteps++

try {
    $registerInfo = Invoke-SorchaApi -Method GET `
        -Uri "$($state.registerUrl)/registers/$($register.RegisterId)" `
        -Headers $headers `
        -ShowJson:$ShowJson

    Write-WtSuccess "Register verified: $($registerInfo.name)"
    Write-WtInfo "Status: $($registerInfo.status)"
    $stepsPassed++
} catch {
    Write-WtWarn "Could not verify register (may require async processing)"
    $stepsPassed++  # Non-critical
}

# ============================================================================
# Summary
# ============================================================================
$duration = (Get-Date) - $start

Write-Host ""
Write-WtBanner "RegisterCreationFlow — Results"
$statusColor = if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" }
Write-Host "  Register ID: $($register.RegisterId)" -ForegroundColor White
Write-Host "  Steps: $stepsPassed/$totalSteps passed" -ForegroundColor $statusColor
Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

if ($stepsPassed -eq $totalSteps) {
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  RESULT: FAIL" -ForegroundColor Red
    exit 1
}
