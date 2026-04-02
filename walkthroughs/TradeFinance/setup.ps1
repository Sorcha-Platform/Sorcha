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
    [ValidateSet('gateway', 'direct', 'aspire')]
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

$secrets = Get-SorchaSecrets -WalkthroughName "trade-finance"
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
    -AdminEmail $secrets.seedAdminEmail `
    -AdminName "Seed Admin" `
    -AdminPassword $secrets.seedAdminPassword

$orgContexts = @{}

foreach ($org in $selectedOrgs) {
    $subdomain = $org.subdomain

    if ($state.organizations.ContainsKey($subdomain) -and $state.organizations[$subdomain]) {
        $orgId = $state.organizations[$subdomain]
        Write-WtInfo "Organisation '$($org.name)' already exists: $orgId"
    } else {
        $adminEmail = "admin@$subdomain.sorcha.dev"

        # Register the admin user on the public org first so PlatformUser exists
        try {
            $null = Register-SorchaPublicUser `
                -TenantUrl $env.TenantUrl `
                -Email $adminEmail `
                -Password (Get-AdminPassword -OrgSubdomain $subdomain) `
                -DisplayName "$($org.name) Admin"
        } catch {
            # User may already exist from a previous run — continue
            Write-WtInfo "  Admin user $adminEmail may already exist — continuing"
        }

        # Create the private org via platform admin API
        try {
            $newOrg = New-SorchaOrganization `
                -TenantUrl $env.TenantUrl `
                -Name $org.name `
                -Subdomain $subdomain `
                -AdminEmail $adminEmail `
                -Headers $seedAdmin.Headers `
                -Description "TradeFinance walkthrough - $($org.role)"

            $orgId = $newOrg.OrganizationId
            $state.organizations[$subdomain] = $orgId
            Write-WtSuccess "Organisation '$($org.name)' created: $orgId"
        } catch {
            # Org may already exist — look it up by listing platform orgs
            Write-WtInfo "  Organisation '$($org.name)' may already exist — looking up..."
            $allOrgsResponse = Invoke-SorchaApi -Method GET -Uri "$($env.TenantUrl)/platform/organizations?page=1&pageSize=50" -Headers $seedAdmin.Headers
            $allOrgs = if ($allOrgsResponse.items) { $allOrgsResponse.items } else { @($allOrgsResponse) }
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
            # Register platform user first (may already exist from previous run)
            try {
                $null = Register-SorchaPublicUser `
                    -TenantUrl $env.TenantUrl `
                    -Email $email `
                    -Password $password `
                    -DisplayName $participant.displayName
            } catch {
                Write-WtInfo "  User $email may already exist — continuing"
            }

            # Add user to the org
            $userId = Get-OrCreateUser `
                -TenantUrl $env.TenantUrl `
                -OrganizationId $ctx.OrganizationId `
                -Email $email `
                -DisplayName $participant.displayName `
                -Headers $ctx.Headers `
                -Roles @("Consumer")

            Write-WtInfo "  User created: $($participant.displayName) ($email)"
        } else {
            Write-WtInfo "  User '$($participant.displayName)' already in state"
        }

        # Create wallet
        if ($state.wallets.ContainsKey($walletKey) -and $state.wallets[$walletKey]) {
            Write-WtInfo "  Wallet for '$partId' already exists: $($state.wallets[$walletKey])"
        } else {
            $wallet = New-SorchaWallet `
                -WalletUrl $env.WalletUrl `
                -Name "$($participant.displayName) Wallet" `
                -Headers $ctx.Headers `
                -Algorithm $participant.algorithm `
                -FetchPublicKey

            $state.wallets[$walletKey] = $wallet.Address
            Write-WtInfo "  Wallet: $partId -> $($wallet.Address)"
        }

        # Store role info
        $state.roles[$roleKey] = @{
            organizationId = $ctx.OrganizationId
            walletAddress  = $state.wallets[$walletKey]
            orgKey         = $subdomain
            email          = $email
            password       = $password
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

        # Use org admin headers — wallet-link endpoints require admin role
        $result = Register-SorchaParticipant `
            -TenantUrl $env.TenantUrl `
            -WalletUrl $env.WalletUrl `
            -OrganizationId $ctx.OrganizationId `
            -WalletAddress $roleInfo.walletAddress `
            -DisplayName $participant.displayName `
            -Headers $ctx.Headers

        $state.roles[$partId].participantId = $result.ParticipantId
        Write-WtInfo "  $partId -> participant: $($result.ParticipantId)"
    }
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
    $ownerOrg = $selectedOrgs | Where-Object { $_.subdomain -eq $ownerSubdomain } | Select-Object -First 1
    $firstParticipant = $ownerOrg.participants[0]
    $ownerWalletAddress = $state.wallets[$firstParticipant.id]

    Write-WtInfo "  Creating register '$($regDef.name)' (owner: $ownerSubdomain/$($firstParticipant.id))..."

    $register = New-SorchaRegister `
        -RegisterUrl $env.RegisterUrl `
        -WalletUrl $env.WalletUrl `
        -Name $regDef.name `
        -Description $regDef.purpose `
        -TenantId $ctx.OrganizationId `
        -OwnerUserId $ctx.AdminUserId `
        -OwnerWalletAddress $ownerWalletAddress `
        -Headers $ctx.Headers `
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

    # Build wallet map from all participants across all selected orgs
    $walletMap = @{}
    foreach ($w in $state.wallets.GetEnumerator()) {
        $walletMap[$w.Key] = $w.Value
    }

    Write-WtInfo "  Publishing blueprint '$bpShortName' to register $registerId..."

    $blueprint = Publish-SorchaBlueprint `
        -BlueprintUrl $env.BlueprintUrl `
        -TemplatePath $templatePath `
        -WalletMap $walletMap `
        -Headers $ctx.Headers `
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
