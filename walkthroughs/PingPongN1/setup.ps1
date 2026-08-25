#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PingPongN1 — Setup
#
# Provisions the two orgs / two wallets / one public register that the run.ps1
# uses to drive a cross-machine ping-pong:
#
#   Local (Phaethon)        n1 (n1.sorcha.dev)
#   ┌──────────────┐        ┌──────────────┐
#   │   Ping Labs  │── R ─▶│   Pong Corp  │
#   │  wallet W_p  │        │  wallet W_q  │
#   └──────────────┘        └──────────────┘
#
# Idempotent — safe to re-run; existing orgs/wallets/registers are reused.
# Requires the platform seed admin (admin@sorcha.local) on both machines.

param(
    [string]$LocalProfile = 'gateway',
    [string]$N1Profile    = 'n1',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PingPongN1 — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "ping-pong-n1"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists. Use -Force to re-run."
    return
}

# ============================================================================
# Step 1: Prove both machines are healthy
# ============================================================================
Write-WtStep "Step 1: Health check both nodes"

$localEnv = Initialize-SorchaEnvironment -Profile $LocalProfile -SkipHealthCheck:$SkipHealthCheck
$n1Env    = Initialize-SorchaEnvironment -Profile $N1Profile    -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Step 2: Login as platform System Admin on both machines
# ============================================================================
Write-WtStep "Step 2: Login as System Admin (local + n1)"

$localAdmin = Connect-SorchaAdmin `
    -TenantUrl $localEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword
Write-WtSuccess "Local admin org: $($localAdmin.OrganizationId)"

$n1Admin = Connect-SorchaAdmin `
    -TenantUrl $n1Env.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword
Write-WtSuccess "n1 admin org: $($n1Admin.OrganizationId)"

# ============================================================================
# Step 3: Create Ping Labs (local) and Pong Corp (n1)
# ============================================================================
Write-WtStep "Step 3: Create Ping Labs (local) and Pong Corp (n1)"

# The seed admin is already a PlatformUser on both nodes, so passing
# admin@sorcha.local makes them an Administrator of each new org directly
# (no invitation required).
$pingOrg = New-SorchaOrganization `
    -TenantUrl $localEnv.TenantUrl `
    -WalletUrl $localEnv.WalletUrl `
    -Name "Ping Labs" `
    -Subdomain "ping-labs" `
    -AdminEmail $secrets.adminEmail `
    -Headers $localAdmin.Headers `
    -Description "Local-side org owning the ping-pong register"

$pongOrg = New-SorchaOrganization `
    -TenantUrl $n1Env.TenantUrl `
    -WalletUrl $n1Env.WalletUrl `
    -Name "Pong Corp" `
    -Subdomain "pong-corp" `
    -AdminEmail $secrets.adminEmail `
    -Headers $n1Admin.Headers `
    -Description "n1-side org replying to pings"

# ============================================================================
# Step 4: Switch into each org and provision a signing wallet
# ============================================================================
Write-WtStep "Step 4: Provision Ping Labs wallet (local)"

$pingSession = Switch-SorchaOrganization `
    -TenantUrl $localEnv.TenantUrl `
    -OrganizationId $pingOrg.OrganizationId `
    -Headers $localAdmin.Headers

$pingWallet = New-SorchaWallet `
    -WalletUrl $localEnv.WalletUrl `
    -Name "Ping Labs Signing Wallet" `
    -Headers $pingSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Ping Labs wallet: $($pingWallet.Address)"

Write-WtStep "Step 5: Provision Pong Corp wallet (n1)"

$pongSession = Switch-SorchaOrganization `
    -TenantUrl $n1Env.TenantUrl `
    -OrganizationId $pongOrg.OrganizationId `
    -Headers $n1Admin.Headers

$pongWallet = New-SorchaWallet `
    -WalletUrl $n1Env.WalletUrl `
    -Name "Pong Corp Signing Wallet" `
    -Headers $pongSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Pong Corp wallet: $($pongWallet.Address)"

# ============================================================================
# Step 6: Persist state for run.ps1
# ============================================================================
Write-WtStep "Step 6: Persist state.json"

$state = @{
    createdAt = (Get-Date).ToString('o')
    local = @{
        profile         = $LocalProfile
        gatewayUrl      = $localEnv.GatewayUrl
        tenantUrl       = $localEnv.TenantUrl
        blueprintUrl    = $localEnv.BlueprintUrl
        registerUrl     = $localEnv.RegisterUrl
        walletUrl       = $localEnv.WalletUrl
        adminEmail      = $secrets.adminEmail
        sysAdminOrgId   = $localAdmin.OrganizationId
        pingOrgId       = $pingOrg.OrganizationId
        pingOrgName     = "Ping Labs"
        pingWallet      = $pingWallet.Address
        pingPublicKey   = $pingWallet.PublicKey
    }
    n1 = @{
        profile         = $N1Profile
        gatewayUrl      = $n1Env.GatewayUrl
        tenantUrl       = $n1Env.TenantUrl
        blueprintUrl    = $n1Env.BlueprintUrl
        registerUrl     = $n1Env.RegisterUrl
        walletUrl       = $n1Env.WalletUrl
        adminEmail      = $secrets.adminEmail
        sysAdminOrgId   = $n1Admin.OrganizationId
        pongOrgId       = $pongOrg.OrganizationId
        pongOrgName     = "Pong Corp"
        pongWallet      = $pongWallet.Address
        pongPublicKey   = $pongWallet.PublicKey
    }
}

$state | ConvertTo-Json -Depth 6 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "state.json written: $stateFile"

Write-Host ""
Write-WtBanner "Setup complete"
Write-Host "  Ping Labs:    $($pingOrg.OrganizationId)" -ForegroundColor White
Write-Host "  Ping wallet:  $($pingWallet.Address)" -ForegroundColor White
Write-Host "  Pong Corp:    $($pongOrg.OrganizationId)" -ForegroundColor White
Write-Host "  Pong wallet:  $($pongWallet.Address)" -ForegroundColor White
Write-Host ""
Write-Host "Next: pwsh walkthroughs/PingPongN1/run.ps1" -ForegroundColor Cyan
