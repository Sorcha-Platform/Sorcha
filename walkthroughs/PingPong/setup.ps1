#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PingPong — Setup
# Bootstrap org, create wallets, register participant, create register, publish blueprint.

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PingPong — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "pingpong"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# Step 1: Bootstrap org
Write-WtStep "Step 1: Bootstrap Organization"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Ping-Pong Walkthrough" `
    -OrgSubdomain "pingpong" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

# Step 2: Create wallets
Write-WtStep "Step 2: Create Wallets"
$pingWallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Ping Wallet" -Headers $admin.Headers -FetchPublicKey
$pongWallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Pong Wallet" -Headers $admin.Headers -FetchPublicKey

# Step 3: Register participant + link wallets
Write-WtStep "Step 3: Register Participant & Link Wallets"
$participant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $admin.OrganizationId `
    -WalletAddress $pingWallet.Address `
    -DisplayName $secrets.adminName `
    -Headers $admin.Headers

# Link pong wallet too
try {
    $challengeBody = @{ walletAddress = $pongWallet.Address; algorithm = "ED25519" }
    $challengeResponse = Invoke-SorchaApi -Method POST `
        -Uri "$($env.TenantUrl)/organizations/$($admin.OrganizationId)/participants/$($participant.ParticipantId)/wallet-links" `
        -Body $challengeBody -Headers $admin.Headers

    $challengeBytes = [System.Text.Encoding]::UTF8.GetBytes($challengeResponse.challenge)
    $signBody = @{ transactionData = [Convert]::ToBase64String($challengeBytes); isPreHashed = $false }
    $signResponse = Invoke-SorchaApi -Method POST `
        -Uri "$($env.WalletUrl)/v1/wallets/$($pongWallet.Address)/sign" `
        -Body $signBody -Headers $admin.Headers

    $verifyBody = @{ signature = $signResponse.signature; publicKey = $signResponse.publicKey }
    $null = Invoke-SorchaApi -Method POST `
        -Uri "$($env.TenantUrl)/organizations/$($admin.OrganizationId)/participants/$($participant.ParticipantId)/wallet-links/$($challengeResponse.challengeId)/verify" `
        -Body $verifyBody -Headers $admin.Headers
    Write-WtSuccess "Pong wallet linked"
} catch {
    Write-WtWarn "Pong wallet link may already exist — continuing"
}

# Resolve actually-linked wallets for idempotent re-runs
$linkedWallets = Invoke-SorchaApi -Method GET `
    -Uri "$($env.TenantUrl)/participants/$($participant.ParticipantId)/wallet-links" `
    -Headers $admin.Headers

$actualPingAddr = $pingWallet.Address
$actualPongAddr = $pongWallet.Address
if ($linkedWallets -and $linkedWallets.Count -ge 2) {
    $actualPingAddr = $linkedWallets[0].walletAddress
    $actualPongAddr = $linkedWallets[1].walletAddress
    Write-WtInfo "Using linked wallets: ping=$actualPingAddr, pong=$actualPongAddr"
}

# Step 4: Create register
Write-WtStep "Step 4: Create Register"
$register = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl `
    -WalletUrl $env.WalletUrl `
    -Name "Ping-Pong Register" `
    -Description "Register for the ping-pong walkthrough" `
    -TenantId $admin.OrganizationId `
    -OwnerUserId $admin.AdminUserId `
    -OwnerWalletAddress $actualPingAddr `
    -Headers $admin.Headers

# Step 5: Publish blueprint
Write-WtStep "Step 5: Publish Blueprint"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templatePath = Join-Path $scriptDir "templates/ping-pong-template.json"

$walletMap = @{
    "ping" = $actualPingAddr
    "pong" = $actualPongAddr
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath $templatePath `
    -WalletMap $walletMap `
    -Headers $admin.Headers `
    -IdPrefix "pingpong" `
    -RegisterId $register.RegisterId

# Save state
$state = @{
    profile         = $Profile
    organizationId  = $admin.OrganizationId
    adminToken      = $admin.Token
    pingWallet      = $actualPingAddr
    pongWallet      = $actualPongAddr
    registerId      = $register.RegisterId
    blueprintId     = $blueprint.BlueprintId
    blueprintUrl    = $env.BlueprintUrl
}

$stateFile = Join-Path $scriptDir "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — state saved to state.json"
Write-WtInfo "Run: pwsh walkthroughs/PingPong/run.ps1"
