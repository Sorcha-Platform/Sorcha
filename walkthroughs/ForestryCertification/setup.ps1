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

# Helper: deterministic participant password — matches the TradeFinance
# convention so the same Highland Timber sales-mgr@... user works in both
# walkthroughs without secrets juggling.
function Get-ParticipantPassword {
    param([string]$Subdomain, [string]$ParticipantId)
    $secretKey = "${Subdomain}_${ParticipantId}_password"
    if ($secrets.ContainsKey($secretKey)) { return $secrets[$secretKey] }
    return "Wt-$Subdomain-$ParticipantId-2026!"
}

# Helper: register a participant USER (separate from the org admin), add them to
# the org with Consumer role, and login as them. Returns a hashtable with Email,
# Password, UserId, Headers — the Headers are scoped to the participant user, so
# wallets created with them are owned by the participant rather than the org admin.
function New-ParticipantUserSession {
    param(
        [string]$ParticipantId,
        [string]$DisplayName,
        [string]$Subdomain,
        [string]$OrganizationId,
        [hashtable]$OrgAdminHeaders
    )

    $email = "$ParticipantId@$Subdomain.sorcha.dev"
    $password = Get-ParticipantPassword -Subdomain $Subdomain -ParticipantId $ParticipantId

    # Spec 136: provision the operator org-scoped + verified (single-org — no public account, no
    # multi-org clash). Org operators are never public users; only citizens are.
    # Idempotent: this org/user may already exist from a sibling walkthrough that shares the org
    # (Forestry + TradeFinance both use highland-timber by design). On a duplicate, fall through
    # to login as the existing org-scoped user — the password is deterministic.
    $userId = $null
    try {
        $provisioned = New-SorchaOrgUser `
            -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $OrganizationId `
            -Email $email `
            -Password $password `
            -DisplayName $DisplayName `
            -Headers $OrgAdminHeaders `
            -Roles @("Consumer") `
            -EmailVerified
        $userId = $provisioned.UserId
    } catch {
        $status = $null; try { $status = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($status -eq 400 -or $status -eq 409) {
            Write-WtInfo "  $email already provisioned — reusing"
        } else {
            throw
        }
    }

    $session = Connect-SorchaUser `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Email $email `
        -Password $password `
        -OrganizationId $OrganizationId

    return @{
        Email    = $email
        Password = $password
        UserId   = if ($userId) { $userId } else { $session.UserId }
        Headers  = $session.Headers
        Token    = $session.Token
    }
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

# Spec 136: provision the org admin org-scoped + verified in one call (single-org — no public
# account, no multi-org clash, no SMTP invite).
try {
    $fcOrg = New-SorchaOrganization `
        -TenantUrl $sorchaEnv.TenantUrl `
        -WalletUrl $sorchaEnv.WalletUrl `
        -Name "Forestry Certification" `
        -Subdomain $fcSubdomain `
        -AdminEmail $fcAdminEmail `
        -AdminPassword $fcAdminPassword `
        -AdminDisplayName "Forestry Certification Admin" `
        -AdminEmailVerified `
        -Headers $sysAdmin.Headers `
        -Description "Independent forestry auditor — issues Digital Product Passports for timber batches"
    $fcOrgId = $fcOrg.OrganizationId
    Write-WtSuccess "Forestry Certification org: $fcOrgId"
} catch {
    $allOrgs = Invoke-SorchaApi -Method GET `
        -Uri "$($sorchaEnv.TenantUrl)/platform/organizations?page=1&pageSize=50" `
        -Headers $sysAdmin.Headers
    $items = Resolve-SorchaCollection -Response $allOrgs -PropertyName 'items'
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

# Spec 136: provision the org admin org-scoped + verified in one call (single-org).
try {
    $htOrg = New-SorchaOrganization `
        -TenantUrl $sorchaEnv.TenantUrl `
        -WalletUrl $sorchaEnv.WalletUrl `
        -Name "Highland Timber Supplies" `
        -Subdomain $htSubdomain `
        -AdminEmail $htAdminEmail `
        -AdminPassword $htAdminPassword `
        -AdminDisplayName "Highland Timber Admin" `
        -AdminEmailVerified `
        -Headers $sysAdmin.Headers `
        -Description "Timber supplier — applies for Digital Product Passport certification on harvested batches"
    $htOrgId = $htOrg.OrganizationId
    Write-WtSuccess "Highland Timber org: $htOrgId"
} catch {
    $allOrgs = Invoke-SorchaApi -Method GET `
        -Uri "$($sorchaEnv.TenantUrl)/platform/organizations?page=1&pageSize=50" `
        -Headers $sysAdmin.Headers
    $items = Resolve-SorchaCollection -Response $allOrgs -PropertyName 'items'
    $existing = $items | Where-Object { $_.subdomain -eq $htSubdomain } | Select-Object -First 1
    if (-not $existing) { throw "Could not create or find Highland Timber org" }
    $htOrgId = $existing.id
    Write-WtInfo "Reusing existing Highland Timber org: $htOrgId"
}

# ============================================================================
# Step 4: Auditor wallet + participant (Forestry Certification)
# ============================================================================
# The auditor is a real user (auditor@forestry-certification.sorcha.dev), not
# the org admin. Their wallet is minted under their own session so when they
# log in they see their own wallet rather than nothing — and the org admin
# isn't carrying everyone's wallets.
Write-WtStep "Step 4: Auditor user, wallet and participant"

$fcAdminSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $fcAdminEmail `
    -Password $fcAdminPassword `
    -OrganizationId $fcOrgId

# Provision the FC org's Feature 083 master key so its Feature 120 VC-issuance key can derive —
# the ForestProductDPPCredential it issues must be cross-register-verifiable (TradeFinance's
# Finance phase presents it). Without it the credential signs with the bare wallet key and fails
# the TrustEvaluator ("issuer signature not verified"). Idempotent. See walkthrough-builder skill.
Set-SorchaOrgMasterKey -WalletUrl $sorchaEnv.WalletUrl -OrganizationId $fcOrgId -Headers $fcAdminSession.Headers

$auditorUser = New-ParticipantUserSession `
    -ParticipantId "auditor" `
    -DisplayName "Forestry Auditor" `
    -Subdomain $fcSubdomain `
    -OrganizationId $fcOrgId `
    -OrgAdminHeaders $fcAdminSession.Headers
Write-WtSuccess "Auditor user: $($auditorUser.Email)"

$auditorWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Forestry Auditor" `
    -Headers $auditorUser.Headers `
    -FetchPublicKey
Write-WtSuccess "Auditor wallet: $($auditorWallet.Address) (owned by $($auditorUser.Email))"

# Use the auditor user's headers — `/me/...self-register` registers the caller,
# and `/v1/wallets/{addr}/sign` requires the wallet owner. Both are the auditor.
$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $fcOrgId `
    -WalletAddress $auditorWallet.Address `
    -DisplayName "Forestry Auditor" `
    -Headers $auditorUser.Headers
Write-WtInfo "Auditor participant registered"

# ============================================================================
# Step 5: Sales Manager wallet + participant (Highland Timber)
# ============================================================================
Write-WtStep "Step 5: Sales Manager user, wallet and participant"

$htAdminSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $htAdminEmail `
    -Password $htAdminPassword `
    -OrganizationId $htOrgId

$salesMgrUser = New-ParticipantUserSession `
    -ParticipantId "sales-mgr" `
    -DisplayName "Sales Manager" `
    -Subdomain $htSubdomain `
    -OrganizationId $htOrgId `
    -OrgAdminHeaders $htAdminSession.Headers
Write-WtSuccess "Sales Manager user: $($salesMgrUser.Email)"

# Idempotent wallet lookup: reuse the user's existing wallet if they already
# have one. Cross-walkthrough composition relies on the DPP credential issued
# here landing in the SAME wallet that TradeFinance's run.ps1 fetches
# credentials from. If we always created a fresh wallet, the credential would
# be invisible to downstream walkthroughs and the cross-register uplift demo
# would silently fail.
#
# Selection precedence:
#   1. The wallet TradeFinance recorded in walkthroughs/TradeFinance/state.json
#      (under wallets["sales-mgr"]) — highest fidelity for cross-walkthrough
#      runs since it pins the DPP to the exact wallet TradeFinance will look at.
#   2. Otherwise, the first wallet returned by /v1/wallets for this user — keeps
#      Forestry standalone runs idempotent across re-runs of -Force setup.
#   3. Otherwise, mint a fresh wallet.
$existingWallets = @()
try {
    $existingWallets = @(Invoke-SorchaApi -Method GET `
        -Uri "$($sorchaEnv.WalletUrl)/v1/wallets" `
        -Headers $salesMgrUser.Headers)
} catch {
    # User has no wallets yet — fall through to create
}

$preferredWalletAddress = $null
$tradeFinanceStatePath = Join-Path (Split-Path -Parent $scriptDir) "TradeFinance/state.json"
if (Test-Path $tradeFinanceStatePath) {
    try {
        $tfState = Get-Content -Path $tradeFinanceStatePath -Raw | ConvertFrom-Json
        if ($tfState.wallets -and $tfState.wallets.'sales-mgr') {
            $preferredWalletAddress = $tfState.wallets.'sales-mgr'
            Write-WtInfo "TradeFinance state.json present — preferring sales-mgr wallet $preferredWalletAddress for cross-register credential composition"
        }
    } catch {
        # Couldn't parse — fall through
    }
}

$selectedWallet = $null
if ($preferredWalletAddress) {
    $selectedWallet = $existingWallets | Where-Object { $_.address -eq $preferredWalletAddress } | Select-Object -First 1
}
if (-not $selectedWallet -and $existingWallets.Count -gt 0) {
    $selectedWallet = $existingWallets[0]
}

if ($selectedWallet) {
    Write-WtInfo "Reusing existing Sales Manager wallet: $($selectedWallet.address)"

    # Fetch public key via signing probe so participant publish has it.
    # Body shape matches New-SorchaWallet -FetchPublicKey (transactionData + isPreHashed).
    $probeBody = @{
        transactionData = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("key-probe"))
        isPreHashed     = $false
    }
    $signResp = Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.WalletUrl)/v1/wallets/$($selectedWallet.address)/sign" `
        -Body $probeBody `
        -Headers $salesMgrUser.Headers

    $salesMgrWallet = @{
        Address   = $selectedWallet.address
        Mnemonic  = $null
        PublicKey = $signResp.publicKey
    }
    Write-WtSuccess "Sales Manager wallet (reused): $($salesMgrWallet.Address) (owned by $($salesMgrUser.Email))"
} else {
    $salesMgrWallet = New-SorchaWallet `
        -WalletUrl $sorchaEnv.WalletUrl `
        -Name "Sales Manager" `
        -Headers $salesMgrUser.Headers `
        -FetchPublicKey
    Write-WtSuccess "Sales Manager wallet: $($salesMgrWallet.Address) (owned by $($salesMgrUser.Email))"
}

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $htOrgId `
    -WalletAddress $salesMgrWallet.Address `
    -DisplayName "Sales Manager" `
    -Headers $salesMgrUser.Headers
Write-WtInfo "Sales Manager participant registered"

# ============================================================================
# Step 6: Create Forestry Certification Register
# ============================================================================
Write-WtStep "Step 6: Create Forestry Certification Register"

# The blueprint publisher must be BOTH an Administrator (CanPublishBlueprints policy) AND the
# register's governance-roster owner (F142 PublishGate matches the caller's wallet_address against
# the roster owner wallet-DID). The org admin (fc-admin) holds the Administrator role, so make it
# the register owner too: give it a wallet + participant, then re-login so the JWT carries
# wallet_address (minted from the first linked wallet at login). The auditor stays a workflow
# participant. See walkthrough-builder skill, "re-login any session AFTER its wallet is created".
$fcAdminWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Forestry Certification Admin" `
    -Headers $fcAdminSession.Headers `
    -FetchPublicKey
$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $fcOrgId `
    -WalletAddress $fcAdminWallet.Address `
    -DisplayName "Forestry Certification Admin" `
    -Headers $fcAdminSession.Headers
$fcAdminSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $fcAdminEmail `
    -Password $fcAdminPassword `
    -OrganizationId $fcOrgId

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Forestry Certification Register" `
    -Description "Digital Product Passports for verifiably-sustainable timber batches" `
    -TenantId $fcOrgId `
    -OwnerUserId $fcAdminSession.UserId `
    -OwnerWalletAddress $fcAdminWallet.Address `
    -Headers $fcAdminSession.Headers `
    -WalletSignerHeaders $fcAdminSession.Headers `
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
        -Headers $fcAdminSession.Headers
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
        -Headers $htAdminSession.Headers
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
    -Headers $fcAdminSession.Headers `
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
        # Participant roles point at the participant USER, not the org admin —
        # so when run.ps1 logs in as 'auditor' it lands on the user that owns
        # the auditor wallet. Logging in as the org admin instead would still
        # see the wallet (admin can list everything in their org) but the demo
        # narrative ("the Auditor signs the audit") is clearer when the actual
        # auditor user is the one signing.
        auditor = @{
            email          = $auditorUser.Email
            password       = $auditorUser.Password
            organizationId = $fcOrgId
            walletAddress  = $auditorWallet.Address
        }
        salesMgr = @{
            email          = $salesMgrUser.Email
            password       = $salesMgrUser.Password
            organizationId = $htOrgId
            walletAddress  = $salesMgrWallet.Address
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "ForestryCertification — Setup Complete"
