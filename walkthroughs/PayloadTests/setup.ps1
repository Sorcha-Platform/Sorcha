#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PayloadTests — Setup (Multi-Org)
# Creates 2 organisations with 1 participant each, a shared register,
# and publishes the file-transfer blueprint.
#
# Organizations:
#   1. Sender Corp   — sender participant (admin)
#   2. Receiver Corp — receiver participant (admin)

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PayloadTests — Multi-Org Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "payload-test"
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
            -Body @{ email = $existingState.sender.email; password = $existingState.sender.password }
        if (-not $testLogin.requires_org_selection -and -not $testLogin.access_token) {
            $stateValid = $false
        }
    } catch {
        $stateValid = $false
    }

    if ($stateValid) {
        Write-WtSuccess "Existing state is valid — skipping setup (use -Force to recreate)"
        Write-WtInfo "Run: pwsh walkthroughs/PayloadTests/run.ps1"
        exit 0
    }
    Write-WtWarn "State invalid — running full setup"
}

$publicOrgId = "00000000-0000-0000-0000-000000000002"

# ============================================================================
# Step 1: System Admin Login
# ============================================================================
Write-WtStep "Step 1: System Admin Login"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Payload Test System" `
    -OrgSubdomain "payload-sys" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

# ============================================================================
# Step 2: Register Users on Public Org
# ============================================================================
Write-WtStep "Step 2: Register Users"

$senderEmail    = "sender@payload-test.local"
$receiverEmail  = "receiver@payload-test.local"
$userPassword   = $secrets.adminPassword  # Reuse for simplicity

foreach ($email in @($senderEmail, $receiverEmail)) {
    try {
        Register-SorchaPublicUser `
            -TenantUrl $env.TenantUrl `
            -Email $email `
            -DisplayName ($email -replace '@.*', '') `
            -Password $userPassword
        Write-WtInfo "Registered: $email"
    } catch {
        if ($_.Exception.Message -match '409|already exists|duplicate') {
            Write-WtInfo "Already registered: $email"
        } else { throw }
    }
}

# Verify emails
foreach ($email in @($senderEmail, $receiverEmail)) {
    try {
        $userId = Get-OrCreateUser `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $publicOrgId `
            -Email $email `
            -DisplayName ($email -replace '@.*', '') `
            -Headers $sysAdmin.Headers
        Confirm-SorchaUserEmail `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $publicOrgId `
            -UserId $userId `
            -Headers $sysAdmin.Headers
        Write-WtInfo "Verified: $email"
    } catch {
        if ($_.Exception.Message -match 'already verified|already confirmed') {
            Write-WtInfo "Already verified: $email"
        } else { throw }
    }
}

# ============================================================================
# Step 3: Create Private Orgs
# ============================================================================
Write-WtStep "Step 3: Create Organizations"

$senderOrg = New-SorchaOrganization `
    -TenantUrl $env.TenantUrl `
    -Name "Sender Corp" `
    -Subdomain "payload-sender" `
    -AdminEmail $senderEmail `
    -Headers $sysAdmin.Headers
Write-WtInfo "Sender Org: $($senderOrg.OrganizationId)"

$receiverOrg = New-SorchaOrganization `
    -TenantUrl $env.TenantUrl `
    -Name "Receiver Corp" `
    -Subdomain "payload-receiver" `
    -AdminEmail $receiverEmail `
    -Headers $sysAdmin.Headers
Write-WtInfo "Receiver Org: $($receiverOrg.OrganizationId)"

# ============================================================================
# Step 4: Login as each user and create wallets + participants
# ============================================================================
Write-WtStep "Step 4: Create Wallets & Participants"

# Sender
$senderAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $senderEmail `
    -Password $userPassword `
    -OrganizationId $senderOrg.OrganizationId
$senderWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Sender Wallet" `
    -Headers $senderAuth.Headers `
    -FetchPublicKey
$senderParticipant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $senderOrg.OrganizationId `
    -WalletAddress $senderWallet.Address `
    -DisplayName "Sender" `
    -Headers $senderAuth.Headers
Write-WtSuccess "Sender: $($senderWallet.Address)"

# Receiver
$receiverAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $receiverEmail `
    -Password $userPassword `
    -OrganizationId $receiverOrg.OrganizationId
$receiverWallet = New-SorchaWallet `
    -WalletUrl $env.WalletUrl `
    -Name "Receiver Wallet" `
    -Headers $receiverAuth.Headers `
    -FetchPublicKey
$receiverParticipant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -WalletUrl $env.WalletUrl `
    -OrganizationId $receiverOrg.OrganizationId `
    -WalletAddress $receiverWallet.Address `
    -DisplayName "Receiver" `
    -Headers $receiverAuth.Headers
Write-WtSuccess "Receiver: $($receiverWallet.Address)"

# ============================================================================
# Step 5: Create Register
# ============================================================================
Write-WtStep "Step 5: Create Shared Register"

$register = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl `
    -WalletUrl $env.WalletUrl `
    -Name "Payload Test Register" `
    -Description "Register for file transfer payload testing" `
    -TenantId $senderOrg.OrganizationId `
    -OwnerUserId $senderAuth.UserId `
    -OwnerWalletAddress $senderWallet.Address `
    -Headers $senderAuth.Headers
Write-WtSuccess "Register: $($register.RegisterId)"

# Subscribe receiver org
New-SorchaRegisterSubscription `
    -RegisterUrl $env.RegisterUrl `
    -RegisterId $register.RegisterId `
    -TenantId $receiverOrg.OrganizationId `
    -Headers $senderAuth.Headers
Write-WtInfo "Receiver org subscribed to register"

# Publish participants to register
Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $senderOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Sender" `
    -OrganizationName "Sender Corp" `
    -WalletAddress $senderWallet.Address `
    -PublicKey $senderWallet.PublicKey `
    -Headers $senderAuth.Headers
Write-WtInfo "Sender participant published"

Publish-SorchaParticipant `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $receiverOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -ParticipantName "Receiver" `
    -OrganizationName "Receiver Corp" `
    -WalletAddress $receiverWallet.Address `
    -PublicKey $receiverWallet.PublicKey `
    -Headers $receiverAuth.Headers
Write-WtInfo "Receiver participant published"

# ============================================================================
# Step 6: Publish Blueprint
# ============================================================================
Write-WtStep "Step 6: Publish File Transfer Blueprint"

$templatePath = Join-Path $scriptDir "file-transfer-template.json"
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath $templatePath `
    -WalletMap @{
        "sender"   = $senderWallet.Address
        "receiver" = $receiverWallet.Address
    } `
    -Headers $senderAuth.Headers `
    -IdPrefix "pt" `
    -RegisterId $register.RegisterId
Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    profile       = $Profile
    tenantUrl     = $env.TenantUrl
    blueprintUrl  = $env.BlueprintUrl
    walletUrl     = $env.WalletUrl
    registerUrl   = $env.RegisterUrl
    gatewayUrl    = $env.GatewayUrl
    registerId    = $register.RegisterId
    blueprintId   = $blueprint.BlueprintId
    sender        = @{
        email          = $senderEmail
        password       = $userPassword
        organizationId = $senderOrg.OrganizationId
        walletAddress  = $senderWallet.Address
        publicKey      = $senderWallet.PublicKey
    }
    receiver      = @{
        email          = $receiverEmail
        password       = $userPassword
        organizationId = $receiverOrg.OrganizationId
        walletAddress  = $receiverWallet.Address
        publicKey      = $receiverWallet.PublicKey
    }
}

$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — state saved to state.json"
Write-WtInfo "Run: pwsh walkthroughs/PayloadTests/run.ps1 [-FileSize 1KB] [-Rounds 1]"
