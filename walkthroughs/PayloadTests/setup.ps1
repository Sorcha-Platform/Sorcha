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
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PayloadTests — Multi-Org Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "payload-test" -Profile $Profile
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

# Verify emails — admin-approve via platform endpoint
foreach ($email in @($senderEmail, $receiverEmail)) {
    try {
        # Use admin endpoint to verify the user's email
        Invoke-SorchaApi -Method POST `
            -Uri "$($env.TenantUrl)/platform/users/verify-email" `
            -Body @{ email = $email } `
            -Headers $sysAdmin.Headers
        Write-WtInfo "Verified: $email"
    } catch {
        # Already verified or endpoint doesn't exist — continue
        Write-WtInfo "Email verification skipped for: $email (may already be verified)"
    }
}

# ============================================================================
# Step 3: Create Private Orgs
# ============================================================================
Write-WtStep "Step 3: Create Organizations"

try {
    $senderOrg = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -Name "Sender Corp" `
        -Subdomain "payload-sender" `
        -AdminEmail $senderEmail `
        -Headers $sysAdmin.Headers
} catch {
    if ($_.Exception.Message -match 'already taken|409|duplicate|400') {
        # Look up existing org
        $orgs = (Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $sysAdmin.Headers)
        $existing = $orgs | Where-Object { $_.name -eq "Sender Corp" -or $_.subdomain -eq "payload-sender" } | Select-Object -First 1
        if (-not $existing) { $existing = ($orgs.items | Where-Object { $_.name -eq "Sender Corp" -or $_.subdomain -eq "payload-sender" }) | Select-Object -First 1 }
        $senderOrg = @{ OrganizationId = $existing.id ?? $existing.organizationId }
        Write-WtInfo "Sender Org exists: $($senderOrg.OrganizationId)"
    } else { throw }
}
Write-WtInfo "Sender Org: $($senderOrg.OrganizationId)"

try {
    $receiverOrg = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -Name "Receiver Corp" `
        -Subdomain "payload-receiver" `
        -AdminEmail $receiverEmail `
        -Headers $sysAdmin.Headers
} catch {
    if ($_.Exception.Message -match 'already taken|409|duplicate|400') {
        if (-not $orgs) { $orgs = (Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $sysAdmin.Headers) }
        $existing = $orgs | Where-Object { $_.name -eq "Receiver Corp" -or $_.subdomain -eq "payload-receiver" } | Select-Object -First 1
        if (-not $existing) { $existing = ($orgs.items | Where-Object { $_.name -eq "Receiver Corp" -or $_.subdomain -eq "payload-receiver" }) | Select-Object -First 1 }
        $receiverOrg = @{ OrganizationId = $existing.id ?? $existing.organizationId }
        Write-WtInfo "Receiver Org exists: $($receiverOrg.OrganizationId)"
    } else { throw }
}
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

# Re-login the sender (register owner + blueprint publisher) so the JWT carries the
# wallet_address claim now that their wallet is created + linked. The F142 publish governance
# gate matches wallet_address against the register roster owner; a token minted before the wallet
# existed lacks it and the publish 403s "You do not hold a publish-governance role". See the
# walkthrough-builder skill, "re-login the blueprint publisher".
$senderAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $senderEmail `
    -Password $userPassword `
    -OrganizationId $senderOrg.OrganizationId

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

# Re-login the receiver too — the file-download endpoint (F085) authorizes via the caller's
# wallet, and the receiver's first token (minted before its wallet existed) carries no
# wallet_address claim, which 403s the download. Same root cause as the publisher re-login.
$receiverAuth = Connect-SorchaUser `
    -TenantUrl $env.TenantUrl `
    -Email $receiverEmail `
    -Password $userPassword `
    -OrganizationId $receiverOrg.OrganizationId

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
    -TenantUrl $env.TenantUrl `
    -OrganizationId $receiverOrg.OrganizationId `
    -RegisterId $register.RegisterId `
    -Headers $senderAuth.Headers `
    -SubscriptionType "Public"
Write-WtInfo "Receiver org subscribed to register"

# Public org subscription so consumer users see the register.
$null = Add-SorchaPublicOrgSubscription `
    -TenantUrl $env.TenantUrl `
    -RegisterId $register.RegisterId `
    -RegisterName "Payload Test Register" `
    -SysAdminHeaders $sysAdmin.Headers `
    -SysAdminEmail $secrets.adminEmail

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
