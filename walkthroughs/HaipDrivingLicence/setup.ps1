#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipDrivingLicence — Setup
# Creates a Council Licensing Authority org. Checks for identity credential
# from HaipVerifiedCitizen (runs it inline if missing).

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
$identityDir = Join-Path (Split-Path -Parent $scriptDir) "HaipVerifiedCitizen"
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
    Write-WtWarn "HaipVerifiedCitizen not run — running it now"
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
# Step 6: Create Register
# ============================================================================
Write-WtStep "Step 6: Create Register"

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "HAIP Driving Licence Register" `
    -Description "Register for HAIP driving licence workflows" `
    -TenantId $councilOrgId `
    -OwnerUserId $councilSession.UserId `
    -OwnerWalletAddress $councilWallet.Address `
    -Headers $councilSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl

Write-WtSuccess "Register: $($register.RegisterId)"

# Subscribe the public org so the citizen applicant can submit Action 1
# from their own session.
try {
    $publicOrgId = "00000000-0000-0000-0000-000000000002"
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "HAIP Driving Licence Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
} catch {
    Write-WtWarn "Public org subscribe to licence register failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 7: Publish Blueprint
# ============================================================================
Write-WtStep "Step 7: Publish Blueprint"

if (-not $idState.citizenWalletAddress) {
    Write-WtFail "HaipVerifiedCitizen state.json has no citizenWalletAddress — re-run that setup first."
    exit 1
}

$walletMap = @{
    "council"   = $councilWallet.Address
    # The applicant is the same citizen from HaipVerifiedCitizen. Using
    # their actual wallet (not a placeholder of the council wallet) means
    # Action 1 of the licence blueprint is correctly signed by the citizen
    # under their own user token in run.ps1, and the in-platform audit trail
    # ties the application back to the real applicant. The licence credential
    # itself is still delivered to their EXTERNAL HAIP wallet via QR.
    "applicant" = $idState.citizenWalletAddress
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/driving-licence.json") `
    -WalletMap $walletMap `
    -Headers $councilSession.Headers `
    -IdPrefix "haip-licence" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    tenantUrl    = $sorchaEnv.TenantUrl
    blueprintUrl = $sorchaEnv.BlueprintUrl
    walletUrl    = $sorchaEnv.WalletUrl
    registerUrl  = $sorchaEnv.RegisterUrl
    gatewayUrl   = $sorchaEnv.GatewayUrl
    tenantId     = $tenantId
    councilOrgId = $councilOrgId
    councilWalletAddress = $councilWallet.Address
    applicantWalletAddress = $idState.citizenWalletAddress
    registerId   = $register.RegisterId
    blueprintId  = $blueprint.BlueprintId
    identityStateFile = $identityState
    walletDir    = $idState.walletDir
    roles = @{
        councilAdmin = @{
            email = $councilAdminEmail
            password = $councilAdminPassword
            organizationId = $councilOrgId
            walletAddress = $councilWallet.Address
        }
        # The applicant is the citizen from HaipVerifiedCitizen. We
        # reach back into that walkthrough's state to pull their credentials
        # and org so run.ps1 can authenticate as them and sign Action 1
        # under their own user token.
        applicant = @{
            email          = $idState.roles.citizen.email
            password       = $idState.roles.citizen.password
            organizationId = $idState.roles.citizen.organizationId
            walletAddress  = $idState.citizenWalletAddress
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipDrivingLicence — Setup Complete"
