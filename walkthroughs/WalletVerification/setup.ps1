#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# WalletVerification — Setup

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "WalletVerification — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "wallet-verify" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Wallet Verification Demo" `
    -OrgSubdomain "wallet-verify" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

$state = @{
    profile        = $Profile
    organizationId = $admin.OrganizationId
    adminUserId    = $admin.AdminUserId
    adminToken     = $admin.Token
    walletUrl      = $env.WalletUrl
    registerUrl    = $env.RegisterUrl
}

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete"
Write-WtInfo "Run: pwsh walkthroughs/WalletVerification/run.ps1"
