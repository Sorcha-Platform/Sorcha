#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# SelfBuildHouse — Setup (Multi-Org)
# Creates 5 private orgs + 1 public-org citizen, 7 users, 7 wallets,
# 2 registers, and publishes the planning permission + building warrant
# blueprints.
#
# Each role's wallet is created under that role's own user token, so the
# wallets belong to the people they represent — nothing pollutes the
# sysadmin's wallet list.
#
# Organisations (5 private + public):
#   1. (Public Org)                          — self-builder (citizen)
#   2. Strathcarron Council — Planning           — planning-officer (admin)
#   3. Strathcarron Council — Building Standards — building-standards-officer (admin)
#                                                + building-inspector (team)
#   4. Caledonian Water                           — utilities-officer (admin)
#   5. Murchison Engineering                      — structural-engineer (admin)
#   6. Heatherbank Environmental                  — ecologist (admin)
#
# Follows the ConstructionPermit multi-org setup pattern exactly.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "SelfBuildHouse — Multi-Org Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "self-build-house" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Well-known public org ID (created by DatabaseInitializer)
$publicOrgId = "00000000-0000-0000-0000-000000000002"

# ============================================================================
# Definitions
# ============================================================================

$orgDefs = @(
    @{ name = "Strathcarron Council — Planning";           subdomain = "strathcarron-planning"; desc = "Planning authority";                      adminRole = "planning-officer" }
    @{ name = "Strathcarron Council — Building Standards"; subdomain = "strathcarron-bs";       desc = "Building control & standards";            adminRole = "building-standards-officer" }
    @{ name = "Caledonian Water";                           subdomain = "caledonian-water";     desc = "Water & drainage consultee";              adminRole = "utilities-officer" }
    @{ name = "Murchison Engineering";                      subdomain = "murchison";            desc = "Structural engineering consultancy";       adminRole = "structural-engineer" }
    @{ name = "Heatherbank Environmental";                  subdomain = "heatherbank";          desc = "Ecology, habitat, and species surveys";    adminRole = "ecologist" }
)

$userDefs = @(
    # Self-builder is a citizen — lives in the Public Org, not a private org admin.
    @{ role = "self-builder";               org = "public";          email = $secrets.selfBuilderEmail;      password = $secrets.selfBuilderPassword;      name = $secrets.selfBuilderName;         isOrgAdmin = $false; isCitizen = $true }
    @{ role = "planning-officer";           org = "strathcarron-planning"; email = $secrets.planningEmail;       password = $secrets.planningPassword;         name = $secrets.planningName;            isOrgAdmin = $true }
    @{ role = "building-standards-officer"; org = "strathcarron-bs";     email = $secrets.buildingStandardsEmail; password = $secrets.buildingStandardsPassword; name = $secrets.buildingStandardsName;  isOrgAdmin = $true }
    @{ role = "building-inspector";         org = "strathcarron-bs";     email = $secrets.buildingInspectorEmail; password = $secrets.buildingInspectorPassword; name = $secrets.buildingInspectorName;  isOrgAdmin = $false }
    @{ role = "utilities-officer";          org = "caledonian-water";  email = $secrets.utilitiesEmail;        password = $secrets.utilitiesPassword;        name = $secrets.utilitiesName;           isOrgAdmin = $true }
    @{ role = "structural-engineer";        org = "murchison";       email = $secrets.structuralEmail;       password = $secrets.structuralPassword;       name = $secrets.structuralName;          isOrgAdmin = $true }
    @{ role = "ecologist";                  org = "heatherbank";    email = $secrets.ecologistEmail;        password = $secrets.ecologistPassword;        name = $secrets.ecologistName;           isOrgAdmin = $true }
)

# ============================================================================
# Step 1: Login as System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

# ============================================================================
# Step 2: Enable Public Org (required for user self-registration)
# ============================================================================
Write-WtStep "Step 2: Enable Public Org"
Invoke-SorchaApi -Method PUT `
    -Uri "$($env.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } `
    -Headers $sysAdmin.Headers
Write-WtInfo "  Public org enabled for self-registration"

# ============================================================================
# Step 3: Register ALL users on the public org (creates PlatformUser records)
# ============================================================================
Write-WtStep "Step 3: Register Users (public org — creates PlatformUser records)"

foreach ($u in $userDefs) {
    Register-SorchaPublicUser `
        -TenantUrl $env.TenantUrl `
        -Email $u.email `
        -Password $u.password `
        -DisplayName $u.name | Out-Null
    Write-WtInfo "  $($u.role) -> $($u.email)"
}

# ============================================================================
# Step 4: Verify Everyone's Email (admin override — bypasses SMTP)
# ============================================================================
Write-WtStep "Step 4: Verify User Emails (admin override)"

$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($env.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers

foreach ($u in $userDefs) {
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
# Step 5: Create 5 Private Organizations (with verified org-admin emails)
# ============================================================================
Write-WtStep "Step 5: Create Organizations (5)"

$orgs = @{}
foreach ($def in $orgDefs) {
    $adminUser = $userDefs | Where-Object { $_.role -eq $def.adminRole } | Select-Object -First 1
    $result = New-SorchaOrganization `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -Name $def.name `
        -Subdomain $def.subdomain `
        -AdminEmail $adminUser.email `
        -AdminPassword $adminUser.password `
        -Headers $sysAdmin.Headers `
        -Description $def.desc
    $orgs[$def.subdomain] = $result.OrganizationId
    Write-WtInfo "  $($def.subdomain): $($result.OrganizationId) (admin: $($adminUser.email))"
}

# Self-builder lives on the public org — its org id is the well-known public one.
$orgs["public"] = $publicOrgId

# ============================================================================
# Step 6: Reconcile User -> Org Membership (admins + team members)
# ============================================================================
Write-WtStep "Step 6: Reconcile User -> Org Membership"

# Idempotency guard: on re-runs, orgs already exist so New-SorchaOrganization
# returns the existing id without re-binding the admin email. Ensure every
# non-citizen user is a member of their target org, regardless of admin/team.
$nonCitizenUsers = $userDefs | Where-Object { -not $_.isCitizen }
foreach ($u in $nonCitizenUsers) {
    $orgId = $orgs[$u.org]
    Get-OrCreateUser `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $orgId `
        -Email $u.email `
        -DisplayName $u.name `
        -Headers $sysAdmin.Headers `
        -Roles @("Administrator", "Consumer") | Out-Null
    Write-WtInfo "  $($u.role) ($($u.email)) -> $($u.org)"
}

# ============================================================================
# Step 7: Per-Role Setup (login, wallet, participant, wallet-link)
# ============================================================================
Write-WtStep "Step 7: Per-Role Setup (each user creates their own wallet)"

$users = @{}   # role -> { email, password, organizationId, walletAddress, orgKey }
$wallets = @{} # role -> wallet address
$sessionCache = @{}

foreach ($u in $userDefs) {
    $orgKey = $u.org
    $orgId = $orgs[$orgKey]
    Write-WtInfo "--- $($u.role) ($($u.name)) in $orgKey ---"

    # Login as this user and select their target org (two-step auth flow).
    $cacheKey = "$($u.email)|$orgId"
    if ($sessionCache.ContainsKey($cacheKey)) {
        $userSession = $sessionCache[$cacheKey]
        Write-WtInfo "  (cached session)"
    } else {
        $userSession = Connect-SorchaUser `
            -TenantUrl $env.TenantUrl `
            -Email $u.email `
            -Password $u.password `
            -OrganizationId $orgId
        $sessionCache[$cacheKey] = $userSession
    }

    # Create wallet under THIS user's token — wallet ownership is scoped to
    # the caller, so creating it here puts it in this user's wallet list.
    $wallet = New-SorchaWallet `
        -WalletUrl $env.WalletUrl `
        -Name "$($u.name) Wallet" `
        -Headers $userSession.Headers `
        -FetchPublicKey
    $wallets[$u.role] = $wallet.Address
    Write-WtInfo "  wallet: $($wallet.Address)"

    # Register participant + link wallet in the user's org.
    $null = Register-SorchaParticipant `
        -TenantUrl $env.TenantUrl `
        -WalletUrl $env.WalletUrl `
        -OrganizationId $orgId `
        -WalletAddress $wallet.Address `
        -DisplayName $u.name `
        -Headers $userSession.Headers

    $users[$u.role] = @{
        email          = $u.email
        password       = $u.password
        organizationId = $orgId
        orgKey         = $orgKey
        walletAddress  = $wallet.Address
    }
}

# ============================================================================
# Step 8: Create Registers (2) — owned by the appropriate council officers
# ============================================================================
Write-WtStep "Step 8: Create Registers (2)"

# Planning register is owned by Strathcarron Council — Planning (planning-officer).
# Build the headers/user-id/wallet from the planning-officer's session so the
# register is created under that user and their org is subscribed as Owner.
# Re-login the planning-officer (register owner) so the JWT carries wallet_address now that
# their wallet is linked — required by the F142 publish governance gate (see walkthrough-builder
# skill, "re-login any session AFTER its wallet is created").
$planningSession = Connect-SorchaUser -TenantUrl $env.TenantUrl `
    -Email $users['planning-officer'].email -Password $users['planning-officer'].password `
    -OrganizationId $users['planning-officer'].organizationId
$sessionCache["$($users['planning-officer'].email)|$($users['planning-officer'].organizationId)"] = $planningSession
$planningOfficerJwt = Decode-SorchaJwt -Token $planningSession.Token
$planningOwnerUserId = $planningOfficerJwt.sub

Write-WtInfo "  Creating Strathcarron Planning Register..."
$planningRegister = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl -TenantUrl $env.TenantUrl `
    -Name "Strathcarron Planning Register" `
    -Description "Planning applications, consultations, and decisions for the Strathcarron Council area" `
    -TenantId $users["planning-officer"].organizationId `
    -OwnerUserId $planningOwnerUserId `
    -OwnerWalletAddress $users["planning-officer"].walletAddress `
    -Headers $planningSession.Headers `
    -DevMode `
    -Metadata @{ createdBy = "SelfBuildHouse/setup.ps1"; registerType = "planning" }
Write-WtSuccess "  Planning Register: $($planningRegister.RegisterId)"

# Building Standards register is owned by Strathcarron Council — Building Standards
# (building-standards-officer).
# Re-login the building-standards-officer (register owner) so the JWT carries wallet_address (F142).
$bsSession = Connect-SorchaUser -TenantUrl $env.TenantUrl `
    -Email $users['building-standards-officer'].email -Password $users['building-standards-officer'].password `
    -OrganizationId $users['building-standards-officer'].organizationId
$sessionCache["$($users['building-standards-officer'].email)|$($users['building-standards-officer'].organizationId)"] = $bsSession
$bsOfficerJwt = Decode-SorchaJwt -Token $bsSession.Token
$bsOwnerUserId = $bsOfficerJwt.sub

Write-WtInfo "  Creating Strathcarron Warrants Register..."
$buildingRegister = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl -TenantUrl $env.TenantUrl `
    -Name "Strathcarron Warrants Register" `
    -Description "Building warrants, staged inspections, and completion certificates for the Strathcarron Council area" `
    -TenantId $users["building-standards-officer"].organizationId `
    -OwnerUserId $bsOwnerUserId `
    -OwnerWalletAddress $users["building-standards-officer"].walletAddress `
    -Headers $bsSession.Headers `
    -DevMode `
    -Metadata @{ createdBy = "SelfBuildHouse/setup.ps1"; registerType = "building-standards" }
Write-WtSuccess "  Building Standards Register: $($buildingRegister.RegisterId)"

# ============================================================================
# Step 9: Cross-Org Subscriptions — every org that participates in either
# workflow must be subscribed to both registers so their members can see
# the transactions they're expected to act on.
# ============================================================================
Write-WtStep "Step 9: Subscribe Participating Orgs to Both Registers"

$privateOrgKeys = $orgDefs | ForEach-Object { $_.subdomain }

foreach ($orgKey in $privateOrgKeys) {
    $orgId = $orgs[$orgKey]

    # Skip owners — they already have Owner subscriptions from finalize.
    $isPlanningOwner = ($orgKey -eq "strathcarron-planning")
    $isBuildingOwner = ($orgKey -eq "strathcarron-bs")

    if (-not $isPlanningOwner) {
        try {
            $null = New-SorchaRegisterSubscription `
                -TenantUrl $env.TenantUrl `
                -OrganizationId $orgId `
                -RegisterId $planningRegister.RegisterId `
                -RegisterName "Strathcarron Planning Register" `
                -SubscriptionType "Public" `
                -Headers $sysAdmin.Headers
        } catch {
            Write-WtWarn "  $orgKey -> Planning Register subscribe failed: $($_.Exception.Message)"
        }
    }

    if (-not $isBuildingOwner) {
        try {
            $null = New-SorchaRegisterSubscription `
                -TenantUrl $env.TenantUrl `
                -OrganizationId $orgId `
                -RegisterId $buildingRegister.RegisterId `
                -RegisterName "Strathcarron Warrants Register" `
                -SubscriptionType "Public" `
                -Headers $sysAdmin.Headers
        } catch {
            Write-WtWarn "  $orgKey -> Building Register subscribe failed: $($_.Exception.Message)"
        }
    }
}

# ============================================================================
# Step 10: Publish Blueprints (planning + warrant)
# ============================================================================
Write-WtStep "Step 10: Publish Blueprints (2)"

# Both registers ISSUE credentials (planning permission, building warrant) that are later
# presented across registers. Provision each issuing org's Feature 083 HD master key so the
# F120 kid-swap signs with the org issuance key (iss=did:sorcha:org:..., kid=...#vc-issuance-N);
# without it the credentials are signed with the bare wallet key and fail the engine
# TrustEvaluator ("issuer signature not verified") on presentation. Idempotent (silent on 409).
Set-SorchaOrgMasterKey -WalletUrl $env.WalletUrl `
    -OrganizationId $users["planning-officer"].organizationId -Headers $planningSession.Headers
Set-SorchaOrgMasterKey -WalletUrl $env.WalletUrl `
    -OrganizationId $users["building-standards-officer"].organizationId -Headers $bsSession.Headers

# The register's genesis control tx — which records the owner governance roster — seals
# asynchronously AFTER New-SorchaRegister returns. The F142 publish gate reads that roster and
# fail-closes with a 403 ("no publish-governance role") when it isn't recorded yet. Wait for each
# register's roster to populate before publishing its blueprint (the register-genesis analogue of
# the F145 action-seal cadence).
function Wait-RegisterRosterReady($registerId, $headers) {
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $roster = Invoke-SorchaApi -Method GET `
                -Uri "$($env.RegisterUrl)/registers/$registerId/governance/roster" -Headers $headers
            if ($roster.members -and $roster.members.Count -gt 0) { return $true }
        } catch { }
        Start-Sleep -Seconds 1
    }
    Write-WtWarn "  register $registerId governance roster not populated after 30s"
    return $false
}

Write-WtInfo "  Publishing Planning Permission blueprint..."
$null = Wait-RegisterRosterReady $planningRegister.RegisterId $planningSession.Headers
$planningBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "planning-permission-template.json") `
    -WalletMap $wallets `
    -Headers $planningSession.Headers `
    -IdPrefix "self-build-planning" `
    -RegisterId $planningRegister.RegisterId
Write-WtSuccess "  Planning Blueprint: $($planningBlueprint.BlueprintId)"

Write-WtInfo "  Publishing Building Warrant blueprint..."
$null = Wait-RegisterRosterReady $buildingRegister.RegisterId $bsSession.Headers
$warrantBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "building-warrant-template.json") `
    -WalletMap $wallets `
    -Headers $bsSession.Headers `
    -IdPrefix "self-build-warrant" `
    -RegisterId $buildingRegister.RegisterId
Write-WtSuccess "  Warrant Blueprint: $($warrantBlueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    profile             = $Profile
    tenantUrl           = $env.TenantUrl
    blueprintUrl        = $env.BlueprintUrl
    registerUrl         = $env.RegisterUrl
    walletUrl           = $env.WalletUrl
    organizations       = $orgs
    roles               = $users
    wallets             = $wallets
    planningRegisterId  = $planningRegister.RegisterId
    buildingRegisterId  = $buildingRegister.RegisterId
    planningBlueprintId = $planningBlueprint.BlueprintId
    warrantBlueprintId  = $warrantBlueprint.BlueprintId
}

$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — 5 private orgs + public org, 7 users, 7 wallets, 2 registers, 2 blueprints"
Write-WtInfo "Run: pwsh walkthroughs/SelfBuildHouse/run.ps1"
