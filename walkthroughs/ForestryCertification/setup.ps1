#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ForestryCertification — Setup
# Provisions Forestry Certification (issuer/auditor) and Highland Timber Supplies
# (supplier) organisations, creates wallets and participants, creates the
# Forestry Certification Register, and publishes the DPP blueprint.
#
# The supplier's "Sales Manager" participant is OPEN by design — late-bound at
# runtime when whichever wallet submits Action 1. The auditor is closed/pre-bound.
#
# Idempotent: re-running picks up existing orgs/wallets/registers via subdomain
# and current-user-subscription lookups. Pass -Force to overwrite state.json.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ForestryCertification — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "forestry-certification" -Profile $Profile
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists. Use -Force to re-run."
    return
}

# Helper: deterministic admin password fallback shared with TradeFinance —
# admin@<subdomain>.sorcha.dev uses Wt-<subdomain>-admin-2026!. If TradeFinance
# already created a Highland Timber org on this stack, this matches its admin
# credentials so the same wallet/identity carries between walkthroughs.
function Get-OrgAdminPassword {
    param([string]$Subdomain)
    $secretKey = "${Subdomain}_admin_password"
    if ($secrets.ContainsKey($secretKey)) { return $secrets[$secretKey] }
    return "Wt-$Subdomain-admin-2026!"
}

# ============================================================================
# Step 1: Login as System Admin (creates platform-level orgs)
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword
Write-WtSuccess "Connected (org: $($sysAdmin.OrganizationId))"

# Raise maxOrgsPerUser so the same admin user can sit in two orgs if needed
try {
    $null = Invoke-SorchaApi -Method PUT `
        -Uri "$($sorchaEnv.TenantUrl)/platform/settings/max-orgs" `
        -Body @{ maxOrgsPerUser = 10 } `
        -Headers $sysAdmin.Headers
} catch { Write-WtWarn "max-orgs setting unchanged" }

# ============================================================================
# Step 2: Forestry Certification Org (issuer)
# ============================================================================
Write-WtStep "Step 2: Forestry Certification organisation"

$fcSubdomain = "forestry-certification"
$fcAdminEmail = "admin@$fcSubdomain.sorcha.dev"
$fcAdminPassword = Get-OrgAdminPassword -Subdomain $fcSubdomain

try {
    $null = Register-SorchaPublicUser `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Email $fcAdminEmail `
        -Password $fcAdminPassword `
        -DisplayName "Forestry Certification Admin"
} catch { Write-WtInfo "  user $fcAdminEmail may already exist" }

try {
    $null = Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.TenantUrl)/platform/users/verify-email" `
        -Body @{ email = $fcAdminEmail } `
        -Headers $sysAdmin.Headers
} catch { Write-WtWarn "  email verify failed for $fcAdminEmail" }

try {
    $fcOrg = New-SorchaOrganization `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Name "Forestry Certification" `
        -Subdomain $fcSubdomain `
        -AdminEmail $fcAdminEmail `
        -Headers $sysAdmin.Headers `
        -Description "Independent forestry auditor — issues Digital Product Passports for timber batches"
    $fcOrgId = $fcOrg.OrganizationId
    Write-WtSuccess "Forestry Certification org: $fcOrgId"
} catch {
    $allOrgs = Invoke-SorchaApi -Method GET `
        -Uri "$($sorchaEnv.TenantUrl)/platform/organizations?page=1&pageSize=50" `
        -Headers $sysAdmin.Headers
    $items = if ($allOrgs.items) { $allOrgs.items } else { @($allOrgs) }
    $existing = $items | Where-Object { $_.subdomain -eq $fcSubdomain } | Select-Object -First 1
    if (-not $existing) { throw "Could not create or find Forestry Certification org" }
    $fcOrgId = $existing.id
    Write-WtInfo "Reusing existing Forestry Certification org: $fcOrgId"
}

# ============================================================================
# Step 3: Highland Timber Supplies Org (supplier — same identity TradeFinance uses)
# ============================================================================
Write-WtStep "Step 3: Highland Timber Supplies organisation"

$htSubdomain = "highland-timber"
$htAdminEmail = "admin@$htSubdomain.sorcha.dev"
$htAdminPassword = Get-OrgAdminPassword -Subdomain $htSubdomain

try {
    $null = Register-SorchaPublicUser `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Email $htAdminEmail `
        -Password $htAdminPassword `
        -DisplayName "Highland Timber Admin"
} catch { Write-WtInfo "  user $htAdminEmail may already exist" }

try {
    $null = Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.TenantUrl)/platform/users/verify-email" `
        -Body @{ email = $htAdminEmail } `
        -Headers $sysAdmin.Headers
} catch { Write-WtWarn "  email verify failed for $htAdminEmail" }

try {
    $htOrg = New-SorchaOrganization `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Name "Highland Timber Supplies" `
        -Subdomain $htSubdomain `
        -AdminEmail $htAdminEmail `
        -Headers $sysAdmin.Headers `
        -Description "Timber supplier — applies for Digital Product Passport certification on harvested batches"
    $htOrgId = $htOrg.OrganizationId
    Write-WtSuccess "Highland Timber org: $htOrgId"
} catch {
    $allOrgs = Invoke-SorchaApi -Method GET `
        -Uri "$($sorchaEnv.TenantUrl)/platform/organizations?page=1&pageSize=50" `
        -Headers $sysAdmin.Headers
    $items = if ($allOrgs.items) { $allOrgs.items } else { @($allOrgs) }
    $existing = $items | Where-Object { $_.subdomain -eq $htSubdomain } | Select-Object -First 1
    if (-not $existing) { throw "Could not create or find Highland Timber org" }
    $htOrgId = $existing.id
    Write-WtInfo "Reusing existing Highland Timber org: $htOrgId"
}

# ============================================================================
# Step 4: Auditor wallet + participant (Forestry Certification)
# ============================================================================
Write-WtStep "Step 4: Auditor wallet and participant"

$fcSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $fcAdminEmail `
    -Password $fcAdminPassword `
    -OrganizationId $fcOrgId

$auditorWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Forestry Auditor" `
    -Headers $fcSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Auditor wallet: $($auditorWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $fcOrgId `
    -WalletAddress $auditorWallet.Address `
    -DisplayName "Forestry Auditor" `
    -Headers $fcSession.Headers
Write-WtInfo "Auditor participant registered"

# ============================================================================
# Step 5: Sales Manager wallet + participant (Highland Timber)
# ============================================================================
Write-WtStep "Step 5: Sales Manager wallet and participant"

$htSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $htAdminEmail `
    -Password $htAdminPassword `
    -OrganizationId $htOrgId

$salesMgrWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Sales Manager" `
    -Headers $htSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Sales Manager wallet: $($salesMgrWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $htOrgId `
    -WalletAddress $salesMgrWallet.Address `
    -DisplayName "Sales Manager" `
    -Headers $htSession.Headers
Write-WtInfo "Sales Manager participant registered"

# ============================================================================
# Step 6: Create Forestry Certification Register
# ============================================================================
Write-WtStep "Step 6: Create Forestry Certification Register"

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Forestry Certification Register" `
    -Description "Digital Product Passports for verifiably-sustainable timber batches" `
    -TenantId $fcOrgId `
    -OwnerUserId $fcSession.UserId `
    -OwnerWalletAddress $auditorWallet.Address `
    -Headers $fcSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl `
    -DevMode
Write-WtSuccess "Register: $($register.RegisterId)"

# Publish auditor as an on-register participant so disclosures resolve
try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $fcOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Forestry Auditor" `
        -OrganizationName "Forestry Certification" `
        -WalletAddress $auditorWallet.Address `
        -PublicKey $auditorWallet.PublicKey `
        -Headers $fcSession.Headers
} catch { Write-WtWarn "Auditor publish failed: $($_.Exception.Message)" }

# Subscribe Highland Timber to the register
try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $htOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "Forestry Certification Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
    Write-WtInfo "Highland Timber subscribed to register"
} catch { Write-WtWarn "Highland Timber subscribe failed: $($_.Exception.Message)" }

# Publish sales-mgr as an on-register participant for disclosure resolution
try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $htOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Sales Manager" `
        -OrganizationName "Highland Timber Supplies" `
        -WalletAddress $salesMgrWallet.Address `
        -PublicKey $salesMgrWallet.PublicKey `
        -Headers $htSession.Headers
} catch { Write-WtWarn "Sales Manager publish failed: $($_.Exception.Message)" }

# ============================================================================
# Step 7: Publish Blueprint
# ============================================================================
Write-WtStep "Step 7: Publish Forestry Certification blueprint"

# Open-participant contract: 'sales-mgr' is the sender of an isStartingAction
# and MUST NOT appear in the wallet map. Late-bound at runtime to whichever
# wallet submits Action 1.
# See .claude/skills/blueprint-builder — "Open Participants & Late Binding".
$walletMap = @{
    "auditor" = $auditorWallet.Address
    # "sales-mgr" intentionally absent — late-bound at runtime
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "forestry-certification-template.json") `
    -WalletMap $walletMap `
    -Headers $fcSession.Headers `
    -IdPrefix "forestry-certification" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Save State
# ============================================================================
$state = @{
    profile      = $Profile
    tenantUrl    = $sorchaEnv.TenantUrl
    blueprintUrl = $sorchaEnv.BlueprintUrl
    walletUrl    = $sorchaEnv.WalletUrl
    registerUrl  = $sorchaEnv.RegisterUrl
    gatewayUrl   = $sorchaEnv.GatewayUrl
    registerId   = $register.RegisterId
    blueprintId  = $blueprint.BlueprintId
    organizations = @{
        forestryCertification = $fcOrgId
        highlandTimber        = $htOrgId
    }
    wallets = @{
        auditor   = $auditorWallet.Address
        salesMgr  = $salesMgrWallet.Address
    }
    roles = @{
        auditor = @{
            email          = $fcAdminEmail
            password       = $fcAdminPassword
            organizationId = $fcOrgId
            walletAddress  = $auditorWallet.Address
        }
        salesMgr = @{
            email          = $htAdminEmail
            password       = $htAdminPassword
            organizationId = $htOrgId
            walletAddress  = $salesMgrWallet.Address
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "ForestryCertification — Setup Complete"
