#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipDrivingLicence — Setup
# Creates a Council Licensing Authority org. Checks for identity credential
# from HaipIdentityAttestation (runs it inline if missing).

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HaipDrivingLicence — Setup"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
$identityDir = Join-Path (Split-Path -Parent $scriptDir) "HaipIdentityAttestation"
$identityState = Join-Path $identityDir "state.json"

# Skip if already set up
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists. Use -Force to re-run."
    return
}

# ============================================================================
# Step 1: Ensure identity attestation is complete
# ============================================================================
Write-WtStep "Step 1: Check Identity Credential"

if (-not (Test-Path $identityState)) {
    Write-WtWarn "HaipIdentityAttestation not run — running it now"
    & (Join-Path $identityDir "setup.ps1") -Profile $Profile -SkipHealthCheck:$SkipHealthCheck
    & (Join-Path $identityDir "run.ps1")
}

$idState = Get-Content -Path $identityState -Raw | ConvertFrom-Json
Write-WtSuccess "Identity credential available"

$secrets = Get-SorchaSecrets -WalkthroughName "haip-licence"
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Step 2: Login as System Admin
# ============================================================================
Write-WtStep "Step 2: Login as System Admin"

$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

# ============================================================================
# Step 3: Register Council Admin + Create Org
# ============================================================================
Write-WtStep "Step 3: Create Council Licensing Authority"

$publicOrgId = "00000000-0000-0000-0000-000000000002"
$councilAdminEmail = "council-admin@haip-walkthrough.local"
$councilAdminPassword = $secrets.DefaultPassword

# Register on public org
Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $councilAdminEmail `
    -Password $councilAdminPassword `
    -DisplayName "Council Admin" | Out-Null

# Verify email
$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers
$councilUser = $publicUsers.users | Where-Object { $_.email -eq $councilAdminEmail } | Select-Object -First 1
if ($councilUser) {
    Confirm-SorchaUserEmail `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -UserId $councilUser.id `
        -Headers $sysAdmin.Headers
}

# Create org
$councilOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Council Licensing Authority" `
    -Subdomain "council-licence" `
    -AdminEmail $councilAdminEmail `
    -Headers $sysAdmin.Headers `
    -Description "Issues driving licences after identity verification"

$councilOrgId = $councilOrg.OrganizationId
Write-WtSuccess "Council org: $councilOrgId"

# ============================================================================
# Step 4: Council Admin — Login, Wallet, Participant
# ============================================================================
Write-WtStep "Step 4: Council Admin Setup"

$councilSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $councilAdminEmail `
    -Password $councilAdminPassword `
    -OrganizationId $councilOrgId

$councilWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Council Licence Issuer" `
    -Headers $councilSession.Headers `
    -FetchPublicKey

Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $councilOrgId `
    -WalletAddress $councilWallet.Address `
    -DisplayName "Council Admin" `
    -Headers $councilSession.Headers

Write-WtSuccess "Council wallet: $($councilWallet.Address)"

# ============================================================================
# Step 5: Enrol Council as HAIP Issuer
# ============================================================================
Write-WtStep "Step 5: Enrol Council as HAIP Issuer"

$tenantId = $idState.tenantId  # Reuse tenant from identity walkthrough

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($councilWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $councilWallet.PublicKey
            orgDisplayName = "Council Licensing Authority"
        }
    Write-WtSuccess "Council cert enrolled"
} catch { Write-WtWarn "Enrolment may already exist" }

# ============================================================================
# Save State
# ============================================================================
$state = @{
    tenantUrl    = $sorchaEnv.TenantUrl
    blueprintUrl = $sorchaEnv.BlueprintUrl
    walletUrl    = $sorchaEnv.WalletUrl
    gatewayUrl   = $sorchaEnv.GatewayUrl
    tenantId     = $tenantId
    councilOrgId = $councilOrgId
    councilWalletAddress = $councilWallet.Address
    identityStateFile = $identityState
    walletDir    = $idState.walletDir
    roles = @{
        councilAdmin = @{
            email = $councilAdminEmail
            password = $councilAdminPassword
            organizationId = $councilOrgId
            walletAddress = $councilWallet.Address
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipDrivingLicence — Setup Complete"
