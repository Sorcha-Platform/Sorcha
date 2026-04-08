#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# WalletVerification — Run
# Tests wallet creation, signing, and register integration for ED25519.

param(
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "WalletVerification — Run"

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
# Step 1: Create Wallet
# ============================================================================
Write-WtStep "Step 1: Create ED25519 Wallet"
$totalSteps++

try {
    $wallet = New-SorchaWallet `
        -WalletUrl $state.walletUrl `
        -Name "Verification Test Wallet" `
        -Headers $headers `
        -FetchPublicKey

    Write-WtInfo "Address: $($wallet.Address)"
    Write-WtInfo "Public Key: $($wallet.PublicKey)"
    $stepsPassed++
} catch {
    Write-WtFail "Wallet creation failed: $($_.Exception.Message)"
    exit 1
}

# ============================================================================
# Step 2: Sign Data
# ============================================================================
Write-WtStep "Step 2: Sign Test Data"
$totalSteps++

try {
    $testMessage = "Hello from WalletVerification walkthrough at $(Get-Date -Format 'o')"
    $messageBytes = [System.Text.Encoding]::UTF8.GetBytes($testMessage)
    $messageBase64 = [Convert]::ToBase64String($messageBytes)

    $signBody = @{
        transactionData = $messageBase64
        isPreHashed     = $false
    }

    $signResponse = Invoke-SorchaApi -Method POST `
        -Uri "$($state.walletUrl)/v1/wallets/$($wallet.Address)/sign" `
        -Body $signBody `
        -Headers $headers `
        -ShowJson:$ShowJson

    Write-WtSuccess "Data signed by wallet"
    Write-WtInfo "Algorithm: $($signResponse.algorithm)"
    Write-WtInfo "Signature length: $($signResponse.signature.Length) chars"
    $stepsPassed++
} catch {
    Write-WtFail "Signing failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 3: Sign Pre-Hashed Data (like register attestations)
# ============================================================================
Write-WtStep "Step 3: Sign Pre-Hashed Data"
$totalSteps++

try {
    # Simulate a SHA-256 hash (like register attestation dataToSign)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($testMessage))
    $hashBase64 = [Convert]::ToBase64String($hashBytes)

    $signBody = @{
        transactionData = $hashBase64
        isPreHashed     = $true
    }

    $signResponse = Invoke-SorchaApi -Method POST `
        -Uri "$($state.walletUrl)/v1/wallets/$($wallet.Address)/sign" `
        -Body $signBody `
        -Headers $headers `
        -ShowJson:$ShowJson

    Write-WtSuccess "Pre-hashed data signed (isPreHashed=true)"
    $stepsPassed++
} catch {
    Write-WtFail "Pre-hashed signing failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 4: Register Creation with Real Signing
# ============================================================================
Write-WtStep "Step 4: Create Register with Signed Attestations"
$totalSteps++

try {
    $register = New-SorchaRegister `
        -RegisterUrl $state.registerUrl `
        -WalletUrl $state.walletUrl `
        -Name "Wallet Verification Register" `
        -Description "Created by WalletVerification walkthrough" `
        -TenantId $state.organizationId `
        -OwnerUserId $state.adminUserId `
        -OwnerWalletAddress $wallet.Address `
        -Headers $headers

    Write-WtSuccess "Register created with real signatures: $($register.RegisterId)"
    $stepsPassed++
} catch {
    Write-WtFail "Register creation failed: $($_.Exception.Message)"
    $errorBody = Get-SorchaErrorBody -ErrorRecord $_
    if ($errorBody) { Write-WtInfo "Response: $errorBody" }
}

# ============================================================================
# Summary
# ============================================================================
$duration = (Get-Date) - $start

Write-Host ""
Write-WtBanner "WalletVerification — Results"
$statusColor = if ($stepsPassed -eq $totalSteps) { "Green" } else { "Red" }
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
