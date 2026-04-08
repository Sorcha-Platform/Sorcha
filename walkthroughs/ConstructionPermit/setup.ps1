#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ConstructionPermit — Setup (Multi-Org)
# Creates 4 organisations, 5 users, wallets, participants, register subscriptions,
# and publishes the construction permit blueprint.
#
# Realistic flow:
#   1. System admin logs in
#   2. Register all users on the public org (creates PlatformUser records)
#   3. System admin verifies all user emails (bypasses SMTP)
#   4. System admin creates 4 private orgs with verified org-admin emails
#   5. Add all users to their target orgs with correct roles
#   6. Per-user setup: wallets, participants, wallet-links
#   7-9. Register creation, subscriptions, participant publishing, blueprint
#
# Organizations:
#   1. Meridian Construction     — contractor (admin)
#   2. Apex Structural Engineers — structural-engineer (admin)
#   3. Riverside Borough Council — planning-officer (admin) + building-control
#   4. Green Valley Environmental — environmental-assessor (admin)

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ConstructionPermit — Multi-Org Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "construction-permit"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Pre-flight: if state.json exists, validate that resources are still live.
# Skip full setup if everything is healthy — only re-run with -Force.
# ============================================================================
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtStep "Validating existing state.json"
    $existingState = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

    $stateValid = $true
    try {
        # Check user can still login
        $testLogin = Invoke-SorchaApi -Method POST `
            -Uri "$($existingState.tenantUrl)/auth/login" `
            -Body @{ email = $existingState.roles.contractor.email; password = $existingState.roles.contractor.password }

        if (-not $testLogin.requires_org_selection -and -not $testLogin.access_token) {
            $stateValid = $false
        }

        # Check blueprint exists
        if ($stateValid -and $existingState.blueprintId) {
            $contractorSelect = Invoke-SorchaApi -Method POST `
                -Uri "$($existingState.tenantUrl)/auth/select-org" `
                -Body @{ platform_login_token = $testLogin.platform_login_token; organization_id = $existingState.roles.contractor.organizationId }
            $bpHeaders = @{ Authorization = "Bearer $($contractorSelect.access_token)" }

            try {
                $bp = Invoke-SorchaApi -Method GET `
                    -Uri "$($existingState.blueprintUrl)/blueprints/$($existingState.blueprintId)" `
                    -Headers $bpHeaders
                if ($bp.id -or $bp.blueprintId) {
                    Write-WtSuccess "Blueprint $($existingState.blueprintId) exists"
                } else { $stateValid = $false }
            } catch {
                Write-WtWarn "Blueprint $($existingState.blueprintId) not found"
                $stateValid = $false
            }
        }
    } catch {
        Write-WtWarn "State validation failed: $($_.Exception.Message)"
        $stateValid = $false
    }

    if ($stateValid) {
        Write-WtSuccess "Existing state is valid — skipping setup (use -Force to recreate)"
        Write-WtInfo "Run: pwsh walkthroughs/ConstructionPermit/run.ps1"
        exit 0
    }

    Write-WtWarn "State invalid — running full setup"
}

# Well-known public org ID (created by DatabaseInitializer)
$publicOrgId = "00000000-0000-0000-0000-000000000002"

# ============================================================================
# Definitions
# ============================================================================

$orgDefs = @(
    @{ name = "Meridian Construction";      subdomain = "meridian";    desc = "General contractor";        adminRole = "contractor" }
    @{ name = "Apex Structural Engineers";  subdomain = "apex";        desc = "Structural assessment";     adminRole = "structural-engineer" }
    @{ name = "Riverside Borough Council";  subdomain = "riverside";   desc = "Local planning authority";  adminRole = "planning-officer" }
    @{ name = "Green Valley Environmental"; subdomain = "greenvalley"; desc = "Environmental assessment";  adminRole = "environmental-assessor" }
)

$userDefs = @(
    @{ role = "contractor";              org = "meridian";    email = $secrets.contractorEmail;       password = $secrets.contractorPassword;       name = $secrets.contractorName;        isOrgAdmin = $true }
    @{ role = "structural-engineer";     org = "apex";        email = $secrets.engineerEmail;         password = $secrets.engineerPassword;         name = $secrets.engineerName;          isOrgAdmin = $true }
    @{ role = "planning-officer";        org = "riverside";   email = $secrets.planningEmail;         password = $secrets.planningPassword;         name = $secrets.planningName;          isOrgAdmin = $true }
    @{ role = "environmental-assessor";  org = "greenvalley"; email = $secrets.environmentalEmail;    password = $secrets.environmentalPassword;    name = $secrets.environmentalName;     isOrgAdmin = $true }
    @{ role = "building-control";        org = "riverside";   email = $secrets.inspectorEmail;        password = $secrets.inspectorPassword;        name = $secrets.inspectorName;         isOrgAdmin = $false }
)

$orgUserMap = @{
    "contractor"             = "meridian"
    "structural-engineer"    = "apex"
    "planning-officer"       = "riverside"
    "environmental-assessor" = "greenvalley"
    "building-control"       = "riverside"
}

# ============================================================================
# Step 1: Login as System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -AdminEmail $secrets.meridianAdminEmail `
    -AdminPassword $secrets.meridianAdminPassword

# ============================================================================
# Step 2: Enable Public Org (required for user self-registration)
# ============================================================================
Write-WtStep "Step 2: Enable Public Org for User Registration"

# The public org may be disabled after fresh database init.
# Enable it so users can self-register (creating PlatformUser records).
Invoke-SorchaApi -Method PUT `
    -Uri "$($env.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } `
    -Headers $sysAdmin.Headers
Write-WtInfo "  Public org enabled for self-registration"

# ============================================================================
# Step 3: Register Org Admins on Public Org (creates PlatformUser with password)
# ============================================================================
Write-WtStep "Step 3: Register Org Admins (public org — creates PlatformUser)"

# Only org admins need PlatformUser records before org creation.
# Team members (building-control) will be added directly by their org admin later.
$orgAdminDefs = $userDefs | Where-Object { $_.isOrgAdmin }
$teamMemberDefs = $userDefs | Where-Object { -not $_.isOrgAdmin }

foreach ($u in $orgAdminDefs) {
    Register-SorchaPublicUser `
        -TenantUrl $env.TenantUrl `
        -Email $u.email `
        -Password $u.password `
        -DisplayName $u.name | Out-Null
    Write-WtInfo "  $($u.role) -> $($u.email)"
}

# ============================================================================
# Step 4: Admin Verify Org Admin Emails (bypasses SMTP verification loop)
# ============================================================================
Write-WtStep "Step 4: Verify Org Admin Emails (admin override — no SMTP needed)"

$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($env.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers

foreach ($u in $orgAdminDefs) {
    $publicUser = $publicUsers.users | Where-Object { $_.email -eq $u.email } | Select-Object -First 1
    if ($publicUser) {
        Confirm-SorchaUserEmail `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $publicOrgId `
            -UserId $publicUser.id `
            -Headers $sysAdmin.Headers
    } else {
        Write-WtWarn "  User $($u.email) not found in public org"
    }
}

# ============================================================================
# Step 4: Create 4 Private Organizations (with verified org admin emails)
# ============================================================================
Write-WtStep "Step 5: Create Organizations (4)"

# Each org is created with its designated admin email.
# OrgProvisioning finds the existing verified PlatformUser and adds them
# directly as org admin — no SMTP invitation needed.
$orgs = @{}

foreach ($def in $orgDefs) {
    $adminUser = $userDefs | Where-Object { $_.role -eq $def.adminRole } | Select-Object -First 1
    $result = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -Name $def.name `
        -Subdomain $def.subdomain `
        -AdminEmail $adminUser.email `
        -Headers $sysAdmin.Headers `
        -Description $def.desc
    $orgs[$def.subdomain] = $result.OrganizationId
    Write-WtInfo "  $($def.subdomain): $($result.OrganizationId) (admin: $($adminUser.email), direct: $($result.AdminDirectlyAdded))"
}

# ============================================================================
# Step 6: Add Team Members to Their Orgs
# ============================================================================
Write-WtStep "Step 6: Add Team Members to Organizations"

# Team members need PlatformUser records first (register on public org),
# then get added to their target org by the system admin.
# This is realistic: the org admin would invite them, we shortcut via admin API.
foreach ($u in $teamMemberDefs) {
    # Create PlatformUser via public org registration
    Register-SorchaPublicUser `
        -TenantUrl $env.TenantUrl `
        -Email $u.email `
        -Password $u.password `
        -DisplayName $u.name | Out-Null

    # Verify their email
    $refreshedUsers = Invoke-SorchaApi -Method GET `
        -Uri "$($env.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
        -Headers $sysAdmin.Headers
    $pubUser = $refreshedUsers.users | Where-Object { $_.email -eq $u.email } | Select-Object -First 1
    if ($pubUser) {
        Confirm-SorchaUserEmail `
            -TenantUrl $env.TenantUrl `
            -OrganizationId $publicOrgId `
            -UserId $pubUser.id `
            -Headers $sysAdmin.Headers
    }

    # Add to their target org
    $orgId = $orgs[$u.org]
    Get-OrCreateUser `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgId `
        -Email $u.email `
        -DisplayName $u.name `
        -Headers $sysAdmin.Headers `
        -Roles @("Administrator", "Consumer")
    Write-WtInfo "  $($u.role) ($($u.email)) -> $($u.org) [Admin]"
}

# ============================================================================
# Step 7: Per-Role Setup (each user logs in to their org)
# ============================================================================
Write-WtStep "Step 7: Per-Role Setup (login, wallet, participant, wallet-link)"

$users = @{}   # role -> session info
$wallets = @{} # role -> wallet address
$sessionCache = @{} # cache per-user org-scoped tokens

foreach ($u in $userDefs) {
    $orgKey = $orgUserMap[$u.role]
    $orgId = $orgs[$orgKey]
    Write-WtInfo "--- $($u.role) ($($u.name)) in $orgKey ---"

    # Login as this user and select their target org (two-step auth flow)
    $cacheKey = "$($u.email)|$orgId"
    if ($sessionCache.ContainsKey($cacheKey)) {
        $orgSession = $sessionCache[$cacheKey]
        Write-WtInfo "  (cached session)"
    } else {
        $orgSession = Connect-SorchaUser `
            -TenantUrl $env.TenantUrl `
            -Email $u.email `
            -Password $u.password `
            -OrganizationId $orgId
        $sessionCache[$cacheKey] = $orgSession
    }

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
        Email          = $u.email
        Password       = $u.password
    }

    Write-WtInfo "  wallet: $($wallet.Address)"
    Write-WtInfo "  participant: $($participant.ParticipantId)"
}

# ============================================================================
# Step 7: Create Register (owned by Meridian contractor)
# ============================================================================
Write-WtStep "Step 8: Create Register"

# Reuse contractor's cached session for meridian org
$meridianSession = $sessionCache["$($secrets.contractorEmail)|$($orgs.meridian)"]

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
# Step 8: Subscribe All Organizations to the Register
# ============================================================================
Write-WtStep "Step 9: Subscribe Organizations to Register"

New-SorchaRegisterSubscription `
    -TenantUrl $env.TenantUrl `
    -OrganizationId $orgs.meridian `
    -RegisterId $register.RegisterId `
    -RegisterName "Construction Permit Register" `
    -SubscriptionType "Owner" `
    -Headers $meridianSession.Headers

$orgAdminMap = @{ "apex" = $secrets.engineerEmail; "riverside" = $secrets.planningEmail; "greenvalley" = $secrets.environmentalEmail }

foreach ($orgKey in @("apex", "riverside", "greenvalley")) {
    $orgSession = $sessionCache["$($orgAdminMap[$orgKey])|$($orgs[$orgKey])"]

    New-SorchaRegisterSubscription `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgs[$orgKey] `
        -RegisterId $register.RegisterId `
        -RegisterName "Construction Permit Register" `
        -SubscriptionType "Public" `
        -Headers $orgSession.Headers
}

# ============================================================================
# Step 9: Publish Participant Records to Register
# ============================================================================
Write-WtStep "Step 10: Publish Participant Records to Register"

$orgNameMap = @{
    "contractor"             = "Meridian Construction"
    "structural-engineer"    = "Apex Structural Engineers"
    "planning-officer"       = "Riverside Borough Council"
    "environmental-assessor" = "Green Valley Environmental"
    "building-control"       = "Riverside Borough Council"
}

foreach ($u in $userDefs) {
    $roleUser = $users[$u.role]
    $cacheKey = "$($u.email)|$($roleUser.OrganizationId)"
    $pubSession = $sessionCache[$cacheKey]

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
# Step 10: Publish Blueprint
# ============================================================================
Write-WtStep "Step 11: Publish Blueprint"
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
        email          = $u.Email
        password       = $u.Password
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
