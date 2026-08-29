#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# RegisterCreationFlow — Setup
# Bootstrap org and create wallet for register owner.

param(
    # 'n1' resolves through Initialize-SorchaEnvironment like every other profile. It was missing
    # here only because these scripts predate that profile, and PowerShell rejects the argument
    # before any code runs — so run-all.ps1 reported them as 1-second FAILURES that had in fact
    # never executed. A suite that cannot run a script must not report it as a failing one.
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "RegisterCreationFlow — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "register-demo" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# Bootstrap org
Write-WtStep "Step 1: Bootstrap Organization"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Register Creation Demo" `
    -OrgSubdomain "register-demo" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

# Create wallet
Write-WtStep "Step 2: Create Owner Wallet"
$wallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Register Owner Wallet" -Headers $admin.Headers

# Save state
$state = @{
    profile        = $Profile
    organizationId = $admin.OrganizationId
    adminUserId    = $admin.AdminUserId
    adminToken     = $admin.Token
    walletAddress  = $wallet.Address
    tenantUrl      = $env.TenantUrl
    registerUrl    = $env.RegisterUrl
    walletUrl      = $env.WalletUrl
}

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — state saved"
Write-WtInfo "Run: pwsh walkthroughs/RegisterCreationFlow/run.ps1"
