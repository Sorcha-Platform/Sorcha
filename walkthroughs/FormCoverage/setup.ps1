#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# FormCoverage — Demo Setup
# Single-org walkthrough built to eyeball-test the SorchaFormRenderer. The
# blueprint exercises every ControlTypes value (Layout, Label, TextLine,
# TextArea, Numeric, DateTime, File, Choice, Checkbox, Selection), all three
# layout modes (x-pages wizard + x-sections + flat form), x-width hints,
# x-rule conditional visibility, x-introduction, and the x-persona extension
# from Feature 092.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "FormCoverage — Demo Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "form-coverage" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Pre-flight: validate existing state unless -Force.
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtStep "Validating existing state.json"
    $existingState = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
    $stateValid = $true

    try {
        $testLogin = Invoke-SorchaApi -Method POST `
            -Uri "$($existingState.tenantUrl)/auth/login" `
            -Body @{ email = $existingState.submitter.email; password = $existingState.submitter.password }
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
    -OrgName "Form Coverage System" `
    -OrgSubdomain "form-coverage-sys" `
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
# Step 2: Register Submitter and Reviewer Users on Public Org
# ============================================================================
Write-WtStep "Step 2: Register Submitter and Reviewer Users"

foreach ($user in @(
    @{ Email = $secrets.submitterEmail; Name = $secrets.submitterName; Password = $secrets.submitterPassword },
    @{ Email = $secrets.reviewerEmail;  Name = $secrets.reviewerName;  Password = $secrets.reviewerPassword }
)) {
    try {
        Register-SorchaPublicUser `
            -TenantUrl $env.TenantUrl `
            -Email $user.Email `
            -DisplayName $user.Name `
            -Password $user.Password
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
# Step 3: Create Form Coverage Demo Org
# ============================================================================
Write-WtStep "Step 3: Create Form Coverage Demo Organization"

try {
    $demoOrg = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -Name "Form Coverage Demo" `
        -Subdomain "form-coverage-demo" `
        -AdminEmail $secrets.submitterEmail `
        -AdminPassword $secrets.submitterPassword `
        -Headers $sysAdmin.Headers
} catch {
    if ($_.Exception.Message -match 'already taken|409|duplicate|400') {
        $orgs = (Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $sysAdmin.Headers)
        $existing = $orgs | Where-Object { $_.name -eq "Form Coverage Demo" -or $_.subdomain -eq "form-coverage-demo" } | Select-Object -First 1
        if (-not $existing) { $existing = ($orgs.items | Where-Object { $_.name -eq "Form Coverage Demo" -or $_.subdomain -eq "form-coverage-demo" }) | Select-Object -First 1 }
        $demoOrg = @{ OrganizationId = $existing.id ?? $existing.organizationId }
        Write-WtInfo "Form Coverage Demo exists: $($demoOrg.OrganizationId)"
    } else { throw }
}
Write-WtInfo "Form Coverage Demo Org: $($demoOrg.OrganizationId)"

# ============================================================================
# Step 3.5: Add reviewer to Form Coverage Demo as an Administrator
# ============================================================================
Write-WtStep "Step 3.5: Add Reviewer to Form Coverage Demo"
Get-OrCreateUser `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $demoOrg.OrganizationId `
    -Email $secrets.reviewerEmail `
    -DisplayName $secrets.reviewerName `
    -Headers $sysAdmin.Headers `
    -Roles @("Administrator", "Consumer") | Out-Null
Write-WtInfo "Reviewer added to Form Coverage Demo"

# ============================================================================
# Step 4: Login and create wallets + participants for both users
# ============================================================================
Write-WtStep "Step 4: Create Wallets & Participants"

$submitterAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $secrets.submitterEmail `
    -Password $secrets.submitterPassword `
    -OrganizationId $demoOrg.OrganizationId

$submitterWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Submitter Wallet" `
    -Headers $submitterAuth.Headers `
    -FetchPublicKey

$null = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $demoOrg.OrganizationId `
    -WalletAddress $submitterWallet.Address `
    -DisplayName "Submitter" `
    -Headers $submitterAuth.Headers
Write-WtSuccess "Submitter: $($submitterWallet.Address)"

# Re-login the submitter (register owner + blueprint publisher) so the JWT carries wallet_address
# now that the wallet is linked — required by the F142 publish governance gate (see the
# walkthrough-builder skill, "re-login the blueprint publisher").
$submitterAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $secrets.submitterEmail `
    -Password $secrets.submitterPassword `
    -OrganizationId $demoOrg.OrganizationId

$reviewerAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $secrets.reviewerEmail `
    -Password $secrets.reviewerPassword `
    -OrganizationId $demoOrg.OrganizationId

$reviewerWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Reviewer Wallet" `
    -Headers $reviewerAuth.Headers `
    -FetchPublicKey

$null = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $demoOrg.OrganizationId `
    -WalletAddress $reviewerWallet.Address `
    -DisplayName "Reviewer" `
    -Headers $reviewerAuth.Headers
Write-WtSuccess "Reviewer: $($reviewerWallet.Address)"

# ============================================================================
# Step 5: Create Register (idempotent — reuse existing one if present)
# ============================================================================
Write-WtStep "Step 5: Create or Reuse Register"

$registerName = "Form Coverage Register"
$existingRegisterId = Get-SorchaRegisterByName `
    -TenantUrl $env.TenantUrl `
    -Name $registerName `
    -Headers $submitterAuth.Headers

if ($existingRegisterId) {
    Write-WtInfo "Reusing existing register '$registerName': $existingRegisterId"
    $register = @{ RegisterId = $existingRegisterId }
} else {
    $register = New-SorchaRegister `
        -RegisterUrl $env.RegisterUrl `
        -WalletUrl $env.WalletUrl `
        -Name $registerName `
        -Description "Register that holds FormCoverage test submissions for eyeballing every control type" `
        -TenantId $demoOrg.OrganizationId `
        -OwnerUserId $submitterAuth.UserId `
        -OwnerWalletAddress $submitterWallet.Address `
        -Headers $submitterAuth.Headers
    Write-WtSuccess "Register created: $($register.RegisterId)"

    try {
        New-SorchaRegisterSubscription `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $demoOrg.OrganizationId `
            -RegisterId $register.RegisterId `
            -RegisterName $registerName `
            -Headers $submitterAuth.Headers `
            -SubscriptionType "Owner" | Out-Null
        Write-WtInfo "Form Coverage Demo subscribed to register as Owner"
    } catch {
        if ($_.Exception.Message -match '409|already exists|duplicate') {
            Write-WtInfo "Form Coverage Demo already subscribed (skipping)"
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
    -OrganizationId $demoOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Submitter" `
    -OrganizationName "Form Coverage Demo" `
    -WalletAddress $submitterWallet.Address `
    -PublicKey $submitterWallet.PublicKey `
    -Headers $submitterAuth.Headers
Write-WtInfo "Submitter participant published"

Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $demoOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Reviewer" `
    -OrganizationName "Form Coverage Demo" `
    -WalletAddress $reviewerWallet.Address `
    -PublicKey $reviewerWallet.PublicKey `
    -Headers $reviewerAuth.Headers
Write-WtInfo "Reviewer participant published"

# ============================================================================
# Step 6: Publish Blueprint
# ============================================================================
Write-WtStep "Step 6: Publish Form Coverage Blueprint"

$templatePath = Join-Path $scriptDir "form-coverage-template.json"
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath $templatePath `
    -WalletMap @{
        "submitter" = $submitterWallet.Address
        "reviewer"  = $reviewerWallet.Address
    } `
    -Headers $submitterAuth.Headers `
    -IdPrefix "fc" `
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
    demoOrg      = @{
        organizationId = $demoOrg.OrganizationId
    }
    submitter    = @{
        email          = $secrets.submitterEmail
        password       = $secrets.submitterPassword
        organizationId = $demoOrg.OrganizationId
        walletAddress  = $submitterWallet.Address
        publicKey      = $submitterWallet.PublicKey
    }
    reviewer     = @{
        email          = $secrets.reviewerEmail
        password       = $secrets.reviewerPassword
        organizationId = $demoOrg.OrganizationId
        walletAddress  = $reviewerWallet.Address
        publicKey      = $reviewerWallet.PublicKey
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "State saved to $stateFile"

Write-WtBanner "FormCoverage Setup Complete"
Write-WtInfo "Run: pwsh walkthroughs/FormCoverage/run.ps1 -Profile $Profile"
Write-WtInfo "Or open: $($env.GatewayUrl)/app/new-submissions and start a new FormCoverage instance"
