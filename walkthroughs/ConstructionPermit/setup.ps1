#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ConstructionPermit — Setup (Multi-Org)
# Creates 4 organisations, 5 users, wallets, participants, register subscriptions,
# and publishes the construction permit blueprint.
#
# Organizations:
#   1. Meridian Construction     — contractor
#   2. Apex Structural Engineers — structural-engineer
#   3. Riverside Borough Council — planning-officer + building-control (2 users)
#   4. Green Valley Environmental — environmental-assessor

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ConstructionPermit — Multi-Org Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "construction-permit"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Step 1: Login as System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -AdminEmail $secrets.meridianAdminEmail `
    -AdminPassword $secrets.meridianAdminPassword

# ============================================================================
# Step 2: Pre-create Platform Users (so org creation can add them directly)
# ============================================================================
Write-WtStep "Step 2: Pre-create Platform Users"

# User definitions: role -> org, email, password, displayName
$userDefs = @(
    @{ role = "contractor";              org = "meridian";    email = $secrets.contractorEmail;       password = $secrets.contractorPassword;       name = $secrets.contractorName }
    @{ role = "structural-engineer";     org = "apex";        email = $secrets.engineerEmail;         password = $secrets.engineerPassword;         name = $secrets.engineerName }
    @{ role = "planning-officer";        org = "riverside";   email = $secrets.planningEmail;         password = $secrets.planningPassword;         name = $secrets.planningName }
    @{ role = "environmental-assessor";  org = "greenvalley"; email = $secrets.environmentalEmail;    password = $secrets.environmentalPassword;    name = $secrets.environmentalName }
    @{ role = "building-control";        org = "riverside";   email = $secrets.inspectorEmail;        password = $secrets.inspectorPassword;        name = $secrets.inspectorName }
)

# Create users in system admin org first — this creates PlatformUser records.
# When we create private orgs below, the existing PlatformUsers are found by email
# and added directly (no email invitation needed).
foreach ($u in $userDefs) {
    Get-OrCreateUser `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $sysAdmin.OrganizationId `
        -Email $u.email `
        -DisplayName $u.name `
        -Headers $sysAdmin.Headers `
        -Roles @("Member")
    Write-WtInfo "  $($u.role) -> $($u.email) (PlatformUser created)"
}

# ============================================================================
# Step 3: Create 4 Organizations
# ============================================================================
Write-WtStep "Step 3: Create Organizations (4)"

# Use the system admin email for org creation — this avoids SMTP invitations
# since the system admin PlatformUser already exists and is added directly.
$orgDefs = @(
    @{ name = "Meridian Construction";      subdomain = "meridian";    desc = "General contractor" }
    @{ name = "Apex Structural Engineers";  subdomain = "apex";        desc = "Structural assessment" }
    @{ name = "Riverside Borough Council";  subdomain = "riverside";   desc = "Local planning authority" }
    @{ name = "Green Valley Environmental"; subdomain = "greenvalley"; desc = "Environmental assessment" }
)

$orgs = @{}

foreach ($def in $orgDefs) {
    $result = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -Name $def.name `
        -Subdomain $def.subdomain `
        -AdminEmail $secrets.meridianAdminEmail `
        -Headers $sysAdmin.Headers `
        -Description $def.desc
    $orgs[$def.subdomain] = $result.OrganizationId
}

Write-WtInfo "  meridian:    $($orgs.meridian)"
Write-WtInfo "  apex:        $($orgs.apex)"
Write-WtInfo "  riverside:   $($orgs.riverside)"
Write-WtInfo "  greenvalley: $($orgs.greenvalley)"

# ============================================================================
# Step 4: Add extra users to their orgs
# ============================================================================
Write-WtStep "Step 4: Add Users to Organizations"

# Building control inspector is a second user in Riverside (planning officer was added as admin)
# We need to add them explicitly.
$orgUserMap = @{
    "contractor"             = "meridian"
    "structural-engineer"    = "apex"
    "planning-officer"       = "riverside"
    "environmental-assessor" = "greenvalley"
    "building-control"       = "riverside"
}

foreach ($u in $userDefs) {
    $orgKey = $orgUserMap[$u.role]
    $orgId = $orgs[$orgKey]
    $roles = if ($u.role -eq "contractor" -or $u.role -eq "planning-officer") { @("Administrator", "Member") } else { @("Member") }
    Get-OrCreateUser `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgId `
        -Email $u.email `
        -DisplayName $u.name `
        -Headers $sysAdmin.Headers `
        -Roles $roles
    Write-WtInfo "  $($u.role) ($($u.email)) -> $orgKey"
}

# ============================================================================
# Step 5: Switch to Each Org and Create Wallets + Participants
# ============================================================================
Write-WtStep "Step 5: Per-Role Setup (switch-org, wallet, participant, wallet-link)"

$users = @{}  # role -> session info
$wallets = @{}  # role -> wallet address

foreach ($u in $userDefs) {
    $orgKey = $orgUserMap[$u.role]
    $orgId = $orgs[$orgKey]
    Write-WtInfo "--- $($u.role) ($($u.name)) in $orgKey ---"

    # Switch admin to this org context
    $orgSession = Switch-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgId `
        -Headers $sysAdmin.Headers

    # Create wallet (wallets are global, not org-scoped)
    $wallet = New-SorchaWallet `
        -WalletUrl $env.WalletUrl `
        -Name "$($u.name) Wallet" `
        -Headers $orgSession.Headers `
        -FetchPublicKey

    $wallets[$u.role] = $wallet.Address

    # Register participant + link wallet in this org
    $participant = Register-SorchaParticipant `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -OrganizationId $orgId `
        -WalletAddress $wallet.Address `
        -DisplayName $u.name `
        -Headers $orgSession.Headers

    # Store session info
    $users[$u.role] = @{
        Token          = $orgSession.Token
        UserId         = $orgSession.UserId
        OrganizationId = $orgId
        WalletAddress  = $wallet.Address
        PublicKey       = $participant.PublicKey
        ParticipantId  = $participant.ParticipantId
        OrgKey         = $orgKey
    }

    Write-WtInfo "  wallet: $($wallet.Address)"
    Write-WtInfo "  participant: $($participant.ParticipantId)"
}

# ============================================================================
# Step 6: Create Register (owned by Meridian contractor)
# ============================================================================
Write-WtStep "Step 6: Create Register"

# Switch to Meridian org for register creation
$meridianSession = Switch-SorchaOrganization `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $orgs.meridian `
    -Headers $sysAdmin.Headers

$register = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl `
    -WalletUrl $env.WalletUrl `
    -Name "Construction Permit Register" `
    -Description "Multi-org construction permit approval register" `
    -TenantId $orgs.meridian `
    -OwnerUserId $meridianSession.UserId `
    -OwnerWalletAddress $users["contractor"].WalletAddress `
    -Headers $meridianSession.Headers `
    -Metadata @{ createdBy = "ConstructionPermit/setup.ps1"; multiOrg = "true" }

# ============================================================================
# Step 7: Subscribe All Organizations to the Register
# ============================================================================
Write-WtStep "Step 7: Subscribe Organizations to Register"

# Owner org (Meridian) gets Owner subscription
New-SorchaRegisterSubscription `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $orgs.meridian `
    -RegisterId $register.RegisterId `
    -RegisterName "Construction Permit Register" `
    -SubscriptionType "Owner" `
    -Headers $meridianSession.Headers

# Other orgs get Public subscriptions (switch to each org context)
$otherOrgs = @("apex", "riverside", "greenvalley")

foreach ($orgKey in $otherOrgs) {
    $orgSession = Switch-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgs[$orgKey] `
        -Headers $sysAdmin.Headers

    New-SorchaRegisterSubscription `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgs[$orgKey] `
        -RegisterId $register.RegisterId `
        -RegisterName "Construction Permit Register" `
        -SubscriptionType "Public" `
        -Headers $orgSession.Headers
}

# ============================================================================
# Step 8: Publish Participant Records to Register
# ============================================================================
Write-WtStep "Step 8: Publish Participant Records to Register"

$orgNameMap = @{
    "contractor"             = "Meridian Construction"
    "structural-engineer"    = "Apex Structural Engineers"
    "planning-officer"       = "Riverside Borough Council"
    "environmental-assessor" = "Green Valley Environmental"
    "building-control"       = "Riverside Borough Council"
}

foreach ($u in $userDefs) {
    $roleUser = $users[$u.role]
    # Switch to the participant's org for publishing
    $pubSession = Switch-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $roleUser.OrganizationId `
        -Headers $sysAdmin.Headers

    Publish-SorchaParticipant `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $roleUser.OrganizationId `
        -RegisterId $register.RegisterId `
        -ParticipantName $u.name `
        -OrganizationName $orgNameMap[$u.role] `
        -WalletAddress $roleUser.WalletAddress `
        -PublicKey $roleUser.PublicKey `
        -Headers $pubSession.Headers
}

# ============================================================================
# Step 9: Publish Blueprint
# ============================================================================
Write-WtStep "Step 9: Publish Blueprint"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "construction-permit-template.json") `
    -WalletMap $wallets `
    -Headers $meridianSession.Headers `
    -IdPrefix "construction-permit" `
    -RegisterId $register.RegisterId

# ============================================================================
# Save State
# ============================================================================
$roleInfo = @{}
foreach ($role in $users.Keys) {
    $u = $users[$role]
    $roleInfo[$role] = @{
        organizationId = $u.OrganizationId
        walletAddress  = $u.WalletAddress
        participantId  = $u.ParticipantId
        orgKey         = $u.OrgKey
    }
}

$state = @{
    profile        = $Profile
    registerId     = $register.RegisterId
    blueprintId    = $blueprint.BlueprintId
    blueprintUrl   = $env.BlueprintUrl
    tenantUrl      = $env.TenantUrl
    adminEmail     = $secrets.meridianAdminEmail
    adminPassword  = $secrets.meridianAdminPassword
    wallets        = $wallets
    organizations  = $orgs
    roles          = $roleInfo
}

$stateFile = Join-Path $scriptDir "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Multi-org setup complete"
Write-WtInfo "  4 organizations, 5 users, 5 wallets, 5 participants"
Write-WtInfo "  1 register (4 org subscriptions), 1 blueprint published"
Write-WtInfo "Run: pwsh walkthroughs/ConstructionPermit/run.ps1"
