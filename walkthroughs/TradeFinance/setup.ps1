#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# TradeFinance — Setup
# Bootstrap 4 organisations, create wallets and participants per org,
# create 2 registers, publish 2 blueprints, generate MCP configs.
# Supports single-machine (all orgs) and multi-machine (-Organizations) modes.
# Idempotent — re-running skips existing resources via state.json.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$Organizations = '',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-WtBanner "TradeFinance — Setup"

# ============================================================================
# Load config and secrets
# ============================================================================

$configPath = Join-Path $scriptDir "config.json"
if (-not (Test-Path $configPath)) {
    throw "Config file not found: $configPath"
}
$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json -Depth 20

$secrets = Get-SorchaSecrets -WalkthroughName "trade-finance" -Profile $Profile
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Determine selected organisations
# ============================================================================

$allOrgs = $config.organizations
if ($Organizations -and $Organizations.Trim() -ne '') {
    $selectedSubdomains = $Organizations.Split(',') | ForEach-Object { $_.Trim() }
    $selectedOrgs = $allOrgs | Where-Object { $selectedSubdomains -contains $_.subdomain }
    if ($selectedOrgs.Count -eq 0) {
        throw "No matching organisations found for: $Organizations. Available: $($allOrgs | ForEach-Object { $_.subdomain } | Join-String -Separator ', ')"
    }
    Write-WtInfo "Selected organisations: $($selectedOrgs | ForEach-Object { $_.subdomain } | Join-String -Separator ', ')"
} else {
    $selectedOrgs = $allOrgs
    Write-WtInfo "All organisations selected (single-machine mode)"
}

# ============================================================================
# Load or initialise state
# ============================================================================

$stateFile = Join-Path $scriptDir "state.json"
if (Test-Path $stateFile) {
    $state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json -Depth 20 -AsHashtable
    Write-WtInfo "Loaded existing state from state.json"
} else {
    $state = @{
        profile      = $Profile
        gatewayUrl   = $env.GatewayUrl
        organizations = @{}
        registers    = @{}
        blueprints   = @{}
        wallets      = @{}
        roles        = @{}
    }
}

# Ensure top-level keys exist (in case state was partially initialised)
foreach ($key in @('organizations', 'registers', 'blueprints', 'wallets', 'roles')) {
    if (-not $state.ContainsKey($key)) { $state[$key] = @{} }
}
$state.profile = $Profile
$state.gatewayUrl = $env.GatewayUrl

# Helper to generate a password for a participant
function Get-ParticipantPassword {
    param([string]$OrgSubdomain, [string]$ParticipantId)
    $secretKey = "$($OrgSubdomain)_$($ParticipantId.Replace('-', '_'))_password"
    if ($secrets.ContainsKey($secretKey)) {
        return $secrets[$secretKey]
    }
    # Fallback: generate deterministic password from secrets admin password + participant id
    $adminKey = "$($OrgSubdomain)_admin_password"
    if ($secrets.ContainsKey($adminKey)) {
        return $secrets[$adminKey]
    }
    # Last resort: use a default pattern
    return "Wt-$OrgSubdomain-$ParticipantId-2026!"
}

function Get-AdminPassword {
    param([string]$OrgSubdomain)
    $secretKey = "$($OrgSubdomain)_admin_password"
    if ($secrets.ContainsKey($secretKey)) {
        return $secrets[$secretKey]
    }
    return "Wt-$OrgSubdomain-admin-2026!"
}

# ============================================================================
# Step 1: Bootstrap Organisations
# ============================================================================

Write-WtStep "Step 1: Bootstrap Organisations ($($selectedOrgs.Count))"

# First, login as seed admin to create private orgs
$seedAdmin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminName "Seed Admin" `
    -AdminPassword $secrets.adminPassword

# Enable public org registration (required for Register-SorchaPublicUser to work)
# and raise maxOrgsPerUser (users need public org + their private org)
try {
    $null = Invoke-SorchaApi -Method PUT `
        -Uri "$($env.TenantUrl)/platform/settings/public-org" `
        -Body @{ enabled = $true } `
        -Headers $seedAdmin.Headers
    $null = Invoke-SorchaApi -Method PUT `
        -Uri "$($env.TenantUrl)/platform/settings/max-orgs" `
        -Body @{ maxOrgsPerUser = 10 } `
        -Headers $seedAdmin.Headers
    Write-WtInfo "Platform settings configured (public org enabled, maxOrgsPerUser=10)"
} catch {
    Write-WtWarn "Could not configure platform settings — user registration may fail"
}

$orgContexts = @{}

foreach ($org in $selectedOrgs) {
    $subdomain = $org.subdomain

    if ($state.organizations.ContainsKey($subdomain) -and $state.organizations[$subdomain]) {
        $orgId = $state.organizations[$subdomain]
        Write-WtInfo "Organisation '$($org.name)' already exists: $orgId"
    } else {
        $adminEmail = "admin@$subdomain.sorcha.dev"

        # Spec 136: provision the org admin directly as an org-scoped, verified password user
        # in one call — single-org (no public account → no multi-org clash → the OAuth2 password
        # grant works), and no SMTP invite. The verified bypass requires the installation to enable
        # Platform:AllowAdminVerifiedUserCreation (dev + n1 do; production does not).
        try {
            $newOrg = New-SorchaOrganization `
                -TenantUrl $env.TenantUrl `
                -WalletUrl $env.WalletUrl `
                -Name $org.name `
                -Subdomain $subdomain `
                -AdminEmail $adminEmail `
                -AdminPassword (Get-AdminPassword -OrgSubdomain $subdomain) `
                -AdminDisplayName "$($org.name) Admin" `
                -AdminEmailVerified `
                -Headers $seedAdmin.Headers `
                -Description "TradeFinance walkthrough - $($org.role)"

            $orgId = $newOrg.OrganizationId
            $state.organizations[$subdomain] = $orgId
            Write-WtSuccess "Organisation '$($org.name)' created: $orgId"
        } catch {
            # Org may already exist — look it up by listing platform orgs
            Write-WtInfo "  Organisation '$($org.name)' may already exist — looking up..."
            $allOrgsResponse = Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $seedAdmin.Headers
            $allOrgs = Resolve-SorchaCollection -Response $allOrgsResponse -PropertyName 'items'
            $existing = $allOrgs | Where-Object { $_.subdomain -eq $subdomain }
            if ($existing) {
                $orgId = $existing.id
                $state.organizations[$subdomain] = $orgId
                Write-WtInfo "  Found existing org: $orgId"
            } else {
                throw "Could not create or find organisation '$($org.name)': $($_.Exception.Message)"
            }
        }
    }

    # Login as the org admin to get org-scoped token
    $adminEmail = "admin@$subdomain.sorcha.dev"
    $adminPassword = Get-AdminPassword -OrgSubdomain $subdomain

    $orgAdmin = Connect-SorchaUser `
        -TenantUrl $env.TenantUrl `
        -Email $adminEmail `
        -Password $adminPassword `
        -OrganizationId $orgId

    $orgContexts[$subdomain] = @{
        OrganizationId = $orgId
        AdminToken     = $orgAdmin.Token
        AdminUserId    = $orgAdmin.UserId
        Headers        = $orgAdmin.Headers
        Subdomain      = $subdomain
        OrgDef         = $org
    }

    Write-WtInfo "  $subdomain -> org: $orgId, admin: $($orgAdmin.UserId)"
}

# ============================================================================
# Step 2: Create Users and Wallets
# ============================================================================

Write-WtStep "Step 2: Create Users and Wallets"

foreach ($org in $selectedOrgs) {
    $subdomain = $org.subdomain
    $ctx = $orgContexts[$subdomain]

    Write-WtInfo "Organisation: $($org.name) ($subdomain)"

    foreach ($participant in $org.participants) {
        $partId = $participant.id
        $walletKey = $partId

        # Create user if not already tracked
        $roleKey = $partId
        $email = "$partId@$subdomain.sorcha.dev"
        $password = Get-ParticipantPassword -OrgSubdomain $subdomain -ParticipantId $partId

        if (-not ($state.roles.ContainsKey($roleKey) -and $state.roles[$roleKey].email)) {
            # Spec 136: provision the participant org-scoped + verified in one call (single-org —
            # no public account, no multi-org clash). Org operators are never public users.
            try {
                $provisioned = New-SorchaOrgUser `
                    -TenantUrl $env.TenantUrl `
                    -OrganizationId $ctx.OrganizationId `
                    -Email $email `
                    -Password $password `
                    -DisplayName $participant.displayName `
                    -Headers $ctx.Headers `
                    -Roles @("Consumer") `
                    -EmailVerified
                $userId = $provisioned.UserId
                Write-WtInfo "  User provisioned (org-scoped): $($participant.displayName) ($email)"
            } catch {
                Write-WtInfo "  User $email may already exist — continuing"
            }
        } else {
            Write-WtInfo "  User '$($participant.displayName)' already in state"
        }

        # Login as THIS participant so the wallet is owned by them, not by the
        # org admin. The earlier shape (admin headers everywhere) made every
        # participant wallet appear in the org admin's wallet list and left the
        # participant users walletless when they logged in.
        $participantSession = Connect-SorchaUser `
            -TenantUrl $env.TenantUrl `
            -Email $email `
            -Password $password `
            -OrganizationId $ctx.OrganizationId

        if ($state.wallets.ContainsKey($walletKey) -and $state.wallets[$walletKey]) {
            Write-WtInfo "  Wallet for '$partId' already exists: $($state.wallets[$walletKey])"
        } else {
            # Wallet name = participant displayName (no " Wallet" suffix). The
            # name is the idempotency key for New-SorchaWallet — keeping it
            # aligned with other walkthroughs (ForestryCertification uses
            # "Sales Manager" too) means the same user across walkthroughs
            # gets ONE wallet, and credentials issued by one walkthrough are
            # visible to another (e.g. ForestryCertification's
            # ForestProductDPPCredential reaches TradeFinance's Invoice
            # Finance phase).
            $wallet = New-SorchaWallet `
                -WalletUrl $env.WalletUrl `
                -Name $participant.displayName `
                -Headers $participantSession.Headers `
                -Algorithm $participant.algorithm `
                -FetchPublicKey

            $state.wallets[$walletKey] = $wallet.Address
            Write-WtInfo "  Wallet: $partId -> $($wallet.Address) (owned by $email)"
        }

        # Store role info. participantHeaders + userId are the participant's
        # own session — Step 3 (Register Participants) and Step 4 (Owner
        # attestation signing) need them so calls run as the participant USER.
        # Without that, /me/...self-register registers the admin as every
        # participant and the wallet sign call rejects the admin trying to
        # sign with a wallet it no longer owns.
        $state.roles[$roleKey] = @{
            organizationId      = $ctx.OrganizationId
            walletAddress       = $state.wallets[$walletKey]
            orgKey              = $subdomain
            email               = $email
            password            = $password
            userId              = $userId
            participantHeaders  = $participantSession.Headers
        }
    }
}

# ============================================================================
# Step 3: Register Participants
# ============================================================================

Write-WtStep "Step 3: Register Participants"

foreach ($org in $selectedOrgs) {
    $subdomain = $org.subdomain
    $ctx = $orgContexts[$subdomain]

    foreach ($participant in $org.participants) {
        $partId = $participant.id
        $roleInfo = $state.roles[$partId]

        if ($roleInfo.ContainsKey('participantId') -and $roleInfo.participantId) {
            Write-WtInfo "  Participant '$partId' already registered: $($roleInfo.participantId)"
            continue
        }

        # Use the participant's OWN headers — `/me/...self-register` registers
        # the current caller, and `/v1/wallets/{addr}/sign` requires the wallet
        # owner. Both are now the participant user, not the org admin.
        $result = Register-SorchaParticipant `
            -TenantUrl $env.TenantUrl `
            -WalletUrl $env.WalletUrl `
            -OrganizationId $ctx.OrganizationId `
            -WalletAddress $roleInfo.walletAddress `
            -DisplayName $participant.displayName `
            -Headers $roleInfo.participantHeaders

        $state.roles[$partId].participantId = $result.ParticipantId
        Write-WtInfo "  $partId -> participant: $($result.ParticipantId)"
    }
}

# ============================================================================
# Step 3b: Org-admin wallets (register owners + blueprint publishers)
# ============================================================================
# A register must NOT be owned by a Consumer participant. The owner + publisher must be an
# Administrator (CanPublishBlueprints policy) AND hold the register's governance-roster wallet
# (F142 PublishGate matches the caller's wallet_address against the roster owner wallet-DID). The
# org admin holds Administrator; give it a wallet + participant so it can own its register, then
# re-login so the JWT carries wallet_address (minted from the first linked wallet at login).
# See walkthrough-builder skill, "re-login any session AFTER its wallet is created".
Write-WtStep "Step 3b: Org-admin wallets (register owners)"

foreach ($org in $selectedOrgs) {
    $subdomain = $org.subdomain
    $ctx = $orgContexts[$subdomain]

    $adminWallet = New-SorchaWallet `
        -WalletUrl $env.WalletUrl `
        -Name "$($org.name) Admin" `
        -Headers $ctx.Headers `
        -FetchPublicKey
    try {
        $null = Register-SorchaParticipant `
            -TenantUrl $env.TenantUrl `
            -WalletUrl $env.WalletUrl `
            -OrganizationId $ctx.OrganizationId `
            -WalletAddress $adminWallet.Address `
            -DisplayName "$($org.name) Admin" `
            -Headers $ctx.Headers
    } catch { Write-WtInfo "  admin participant for $subdomain already registered" }

    $adminRelogin = Connect-SorchaUser `
        -TenantUrl $env.TenantUrl `
        -Email "admin@$subdomain.sorcha.dev" `
        -Password (Get-AdminPassword -OrgSubdomain $subdomain) `
        -OrganizationId $ctx.OrganizationId
    $ctx.Headers = $adminRelogin.Headers
    $ctx.AdminToken = $adminRelogin.Token
    $ctx.AdminWallet = $adminWallet.Address
    Write-WtInfo "  $subdomain admin wallet (register owner): $($adminWallet.Address)"

    # Provision the org's Feature 083 master key so its Feature 120 VC-issuance key can derive —
    # required for any credential this org issues to be cross-register-verifiable (otherwise the
    # credential is signed with the bare wallet key and the Finance phase's TrustEvaluator rejects
    # it: "issuer signature not verified"). Idempotent. See walkthrough-builder skill.
    Set-SorchaOrgMasterKey -WalletUrl $env.WalletUrl -OrganizationId $ctx.OrganizationId -Headers $ctx.Headers
}

# ============================================================================
# Step 4: Create Registers
# ============================================================================

Write-WtStep "Step 4: Create Registers ($($config.registers.Count))"

foreach ($regDef in $config.registers) {
    $regShortName = $regDef.name -replace '\s+', '-' | ForEach-Object { $_.ToLower() }

    # Check if ownerOrg is in selected orgs
    $ownerSubdomain = $regDef.ownerOrg
    $ownerSelected = $selectedOrgs | Where-Object { $_.subdomain -eq $ownerSubdomain }

    if (-not $ownerSelected) {
        Write-WtWarn "  Register '$($regDef.name)' owner org '$ownerSubdomain' not in selected orgs — skipping (available via replication)"
        continue
    }

    if ($state.registers.ContainsKey($regShortName) -and $state.registers[$regShortName].id) {
        Write-WtInfo "  Register '$($regDef.name)' already exists: $($state.registers[$regShortName].id)"
        continue
    }

    $ctx = $orgContexts[$ownerSubdomain]
    # Register owner = the org ADMIN's wallet (Administrator + roster owner), NOT a Consumer
    # participant. Both -Headers and -WalletSignerHeaders are the (re-logged) admin, who owns
    # $ctx.AdminWallet, so the owner attestation is signed by the wallet it names.
    $ownerWalletAddress = $ctx.AdminWallet

    Write-WtInfo "  Creating register '$($regDef.name)' (owner: $ownerSubdomain admin)..."

    $register = New-SorchaRegister `
        -RegisterUrl $env.RegisterUrl `
        -WalletUrl $env.WalletUrl `
        -Name $regDef.name `
        -Description $regDef.purpose `
        -TenantId $ctx.OrganizationId `
        -OwnerUserId $ctx.AdminUserId `
        -OwnerWalletAddress $ownerWalletAddress `
        -Headers $ctx.Headers `
        -WalletSignerHeaders $ctx.Headers `
        -DevMode `
        -Metadata @{ createdBy = "TradeFinance/setup.ps1"; registerType = $regDef.ownerOrg }

    $state.registers[$regShortName] = @{
        id   = $register.RegisterId
        name = $regDef.name
    }

    # Subscribe the owning org to its register
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $ctx.OrganizationId `
        -RegisterId $register.RegisterId `
        -Headers $ctx.Headers `
        -RegisterName $regDef.name `
        -SubscriptionType "Owner"

    # Public org subscription so consumer users see the register.
    $null = Add-SorchaPublicOrgSubscription `
        -TenantUrl $env.TenantUrl `
        -RegisterId $register.RegisterId `
        -RegisterName $regDef.name `
        -SysAdminHeaders $seedAdmin.Headers `
        -SysAdminEmail $secrets.adminEmail

    Write-WtSuccess "  Register '$($regDef.name)': $($register.RegisterId)"
}

# ============================================================================
# Step 5: Publish Blueprints
# ============================================================================

Write-WtStep "Step 5: Publish Blueprints"

foreach ($regDef in $config.registers) {
    $regShortName = $regDef.name -replace '\s+', '-' | ForEach-Object { $_.ToLower() }
    $templateName = $regDef.template
    $bpShortName = $templateName -replace '-template\.json$', ''

    $ownerSubdomain = $regDef.ownerOrg
    $ownerSelected = $selectedOrgs | Where-Object { $_.subdomain -eq $ownerSubdomain }

    if (-not $ownerSelected) {
        Write-WtInfo "  Skipping blueprint for '$($regDef.name)' — owner org not selected"
        continue
    }

    if (-not $state.registers.ContainsKey($regShortName)) {
        Write-WtWarn "  Register '$($regDef.name)' not in state — skipping blueprint"
        continue
    }

    if ($state.blueprints.ContainsKey($bpShortName) -and $state.blueprints[$bpShortName].id) {
        Write-WtInfo "  Blueprint '$bpShortName' already published: $($state.blueprints[$bpShortName].id)"
        continue
    }

    $templatePath = Join-Path $scriptDir $templateName
    if (-not (Test-Path $templatePath)) {
        Write-WtWarn "  Template not found: $templatePath — skipping"
        continue
    }

    $ctx = $orgContexts[$ownerSubdomain]
    $registerId = $state.registers[$regShortName].id

    # Re-login the owner admin FRESH right here so the publish token definitely carries the
    # wallet_address claim (F142 PublishGate matches it against the register's roster owner). The
    # owner admin holds Administrator (CanPublishBlueprints) and owns the register wallet (Step 3b),
    # so a fresh token clears both gates. Done inline rather than reusing a step-old session token.
    $publisher = Connect-SorchaUser `
        -TenantUrl $env.TenantUrl `
        -Email "admin@$ownerSubdomain.sorcha.dev" `
        -Password (Get-AdminPassword -OrgSubdomain $ownerSubdomain) `
        -OrganizationId $ctx.OrganizationId
    # The register's genesis control tx — which records the owner governance roster — seals
    # asynchronously AFTER New-SorchaRegister returns. The F142 publish gate reads that roster and
    # fail-closes with a 403 ("no publish-governance role") when it isn't recorded yet. Wait for the
    # roster to populate before publishing (the register-genesis analogue of the F145 action-seal
    # cadence). Unlike ConstructionPermit/Forestry there are no intervening on-register steps here,
    # so the publish would otherwise race the seal.
    $rosterReady = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $roster = Invoke-SorchaApi -Method GET `
                -Uri "$($env.RegisterUrl)/registers/$registerId/governance/roster" `
                -Headers $publisher.Headers
            if ($roster.members -and $roster.members.Count -gt 0) { $rosterReady = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $rosterReady) { Write-WtWarn "  register $registerId governance roster not populated after 30s" }

    # Build wallet map from all participants across all selected orgs.
    # Publish-SorchaBlueprint auto-skips any participant that is the sender
    # of an open starting action (isStartingAction: true), so we don't need
    # to filter the map here.
    $walletMap = @{}
    foreach ($w in $state.wallets.GetEnumerator()) {
        $walletMap[$w.Key] = $w.Value
    }

    Write-WtInfo "  Publishing blueprint '$bpShortName' to register $registerId..."

    $blueprint = Publish-SorchaBlueprint `
        -BlueprintUrl $env.BlueprintUrl `
        -TemplatePath $templatePath `
        -WalletMap $walletMap `
        -Headers $publisher.Headers `
        -IdPrefix "trade-$bpShortName" `
        -RegisterId $registerId

    $state.blueprints[$bpShortName] = @{
        id         = $blueprint.BlueprintId
        registerId = $registerId
    }

    Write-WtSuccess "  Blueprint '$bpShortName': $($blueprint.BlueprintId)"
}

# ============================================================================
# Step 6: Generate MCP Configs
# ============================================================================

Write-WtStep "Step 6: Generate MCP Configs"

$mcpTemplateDir = Join-Path $scriptDir "mcp-configs"
$mcpTemplatePath = Join-Path $mcpTemplateDir "template.json"
$mcpGeneratedDir = Join-Path $mcpTemplateDir "generated"

if (-not (Test-Path $mcpTemplatePath)) {
    Write-WtWarn "MCP template not found at $mcpTemplatePath — skipping MCP config generation"
} else {
    if (-not (Test-Path $mcpGeneratedDir)) {
        New-Item -Path $mcpGeneratedDir -ItemType Directory -Force | Out-Null
        Write-WtInfo "Created directory: $mcpGeneratedDir"
    }

    $mcpTemplate = Get-Content -Path $mcpTemplatePath -Raw

    foreach ($org in $selectedOrgs) {
        $subdomain = $org.subdomain
        $ctx = $orgContexts[$subdomain]

        foreach ($participant in $org.participants) {
            $partId = $participant.id
            $roleInfo = $state.roles[$partId]

            # Get a JWT for this participant
            $participantToken = ""
            try {
                $userCtx = Connect-SorchaUser `
                    -TenantUrl $env.TenantUrl `
                    -Email $roleInfo.email `
                    -Password $roleInfo.password `
                    -OrganizationId $ctx.OrganizationId
                $participantToken = $userCtx.Token
            } catch {
                Write-WtWarn "  Could not get token for $partId — using admin token"
                $participantToken = $ctx.AdminToken
            }

            $mcpContent = $mcpTemplate `
                -replace 'PARTICIPANT_ID', $partId `
                -replace 'JWT_TOKEN_PLACEHOLDER', $participantToken `
                -replace 'GATEWAY_URL_PLACEHOLDER', $env.GatewayUrl

            $mcpOutputPath = Join-Path $mcpGeneratedDir "sorcha-$partId.json"
            $mcpContent | Set-Content -Path $mcpOutputPath -Encoding UTF8

            Write-WtSuccess "  MCP config: sorcha-$partId.json"
        }
    }
}

# ============================================================================
# Save state
# ============================================================================

# Strip in-memory session headers before serialising — they're per-run JWTs
# that don't belong in state.json.
foreach ($k in @($state.roles.Keys)) {
    if ($state.roles[$k] -is [hashtable] -and $state.roles[$k].ContainsKey('participantHeaders')) {
        $state.roles[$k].Remove('participantHeaders')
    }
}
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile -Encoding UTF8
Write-WtSuccess "State saved to state.json"

# ============================================================================
# Summary
# ============================================================================

$totalWallets = ($state.wallets.Keys | Measure-Object).Count
$totalRegisters = ($state.registers.Keys | Measure-Object).Count
$totalBlueprints = ($state.blueprints.Keys | Measure-Object).Count
$totalParticipants = ($state.roles.Keys | Where-Object { $state.roles[$_].participantId } | Measure-Object).Count

Write-WtBanner "Setup Complete"
Write-WtSuccess "Organisations: $($selectedOrgs.Count) bootstrapped"
Write-WtSuccess "Wallets:       $totalWallets created"
Write-WtSuccess "Participants:  $totalParticipants registered"
Write-WtSuccess "Registers:     $totalRegisters created"
Write-WtSuccess "Blueprints:    $totalBlueprints published"
Write-Host ""
Write-WtInfo "Next steps:"
Write-WtInfo "  Single machine:  pwsh walkthroughs/TradeFinance/run.ps1"
Write-WtInfo "  Multi-machine:   Copy mcp-configs/generated/sorcha-*.json to each participant's machine"
