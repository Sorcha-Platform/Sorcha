#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# WalletVerification — Setup

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
