#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# OrganizationPingPong — Setup
# Full org setup: bootstrap, users, wallets, participant, register, blueprint.

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "OrganizationPingPong — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "org-pingpong"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# Step 1: Bootstrap org
Write-WtStep "Step 1: Bootstrap Organization"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Organization Ping-Pong Demo" `
    -OrgSubdomain "org-pingpong" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

# Step 2: Create participant users
Write-WtStep "Step 2: Create Participant Users"
$null = Get-OrCreateUser -TenantUrl $env.TenantUrl -OrganizationId $admin.OrganizationId `
    -Email $secrets.alphaEmail -DisplayName "Participant Alpha" -Headers $admin.Headers
$null = Get-OrCreateUser -TenantUrl $env.TenantUrl -OrganizationId $admin.OrganizationId `
    -Email $secrets.betaEmail -DisplayName "Participant Beta" -Headers $admin.Headers

# Step 3: Create wallets
Write-WtStep "Step 3: Create Wallets"
$designerWallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Designer Wallet" -Headers $admin.Headers
$alphaWallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Alpha Wallet" -Headers $admin.Headers -FetchPublicKey
$betaWallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Beta Wallet" -Headers $admin.Headers -FetchPublicKey

# Step 4: Register participant + link wallets
Write-WtStep "Step 4: Register Participant & Link Wallets"
$participant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl -WalletUrl $env.WalletUrl `
    -OrganizationId $admin.OrganizationId `
    -WalletAddress $alphaWallet.Address `
    -DisplayName $secrets.adminName `
    -Headers $admin.Headers

# Link beta wallet
try {
    $challengeBody = @{ walletAddress = $betaWallet.Address; algorithm = "ED25519" }
    $cr = Invoke-SorchaApi -Method POST `
        -Uri "$($env.TenantUrl)/organizations/$($admin.OrganizationId)/participants/$($participant.ParticipantId)/wallet-links" `
        -Body $challengeBody -Headers $admin.Headers
    $cb = [System.Text.Encoding]::UTF8.GetBytes($cr.challenge)
    $sr = Invoke-SorchaApi -Method POST `
        -Uri "$($env.WalletUrl)/v1/wallets/$($betaWallet.Address)/sign" `
        -Body @{ transactionData = [Convert]::ToBase64String($cb); isPreHashed = $false } -Headers $admin.Headers
    $null = Invoke-SorchaApi -Method POST `
        -Uri "$($env.TenantUrl)/organizations/$($admin.OrganizationId)/participants/$($participant.ParticipantId)/wallet-links/$($cr.challengeId)/verify" `
        -Body @{ signature = $sr.signature; publicKey = $sr.publicKey } -Headers $admin.Headers
    Write-WtSuccess "Beta wallet linked"
} catch { Write-WtWarn "Beta wallet link may already exist — continuing" }

# Resolve actually-linked wallets
$linkedWallets = Invoke-SorchaApi -Method GET `
    -Uri "$($env.TenantUrl)/participants/$($participant.ParticipantId)/wallet-links" -Headers $admin.Headers
$actualAlpha = $alphaWallet.Address
$actualBeta = $betaWallet.Address
if ($linkedWallets -and $linkedWallets.Count -ge 2) {
    $actualAlpha = $linkedWallets[0].walletAddress
    $actualBeta = $linkedWallets[1].walletAddress
    Write-WtInfo "Using linked wallets: alpha=$actualAlpha, beta=$actualBeta"
}

# Step 5: Create register
Write-WtStep "Step 5: Create Register"
$register = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl `
    -Name "Org Ping-Pong Register" -Description "Register for org ping-pong walkthrough" `
    -TenantId $admin.OrganizationId -OwnerUserId $admin.AdminUserId `
    -OwnerWalletAddress $designerWallet.Address -Headers $admin.Headers

# Step 6: Publish blueprint
Write-WtStep "Step 6: Publish Blueprint"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "templates/ping-pong-template.json") `
    -WalletMap @{ "ping" = $actualAlpha; "pong" = $actualBeta } `
    -Headers $admin.Headers -IdPrefix "org-pingpong" `
    -RegisterId $register.RegisterId

# Save state
$state = @{
    profile        = $Profile
    organizationId = $admin.OrganizationId
    adminToken     = $admin.Token
    alphaWallet    = $actualAlpha
    betaWallet     = $actualBeta
    designerWallet = $designerWallet.Address
    registerId     = $register.RegisterId
    blueprintId    = $blueprint.BlueprintId
    blueprintUrl   = $env.BlueprintUrl
}

$stateFile = Join-Path $scriptDir "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete"
Write-WtInfo "Run: pwsh walkthroughs/OrganizationPingPong/run.ps1"
