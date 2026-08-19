#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HealthDeclaration — Demo Setup
# Single org, single participant. Bootstraps a demo clinic, creates one patient
# user with a wallet, creates one register, and publishes the health declaration
# blueprint exercising x-pages, x-sections, x-rule, x-introduction, and x-width.
#
# Use this walkthrough to demo Feature 091 layout extensions on any node.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HealthDeclaration — Demo Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "health-declaration" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Pre-flight: validate existing state unless -Force
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtStep "Validating existing state.json"
    $existingState = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
    $stateValid = $true

    try {
        $testLogin = Invoke-SorchaApi -Method POST `
            -Uri "$($existingState.tenantUrl)/auth/login" `
            -Body @{ email = $existingState.patient.email; password = $existingState.patient.password }
        if (-not $testLogin.requires_org_selection -and -not $testLogin.access_token) {
            $stateValid = $false
        }
    } catch {
        $stateValid = $false
    }

    if ($stateValid) {
        Write-WtSuccess "Existing state is valid — skipping setup (use -Force to recreate)"
        Write-WtInfo "Visit: $($env.GatewayUrl)/app/new-submissions"
        exit 0
    }
    Write-WtWarn "State invalid — running full setup"
}

# ============================================================================
# Step 1: System Admin Login
# ============================================================================
Write-WtStep "Step 1: System Admin Login"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Health Declaration System" `
    -OrgSubdomain "health-sys" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

# ============================================================================
# Step 1.5: Enable Public Org (required for self-registration)
# ============================================================================
Write-WtStep "Step 1.5: Enable Public Org for User Registration"
try {
    Invoke-SorchaApi -Method PUT `
        -Uri "$($env.TenantUrl)/platform/settings/public-org" `
        -Body @{ enabled = $true } `
        -Headers $sysAdmin.Headers | Out-Null
    Write-WtInfo "  Public org enabled for self-registration"
} catch {
    Write-WtInfo "  Public org may already be enabled (continuing)"
}

# ============================================================================
# Step 2: Register Patient and Clinician Users on Public Org
# ============================================================================
Write-WtStep "Step 2: Register Patient and Clinician Users"

$clinicianEmail    = "clinician@health-demo.local"
$clinicianPassword = $secrets.patientPassword

foreach ($user in @(
    @{ Email = $secrets.patientEmail; Name = $secrets.patientName },
    @{ Email = $clinicianEmail;       Name = "Demo Clinician" }
)) {
    try {
        Register-SorchaPublicUser `
            -TenantUrl $env.TenantUrl `
            -Email $user.Email `
            -DisplayName $user.Name `
            -Password $secrets.patientPassword
        Write-WtInfo "Registered: $($user.Email)"
    } catch {
        if ($_.Exception.Message -match '409|already exists|duplicate') {
            Write-WtInfo "Already registered: $($user.Email)"
        } else { throw }
    }

    try {
        Invoke-SorchaApi -Method POST `
            -Uri "$($env.TenantUrl)/platform/users/verify-email" `
            -Body @{ email = $user.Email } `
            -Headers $sysAdmin.Headers | Out-Null
    } catch { }
}

# ============================================================================
# Step 3: Create Clinic Org
# ============================================================================
Write-WtStep "Step 3: Create Demo Clinic Organization"

try {
    $clinicOrg = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -Name "Demo Clinic" `
        -Subdomain "health-demo" `
        -AdminEmail $secrets.patientEmail `
        -AdminPassword $secrets.patientPassword `
        -Headers $sysAdmin.Headers
} catch {
    if ($_.Exception.Message -match 'already taken|409|duplicate|400') {
        $orgs = (Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $sysAdmin.Headers)
        $existing = $orgs | Where-Object { $_.name -eq "Demo Clinic" -or $_.subdomain -eq "health-demo" } | Select-Object -First 1
        if (-not $existing) { $existing = ($orgs.items | Where-Object { $_.name -eq "Demo Clinic" -or $_.subdomain -eq "health-demo" }) | Select-Object -First 1 }
        $clinicOrg = @{ OrganizationId = $existing.id ?? $existing.organizationId }
        Write-WtInfo "Demo Clinic exists: $($clinicOrg.OrganizationId)"
    } else { throw }
}
Write-WtInfo "Demo Clinic Org: $($clinicOrg.OrganizationId)"

# ============================================================================
# Step 3.5: Add clinician to Demo Clinic as an Administrator
# ============================================================================
Write-WtStep "Step 3.5: Add Clinician to Demo Clinic"
Get-OrCreateUser `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $clinicOrg.OrganizationId `
    -Email $clinicianEmail `
    -DisplayName "Demo Clinician" `
    -Headers $sysAdmin.Headers `
    -Roles @("Administrator", "Consumer") | Out-Null
Write-WtInfo "Clinician added to Demo Clinic"

# ============================================================================
# Step 4: Login and create wallets + participants for both users
# ============================================================================
Write-WtStep "Step 4: Create Wallets & Participants"

$patientAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $secrets.patientEmail `
    -Password $secrets.patientPassword `
    -OrganizationId $clinicOrg.OrganizationId

$patientWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Patient Wallet" `
    -Headers $patientAuth.Headers `
    -FetchPublicKey

$patientParticipant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $clinicOrg.OrganizationId `
    -WalletAddress $patientWallet.Address `
    -DisplayName "Patient" `
    -Headers $patientAuth.Headers
Write-WtSuccess "Patient: $($patientWallet.Address)"

$clinicianAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $clinicianEmail `
    -Password $clinicianPassword `
    -OrganizationId $clinicOrg.OrganizationId

$clinicianWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Clinician Wallet" `
    -Headers $clinicianAuth.Headers `
    -FetchPublicKey

$clinicianParticipant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $clinicOrg.OrganizationId `
    -WalletAddress $clinicianWallet.Address `
    -DisplayName "Clinician" `
    -Headers $clinicianAuth.Headers
Write-WtSuccess "Clinician: $($clinicianWallet.Address)"

# ============================================================================
# Step 5: Create Register (idempotent — reuse existing one if present)
# ============================================================================
Write-WtStep "Step 5: Create or Reuse Register"

$registerName = "Health Declarations Register"
$existingRegisterId = Get-SorchaRegisterByName `
    -TenantUrl $env.TenantUrl `
    -Name $registerName `
    -Headers $patientAuth.Headers

if ($existingRegisterId) {
    Write-WtInfo "Reusing existing register '$registerName': $existingRegisterId"
    $register = @{ RegisterId = $existingRegisterId }
} else {
    $register = New-SorchaRegister `
        -RegisterUrl $env.RegisterUrl `
        -WalletUrl $env.WalletUrl `
        -Name $registerName `
        -Description "Register holding patient health declarations submitted before treatment" `
        -TenantId $clinicOrg.OrganizationId `
        -OwnerUserId $patientAuth.UserId `
        -OwnerWalletAddress $patientWallet.Address `
        -Headers $patientAuth.Headers
    Write-WtSuccess "Register created: $($register.RegisterId)"

    # Explicitly subscribe Demo Clinic as Owner so the register appears in
    # /api/me/subscribed-registers for both patient and clinician.
    try {
        New-SorchaRegisterSubscription `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $clinicOrg.OrganizationId `
            -RegisterId $register.RegisterId `
            -RegisterName $registerName `
            -Headers $patientAuth.Headers `
            -SubscriptionType "Owner" | Out-Null
        Write-WtInfo "Demo Clinic subscribed to register as Owner"
    } catch {
        if ($_.Exception.Message -match '409|already exists|duplicate') {
            Write-WtInfo "Demo Clinic already subscribed (skipping)"
        } else {
            Write-WtWarn "Subscription failed: $($_.Exception.Message)"
        }
    }
}

# Public org subscription so consumer users see the register in their list.
$null = Add-SorchaPublicOrgSubscription `
    -TenantUrl $env.TenantUrl `
    -RegisterId $register.RegisterId `
    -RegisterName $registerName `
    -SysAdminHeaders $sysAdmin.Headers `
    -SysAdminEmail $secrets.adminEmail

# Publish both participants to register
Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $clinicOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Patient" `
    -OrganizationName "Demo Clinic" `
    -WalletAddress $patientWallet.Address `
    -PublicKey $patientWallet.PublicKey `
    -Headers $patientAuth.Headers
Write-WtInfo "Patient participant published"

Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $clinicOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Clinician" `
    -OrganizationName "Demo Clinic" `
    -WalletAddress $clinicianWallet.Address `
    -PublicKey $clinicianWallet.PublicKey `
    -Headers $clinicianAuth.Headers
Write-WtInfo "Clinician participant published"

# ============================================================================
# Step 6: Publish Blueprint
# ============================================================================
Write-WtStep "Step 6: Publish Health Declaration Blueprint"

$templatePath = Join-Path $scriptDir "health-declaration-template.json"
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath $templatePath `
    -WalletMap @{
        # patient is the open starting-action sender — Publish-SorchaBlueprint
        # auto-skips open participants (Feature 103), so it's fine to pass it
        # in the map, but we omit it here to make the intent explicit.
        "clinician" = $clinicianWallet.Address
    } `
    -Headers $patientAuth.Headers `
    -IdPrefix "hd" `
    -RegisterId $register.RegisterId
Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    profile      = $Profile
    tenantUrl    = $env.TenantUrl
    blueprintUrl = $env.BlueprintUrl
    walletUrl    = $env.WalletUrl
    registerUrl  = $env.RegisterUrl
    gatewayUrl   = $env.GatewayUrl
    registerId   = $register.RegisterId
    blueprintId  = $blueprint.BlueprintId
    clinic       = @{
        organizationId = $clinicOrg.OrganizationId
    }
    patient      = @{
        email          = $secrets.patientEmail
        password       = $secrets.patientPassword
        organizationId = $clinicOrg.OrganizationId
        walletAddress  = $patientWallet.Address
        publicKey      = $patientWallet.PublicKey
    }
    clinician    = @{
        email          = $clinicianEmail
        password       = $clinicianPassword
        organizationId = $clinicOrg.OrganizationId
        walletAddress  = $clinicianWallet.Address
        publicKey      = $clinicianWallet.PublicKey
    }
}

$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — state saved to state.json"
Write-Host ""
Write-WtInfo "Demo:"
Write-WtInfo "  1. Visit: $($env.GatewayUrl)/app/new-submissions"
Write-WtInfo "  2. Login: $($secrets.patientEmail) / $($secrets.patientPassword)"
Write-WtInfo "  3. Click 'Pre-Treatment Health Declaration' to start the wizard"
Write-Host ""
Write-WtInfo "Try toggling 'Do you have any ongoing medical conditions?' on page 2 —"
Write-WtInfo "the conditions list field appears via the x-rule extension."
