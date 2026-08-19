# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Shared council universe setup — creates orgs, admin users, and wallets
# for the Strathcarron Council demo universe. Idempotent.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$Force,
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$stateFile = Join-Path $scriptDir "council-state.json"

# ── Module ────────────────────────────────────────────────────────
$modulePath = Join-Path $scriptDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

# ── Environment ───────────────────────────────────────────────────
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck
$secrets = Get-SorchaSecrets -WalkthroughName "council" -Profile $Profile

Write-WtBanner "Strathcarron Council Universe Setup"

# ── Idempotency: return cached state if valid ─────────────────────
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "Council state file exists, validating..."
    $existing = Get-Content $stateFile -Raw | ConvertFrom-Json
    try {
        $sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
            -AdminEmail $secrets.sysAdminEmail -AdminPassword $secrets.sysAdminPassword
        Write-WtSuccess "Council state valid — reusing"
        return $existing
    } catch {
        Write-WtWarn "Council state invalid, recreating..."
    }
}

# ── System admin login ────────────────────────────────────────────
$sysAdmin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.sysAdminEmail -AdminPassword $secrets.sysAdminPassword

# ── Enable public org ─────────────────────────────────────────────
Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } -Headers $sysAdmin.Headers | Out-Null

# ── Organisation definitions ──────────────────────────────────────
$orgDefs = @(
    @{ name = "Strathcarron Council"; subdomain = "strathcarron"; desc = "Local authority — planning, building standards, housing" }
    @{ name = "Stoniebridge Construction"; subdomain = "stoniebridge"; desc = "General contractor" }
    @{ name = "Murchison Engineering"; subdomain = "murchison"; desc = "Structural engineering consultancy" }
    @{ name = "Heatherbank Environmental"; subdomain = "heatherbank"; desc = "Ecology and environmental consultancy" }
    @{ name = "Caledonian Water"; subdomain = "caledonian-water"; desc = "Water and drainage utility" }
)

# ── User definitions (org admins) ─────────────────────────────────
$orgAdminDefs = @(
    @{ role = "planning-officer"; orgSubdomain = "strathcarron"; email = $secrets.planningOfficerEmail; password = $secrets.planningOfficerPassword; name = $secrets.planningOfficerName }
    @{ role = "contractor"; orgSubdomain = "stoniebridge"; email = $secrets.contractorEmail; password = $secrets.contractorPassword; name = $secrets.contractorName }
    @{ role = "structural-engineer"; orgSubdomain = "murchison"; email = $secrets.structuralEmail; password = $secrets.structuralPassword; name = $secrets.structuralName }
    @{ role = "ecologist"; orgSubdomain = "heatherbank"; email = $secrets.ecologistEmail; password = $secrets.ecologistPassword; name = $secrets.ecologistName }
    @{ role = "utilities-officer"; orgSubdomain = "caledonian-water"; email = $secrets.utilitiesEmail; password = $secrets.utilitiesPassword; name = $secrets.utilitiesName }
)

# Team members (non-admin users added to existing orgs)
$teamMemberDefs = @(
    @{ role = "building-standards-officer"; orgSubdomain = "strathcarron"; email = $secrets.buildingStandardsEmail; password = $secrets.buildingStandardsPassword; name = $secrets.buildingStandardsName }
    @{ role = "building-inspector"; orgSubdomain = "strathcarron"; email = $secrets.buildingInspectorEmail; password = $secrets.buildingInspectorPassword; name = $secrets.buildingInspectorName }
    @{ role = "building-control"; orgSubdomain = "strathcarron"; email = $secrets.buildingControlEmail; password = $secrets.buildingControlPassword; name = $secrets.buildingControlName }
    @{ role = "housing-officer"; orgSubdomain = "strathcarron"; email = $secrets.housingOfficerEmail; password = $secrets.housingOfficerPassword; name = $secrets.housingOfficerName }
)

$publicOrgId = "00000000-0000-0000-0000-000000000002"

# ── Step 1: Register all users on public org ──────────────────────
Write-WtStep "Registering users on public org"
$allUsers = $orgAdminDefs + $teamMemberDefs
foreach ($u in $allUsers) {
    Register-SorchaPublicUser -TenantUrl $sorchaEnv.TenantUrl `
        -Email $u.email -Password $u.password -DisplayName $u.name | Out-Null
}

# ── Step 2: Verify emails ────────────────────────────────────────
Write-WtStep "Verifying user emails"
$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true&pageSize=100" `
    -Headers $sysAdmin.Headers
foreach ($u in $allUsers) {
    $pu = $publicUsers.users | Where-Object { $_.email -eq $u.email } | Select-Object -First 1
    if ($pu) {
        Confirm-SorchaUserEmail -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $publicOrgId -UserId $pu.id -Headers $sysAdmin.Headers | Out-Null
    }
}

# ── Step 3: Create private orgs ──────────────────────────────────
Write-WtStep "Creating organisations"
$orgs = @{}
foreach ($def in $orgDefs) {
    $adminUser = $orgAdminDefs | Where-Object { $_.orgSubdomain -eq $def.subdomain } | Select-Object -First 1
    $result = New-SorchaOrganization -TenantUrl $sorchaEnv.TenantUrl `
        -Name $def.name -Subdomain $def.subdomain `
        -AdminEmail $adminUser.email -Headers $sysAdmin.Headers -Description $def.desc
    $orgs[$def.subdomain] = $result.OrganizationId
    Write-WtInfo "  $($def.name) → $($result.OrganizationId)"
}

# ── Step 4: Reconcile user-org membership (admins + team members) ──
# Idempotency: on re-runs, orgs already exist, so New-SorchaOrganization
# returns the existing id without re-binding the admin email. Ensure every
# user is a member of their target org regardless of admin-vs-team role.
Write-WtStep "Reconciling user-org membership"
foreach ($u in $orgAdminDefs) {
    $orgId = $orgs[$u.orgSubdomain]
    Get-OrCreateUser -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $orgId `
        -Email $u.email -DisplayName $u.name `
        -Headers $sysAdmin.Headers -Roles @("Administrator", "Consumer") | Out-Null
    Write-WtInfo "  $($u.name) ensured in $($u.orgSubdomain)"
}
foreach ($u in $teamMemberDefs) {
    $orgId = $orgs[$u.orgSubdomain]
    Get-OrCreateUser -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $orgId `
        -Email $u.email -DisplayName $u.name `
        -Headers $sysAdmin.Headers -Roles @("Administrator", "Consumer") | Out-Null
    Write-WtInfo "  $($u.name) ensured in $($u.orgSubdomain)"
}

# ── Step 4b: Each org's ADMIN creates that organisation's wallet ──
# #1525 — the org wallet is what the organisation's issuer DID anchors on and what its governance
# roster identity is matched against, and its recovery phrase is shown once and never stored. So it
# is created by an administrator OF THAT ORG, not by the platform. Done here, after membership is
# reconciled, because that is the first point an admin session exists.
Write-WtStep "Creating organisation wallets (as each org's admin)"
foreach ($u in $orgAdminDefs) {
    $orgId = $orgs[$u.orgSubdomain]
    $adminSession = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
        -Email $u.email -Password $u.password -OrganizationId $orgId
    $null = New-SorchaOrgWallet -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
        -OrganizationId $orgId -Headers $adminSession.Headers `
        -Name "org-$($u.orgSubdomain)-signing"
}

# ── Step 5: Login as each user, create wallets, register participants ─
Write-WtStep "Creating wallets and registering participants"
$sessionCache = @{}
$roles = @{}

foreach ($u in $allUsers) {
    $orgId = $orgs[$u.orgSubdomain]
    $cacheKey = "$($u.email)|$orgId"

    # Login
    if (-not $sessionCache.ContainsKey($cacheKey)) {
        $session = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
            -Email $u.email -Password $u.password -OrganizationId $orgId
        $sessionCache[$cacheKey] = $session
    }
    $session = $sessionCache[$cacheKey]

    # Wallet
    $wallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl `
        -Name "$($u.name) Wallet" -Headers $session.Headers -FetchPublicKey

    # Participant
    $participant = Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl `
        -WalletUrl $sorchaEnv.WalletUrl -OrganizationId $orgId `
        -WalletAddress $wallet.Address -DisplayName $u.name -Headers $session.Headers

    $roles[$u.role] = @{
        email          = $u.email
        password       = $u.password
        name           = $u.name
        organizationId = $orgId
        walletAddress  = $wallet.Address
        publicKey      = $participant.PublicKey ?? $wallet.PublicKey
        participantId  = $participant.ParticipantId
        orgSubdomain   = $u.orgSubdomain
    }
    Write-WtInfo "  $($u.role) → $($wallet.Address)"
}

# ── Build state ───────────────────────────────────────────────────
$councilState = @{
    profile       = $Profile
    organizations = $orgs
    roles         = $roles
    sysAdmin      = @{
        email    = $secrets.sysAdminEmail
        password = $secrets.sysAdminPassword
    }
    environment   = @{
        gatewayUrl   = $sorchaEnv.GatewayUrl
        tenantUrl    = $sorchaEnv.TenantUrl
        blueprintUrl = $sorchaEnv.BlueprintUrl
        registerUrl  = $sorchaEnv.RegisterUrl
        walletUrl    = $sorchaEnv.WalletUrl
    }
}

$councilState | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "Council state saved to $stateFile"

return $councilState
