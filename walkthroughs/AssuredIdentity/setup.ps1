#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Setup
# Feature 107 + Feature 124. Creates the Acme Verification Co. issuing
# organisation, provisions its trust anchor + HAIP issuer enrolment,
# creates the citizen public-org account, and publishes the AssuredIdentity
# blueprint (now configured to deliver into the Citizen Wallet PWA via
# SorchaLocalWallet target audience).
#
# After setup, sign the citizen into the wallet host once on the demo
# device: open http://localhost/wallet/, tap Settings, sign in with the
# citizen credentials printed at the end of this script.
#
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

$secrets = Get-SorchaSecrets -WalkthroughName "assured-identity" -Profile $Profile
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

$verificationAdminEmail    = "verification-admin@assured-identity.local"
$verificationAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAdminEmail `
    -Password $verificationAdminPassword `
    -DisplayName "Verification Admin" | Out-Null
Write-WtInfo "verification-admin registered: $verificationAdminEmail"

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

foreach ($email in @($verificationAdminEmail, $citizenEmail)) {
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
# Step 4: Create Acme Verification Co. Org
# ============================================================================
Write-WtStep "Step 4: Create Acme Verification Co."

$verificationOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Acme Verification Co." `
    -Subdomain "acme-verification" `
    -AdminEmail $verificationAdminEmail `
    -Headers $sysAdmin.Headers `
    -Description "Issues Assured Identity credentials to citizens"

$verificationOrgId = $verificationOrg.OrganizationId
Write-WtSuccess "Verification org: $verificationOrgId"

# ============================================================================
# Step 5: Verification Admin — Login, Wallet, Participant
# ============================================================================
Write-WtStep "Step 5: Verification Admin — Login, Wallet, Participant"

$verificationSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAdminEmail `
    -Password $verificationAdminPassword `
    -OrganizationId $verificationOrgId

$verificationWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Verification Co. Issuer" `
    -Headers $verificationSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Verification wallet: $($verificationWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $verificationOrgId `
    -WalletAddress $verificationWallet.Address `
    -DisplayName "Verification Analyst" `
    -Headers $verificationSession.Headers
Write-WtInfo "Verification analyst participant registered"

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
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($verificationWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $verificationWallet.PublicKey
            orgDisplayName     = "Acme Verification Co."
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
    -TenantId $verificationOrgId `
    -OwnerUserId $verificationSession.UserId `
    -OwnerWalletAddress $verificationWallet.Address `
    -Headers $verificationSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl `
    -DevMode
Write-WtSuccess "Register: $($register.RegisterId)"

try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $verificationOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Verification Analyst" `
        -OrganizationName "Acme Verification Co." `
        -WalletAddress $verificationWallet.Address `
        -PublicKey $verificationWallet.PublicKey `
        -Headers $verificationSession.Headers
} catch {
    Write-WtWarn "Verification analyst participant publish failed: $($_.Exception.Message)"
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
    "verification-analyst" = $verificationWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime.
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/assured-identity.json") `
    -WalletMap $walletMap `
    -Headers $verificationSession.Headers `
    -IdPrefix "assured-identity" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Step 9: Acme Licensing Co. — Feature 107 PR 2 (US2)
# ============================================================================
Write-WtStep "Step 9: Create Acme Licensing Co."

$licensingAdminEmail    = "licensing-admin@assured-identity.local"
$licensingAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $licensingAdminEmail `
    -Password $licensingAdminPassword `
    -DisplayName "Licensing Admin" | Out-Null

$publicUsersAfterLicensing = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers
$licensingPublicUser = $publicUsersAfterLicensing.users | Where-Object { $_.email -eq $licensingAdminEmail } | Select-Object -First 1
if ($licensingPublicUser) {
    Confirm-SorchaUserEmail `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -UserId $licensingPublicUser.id `
        -Headers $sysAdmin.Headers
}

$licensingOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Acme Licensing Co." `
    -Subdomain "acme-licensing" `
    -AdminEmail $licensingAdminEmail `
    -Headers $sysAdmin.Headers `
    -Description "Issues Driving Licence credentials to citizens holding an AssuredIdentityCredential"
$licensingOrgId = $licensingOrg.OrganizationId
Write-WtSuccess "Licensing org: $licensingOrgId"

$licensingSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $licensingAdminEmail `
    -Password $licensingAdminPassword `
    -OrganizationId $licensingOrgId

$licensingWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Licensing Co. Issuer" `
    -Headers $licensingSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Licensing wallet: $($licensingWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $licensingOrgId `
    -WalletAddress $licensingWallet.Address `
    -DisplayName "Licensing Officer" `
    -Headers $licensingSession.Headers

# Licensing org as HAIP issuer too — trust anchor already provisioned for the tenant,
# enrol the licensing org cert alongside Acme Verification Co.
try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($licensingWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $licensingWallet.PublicKey
            orgDisplayName     = "Acme Licensing Co."
        }
    Write-WtSuccess "Licensing org cert enrolled"
} catch { Write-WtWarn "Licensing enrolment may already exist" }

# Reuse the Assured Identity register — Feature 107 design keeps both
# credentials on a single canonical register for the walkthrough so the
# citizen's wallet stays coherent.

try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $licensingOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Licensing Officer" `
        -OrganizationName "Acme Licensing Co." `
        -WalletAddress $licensingWallet.Address `
        -PublicKey $licensingWallet.PublicKey `
        -Headers $licensingSession.Headers
} catch {
    Write-WtWarn "Licensing participant publish failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 10: Publish Driving Licence Blueprint
# ============================================================================
Write-WtStep "Step 10: Publish Driving Licence Blueprint"

$licenceWalletMap = @{
    "licensing-officer" = $licensingWallet.Address
    # "citizen" intentionally absent — late-bound per Feature 103 open-participant contract.
}

$licenceBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/driving-licence.json") `
    -WalletMap $licenceWalletMap `
    -Headers $licensingSession.Headers `
    -IdPrefix "driving-licence" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Driving Licence blueprint: $($licenceBlueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    tenantUrl                 = $sorchaEnv.TenantUrl
    blueprintUrl              = $sorchaEnv.BlueprintUrl
    walletUrl                 = $sorchaEnv.WalletUrl
    registerUrl               = $sorchaEnv.RegisterUrl
    gatewayUrl                = $sorchaEnv.GatewayUrl
    tenantId                  = $tenantId
    verificationOrgId         = $verificationOrgId
    verificationWalletAddress = $verificationWallet.Address
    verificationWalletPublicKey = $verificationWallet.PublicKey
    publicOrgId               = $publicOrgId
    citizenWalletAddress      = $citizenWallet.Address
    registerId                = $register.RegisterId
    blueprintId               = $blueprint.BlueprintId
    # walletDir removed in Feature 124 — credential delivery now lands in the
    # Citizen Wallet PWA (SorchaLocalWallet target audience), not in a
    # filesystem wallet on the operator host.
    # PR 2 additions — licensing org, licensing wallet, Driving Licence blueprint id.
    licensingOrgId            = $licensingOrgId
    licensingWalletAddress    = $licensingWallet.Address
    licensingWalletPublicKey  = $licensingWallet.PublicKey
    licenceBlueprintId        = $licenceBlueprint.BlueprintId
    roles = @{
        verificationAnalyst = @{
            email          = $verificationAdminEmail
            password       = $verificationAdminPassword
            organizationId = $verificationOrgId
            walletAddress  = $verificationWallet.Address
        }
        citizen = @{
            email          = $citizenEmail
            password       = $citizenPassword
            organizationId = $publicOrgId
            walletAddress  = $citizenWallet.Address
        }
        licensingOfficer = @{
            email          = $licensingAdminEmail
            password       = $licensingAdminPassword
            organizationId = $licensingOrgId
            walletAddress  = $licensingWallet.Address
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

# Feature 124 — surface the citizen credentials so the operator can sign
# the demo citizen into the PWA host before running phase 1.
Write-WtInfo ""
Write-WtInfo "Next step: sign the citizen into the wallet host."
Write-WtInfo "  URL:       http://localhost/wallet/ (or your gateway equivalent)"
Write-WtInfo "  Settings → Sign in"
Write-WtInfo "  Email:     $citizenEmail"
Write-WtInfo "  Password:  $citizenPassword"
Write-WtInfo ""
Write-WtInfo "Then enrol the device on the wallet PWA. In two terminals:"
Write-WtInfo "  T1> pwsh walkthroughs/AssuredIdentity/run-agents.ps1"
Write-WtInfo "  T2> pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1"
Write-WtInfo ""

Write-WtBanner "AssuredIdentity — Setup Complete"
