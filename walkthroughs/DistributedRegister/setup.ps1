#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# DistributedRegister — Setup
# Bootstrap org and create wallet on LOCAL machine.

param(
    # NO 'n1' — DistributedRegister is a LOCAL two-node test by design (this bootstraps on the
    # local machine and sync-test.ps1 compares a local gateway/peer against a remote one, using
    # hardcoded http://localhost endpoints). Adding 'n1' here would let the suite start a test
    # whose other half cannot target n1.
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "DistributedRegister — Setup (Local)"

$secrets = Get-SorchaSecrets -WalkthroughName "dist-register" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Distributed Register Demo" `
    -OrgSubdomain "dist-register" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

$wallet = New-SorchaWallet -WalletUrl $env.WalletUrl -Name "Dist Register Wallet" -Headers $admin.Headers

$state = @{
    profile        = $Profile
    organizationId = $admin.OrganizationId
    adminUserId    = $admin.AdminUserId
    adminToken     = $admin.Token
    walletAddress  = $wallet.Address
    tenantUrl      = $env.TenantUrl
    registerUrl    = $env.RegisterUrl
    walletUrl      = $env.WalletUrl
    blueprintUrl   = $env.BlueprintUrl
}

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete"
Write-WtInfo "Run: pwsh walkthroughs/DistributedRegister/run.ps1 -RemoteHost <IP>"
