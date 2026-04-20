#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Setup
# Feature 107. Creates the Government of Scotland issuing organisation,
# provisions its trust anchor + HAIP issuer enrolment, creates the citizen
# public-org account, and publishes the AssuredIdentity blueprint.
# Idempotent — safe to run multiple times.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "assured-identity"
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Well-known public org id (platform-wide, used for citizen accounts).
$publicOrgId = "00000000-0000-0000-0000-000000000002"

if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists. Use -Force to re-run."
    return
}

# ============================================================================
# Step 1: Login as System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword
Write-WtSuccess "Connected (org: $($sysAdmin.OrganizationId))"

# ============================================================================
# Step 2: Enable Public Org + Register Users
# ============================================================================
Write-WtStep "Step 2: Enable Public Org and Register Users"

Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } `
    -Headers $sysAdmin.Headers
Write-WtInfo "Public org enabled"

$govAdminEmail    = "gov-admin@assured-identity.local"
$govAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $govAdminEmail `
    -Password $govAdminPassword `
    -DisplayName "Government Admin" | Out-Null
Write-WtInfo "gov-admin registered: $govAdminEmail"

$citizenEmail    = "alex.macleod@assured-identity.local"
$citizenPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $citizenEmail `
    -Password $citizenPassword `
    -DisplayName "Alex MacLeod" | Out-Null
Write-WtInfo "citizen registered: $citizenEmail"

# ============================================================================
# Step 3: Verify Emails (admin override — no SMTP)
# ============================================================================
Write-WtStep "Step 3: Verify User Emails"

$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers

foreach ($email in @($govAdminEmail, $citizenEmail)) {
    $user = $publicUsers.users | Where-Object { $_.email -eq $email } | Select-Object -First 1
    if ($user) {
        Confirm-SorchaUserEmail `
            -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $publicOrgId `
            -UserId $user.id `
            -Headers $sysAdmin.Headers
        Write-WtInfo "  verified: $email"
    }
}

# ============================================================================
# Step 4: Create Government of Scotland Org
# ============================================================================
Write-WtStep "Step 4: Create Government of Scotland"

$govOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Government of Scotland" `
    -Subdomain "gov-scotland" `
    -AdminEmail $govAdminEmail `
    -Headers $sysAdmin.Headers `
    -Description "Issues Assured Identity credentials to citizens"

$govOrgId = $govOrg.OrganizationId
Write-WtSuccess "Government org: $govOrgId"

# ============================================================================
# Step 5: Government Admin — Login, Wallet, Participant
# ============================================================================
Write-WtStep "Step 5: Government Admin — Login, Wallet, Participant"

$govSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $govAdminEmail `
    -Password $govAdminPassword `
    -OrganizationId $govOrgId

$govWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Government of Scotland Issuer" `
    -Headers $govSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Government wallet: $($govWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $govOrgId `
    -WalletAddress $govWallet.Address `
    -DisplayName "Government Assessor" `
    -Headers $govSession.Headers
Write-WtInfo "Government assessor participant registered"

# ============================================================================
# Step 5b: Citizen — Login, Wallet, Participant
# ============================================================================
Write-WtStep "Step 5b: Citizen — Login, Wallet, Participant"

$citizenSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $citizenEmail `
    -Password $citizenPassword `
    -OrganizationId $publicOrgId

$citizenWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Citizen Application Wallet" `
    -Headers $citizenSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Citizen wallet: $($citizenWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $publicOrgId `
    -WalletAddress $citizenWallet.Address `
    -DisplayName "Alex MacLeod" `
    -Headers $citizenSession.Headers
Write-WtInfo "Citizen participant registered in public org"

# ============================================================================
# Step 6: Provision Trust Anchor + Enrol as HAIP Issuer
# ============================================================================
Write-WtStep "Step 6: Trust Anchor + HAIP Issuer Enrolment"

$tenantId = $sysAdmin.OrganizationId

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/provision" `
        -Headers $sysAdmin.Headers `
        -Body @{}
    Write-WtSuccess "Trust anchor provisioned"
} catch { Write-WtWarn "Trust anchor may already exist" }

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($govWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $govWallet.PublicKey
            orgDisplayName     = "Government of Scotland"
        }
    Write-WtSuccess "Org cert enrolled"
} catch { Write-WtWarn "Enrolment may already exist" }

# ============================================================================
# Step 7: Create Register
# ============================================================================
Write-WtStep "Step 7: Create Register"

# DevMode — citizen is late-bound and never published to the register, so
# Action 2's credential delivery uses the plaintext path. Same security
# posture as HaipVerifiedCitizen (the source this walkthrough replaces).
$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Assured Identity Register" `
    -Description "Register for Feature 107 Assured Identity workflows" `
    -TenantId $govOrgId `
    -OwnerUserId $govSession.UserId `
    -OwnerWalletAddress $govWallet.Address `
    -Headers $govSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl `
    -DevMode
Write-WtSuccess "Register: $($register.RegisterId)"

try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $govOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Government Assessor" `
        -OrganizationName "Government of Scotland" `
        -WalletAddress $govWallet.Address `
        -PublicKey $govWallet.PublicKey `
        -Headers $govSession.Headers
} catch {
    Write-WtWarn "Government participant publish failed: $($_.Exception.Message)"
}

try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "Assured Identity Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
} catch {
    Write-WtWarn "Public org subscribe to identity register failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 8: Publish Blueprint
# ============================================================================
Write-WtStep "Step 8: Publish Blueprint"

# Open-participant contract: the 'citizen' participant is the sender of an
# isStartingAction: true action and therefore MUST NOT appear in the wallet
# map. Runtime late-binds whichever authenticated public-org user submits
# Action 1 as the bound citizen for that instance.
# See: .claude/skills/blueprint-builder/SKILL.md — "Open Participants & Late Binding"
$walletMap = @{
    "government-assessor" = $govWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime.
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/assured-identity.json") `
    -WalletMap $walletMap `
    -Headers $govSession.Headers `
    -IdPrefix "assured-identity" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    tenantUrl            = $sorchaEnv.TenantUrl
    blueprintUrl         = $sorchaEnv.BlueprintUrl
    walletUrl            = $sorchaEnv.WalletUrl
    registerUrl          = $sorchaEnv.RegisterUrl
    gatewayUrl           = $sorchaEnv.GatewayUrl
    tenantId             = $tenantId
    govOrgId             = $govOrgId
    govWalletAddress     = $govWallet.Address
    govWalletPublicKey   = $govWallet.PublicKey
    publicOrgId          = $publicOrgId
    citizenWalletAddress = $citizenWallet.Address
    registerId           = $register.RegisterId
    blueprintId          = $blueprint.BlueprintId
    walletDir            = (Join-Path $scriptDir "wallet")
    roles = @{
        govAssessor = @{
            email          = $govAdminEmail
            password       = $govAdminPassword
            organizationId = $govOrgId
            walletAddress  = $govWallet.Address
        }
        citizen = @{
            email          = $citizenEmail
            password       = $citizenPassword
            organizationId = $publicOrgId
            walletAddress  = $citizenWallet.Address
        }
    }
    persona = @{
        givenName  = "Alex"
        middleName = "Morgan"
        familyName = "MacLeod"
        fullName   = "Alex Morgan MacLeod"
        dateOfBirth = "1990-06-21"
        defaultEmail = $citizenEmail
        defaultAddress = @{
            street   = "12 Castle Street"
            locality = "Edinburgh"
            region   = "Lothian"
            postcode = "EH1 2DU"
            country  = "Scotland"
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "AssuredIdentity — Setup Complete"
