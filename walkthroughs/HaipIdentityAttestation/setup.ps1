#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipIdentityAttestation — Setup
# Creates a Government Identity Authority org with admin user and wallet.
# Provisions trust anchor and enrols the Government org as a HAIP issuer.
# Creates a citizen user with persona data.
#
# Follows the same setup pattern as ConstructionPermit.
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

Write-WtBanner "HaipIdentityAttestation — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "haip-identity"
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Well-known public org ID
$publicOrgId = "00000000-0000-0000-0000-000000000002"

# Skip if already set up
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

# Register Government admin on public org
$govAdminEmail = "gov-admin@haip-walkthrough.local"
$govAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $govAdminEmail `
    -Password $govAdminPassword `
    -DisplayName "Government Admin" | Out-Null
Write-WtInfo "gov-admin registered: $govAdminEmail"

# Register citizen on public org
$citizenEmail = "alice.obrien@haip-walkthrough.local"
$citizenPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $citizenEmail `
    -Password $citizenPassword `
    -DisplayName "Alice O'Brien" | Out-Null
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
# Step 4: Create Government Identity Authority Org
# ============================================================================
Write-WtStep "Step 4: Create Government Identity Authority"

$govOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Name "Government Identity Authority" `
    -Subdomain "gov-identity" `
    -AdminEmail $govAdminEmail `
    -Headers $sysAdmin.Headers `
    -Description "Issues verified identity credentials to citizens"

$govOrgId = $govOrg.OrganizationId
Write-WtSuccess "Government org: $govOrgId"

# ============================================================================
# Step 5: Per-Role Setup (login, wallet, participant)
# ============================================================================
Write-WtStep "Step 5: Government Admin — Login, Wallet, Participant"

$govSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $govAdminEmail `
    -Password $govAdminPassword `
    -OrganizationId $govOrgId

$govWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Government Identity Issuer" `
    -Headers $govSession.Headers `
    -FetchPublicKey

Write-WtSuccess "Government wallet: $($govWallet.Address)"

# Register participant + link wallet
$govParticipant = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $govOrgId `
    -WalletAddress $govWallet.Address `
    -DisplayName "Government Assessor" `
    -Headers $govSession.Headers
Write-WtInfo "Government assessor participant registered"

# ============================================================================
# Step 5b: Citizen — Login on Public Org, Wallet, Participant
# ============================================================================
# The citizen submits Action 1 of the blueprint (their own identity application)
# from inside the platform. They need their own wallet under their own user
# token so the action is signed by them — not by the government assessor or
# the system admin. The verifiable credential issued by Action 2 still goes
# to their EXTERNAL HAIP wallet via QR; this in-platform wallet is only used
# to sign the in-platform application submission.
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
    -DisplayName "Alice O'Brien" `
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
            orgDisplayName = "Government Identity Authority"
        }
    Write-WtSuccess "Org cert enrolled"
} catch { Write-WtWarn "Enrolment may already exist" }

# ============================================================================
# Step 7: Create Register
# ============================================================================
Write-WtStep "Step 7: Create Register"

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "HAIP Identity Register" `
    -Description "Register for HAIP identity attestation workflows" `
    -TenantId $govOrgId `
    -OwnerUserId $govSession.UserId `
    -OwnerWalletAddress $govWallet.Address `
    -Headers $govSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl

Write-WtSuccess "Register: $($register.RegisterId)"

# Subscribe the public org so the citizen can see the register and submit
# Action 1 from their own session. Owner subscription for the gov org is
# created server-side by Register Service finalize (PR #258).
try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "HAIP Identity Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
} catch {
    Write-WtWarn "Public org subscribe to identity register failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 8: Publish Blueprint
# ============================================================================
Write-WtStep "Step 8: Publish Blueprint"

$walletMap = @{
    "government-assessor" = $govWallet.Address
    "citizen"             = $citizenWallet.Address
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/identity-attestation.json") `
    -WalletMap $walletMap `
    -Headers $govSession.Headers `
    -IdPrefix "haip-identity" `
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
    govOrgId     = $govOrgId
    govWalletAddress = $govWallet.Address
    govWalletPublicKey = $govWallet.PublicKey
    publicOrgId  = $publicOrgId
    citizenWalletAddress = $citizenWallet.Address
    registerId   = $register.RegisterId
    blueprintId  = $blueprint.BlueprintId
    # walletDir is captured here at setup time so downstream walkthroughs
    # (HaipDrivingLicence) don't read a null when they pull in this state file.
    walletDir    = (Join-Path $scriptDir "wallet")
    roles = @{
        # govAdmin retained as an alias so older state.json consumers continue
        # to load — this is the same record as govAssessor below.
        govAdmin = @{
            email = $govAdminEmail
            password = $govAdminPassword
            organizationId = $govOrgId
            walletAddress = $govWallet.Address
        }
        govAssessor = @{
            email = $govAdminEmail
            password = $govAdminPassword
            organizationId = $govOrgId
            walletAddress = $govWallet.Address
        }
        citizen = @{
            email = $citizenEmail
            password = $citizenPassword
            organizationId = $publicOrgId
            walletAddress = $citizenWallet.Address
        }
    }
    persona = @{
        givenName = "Alice"
        familyName = "O'Brien"
        fullName = "Alice O'Brien"
        dateOfBirth = "1990-03-15"
        defaultEmail = $citizenEmail
        defaultAddress = @{
            street = "42 Grafton Street"
            locality = "Dublin"
            region = "Leinster"
            postcode = "D02 Y1K8"
            country = "Ireland"
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipIdentityAttestation — Setup Complete"
