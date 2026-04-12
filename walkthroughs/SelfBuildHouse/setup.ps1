#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# SelfBuildHouse — Setup
# Bootstrap org, create 7 wallets, 2 registers, publish 2 blueprints.
# Uses single-org model with admin token for all participants (simplified).

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "SelfBuildHouse — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "self-build-house"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# Step 1: Bootstrap primary org
Write-WtStep "Step 1: Bootstrap Organisation"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "Highland Council" `
    -OrgSubdomain "highland-council" `
    -AdminEmail $secrets.highlandAdminEmail `
    -AdminName "Planning Admin" `
    -AdminPassword $secrets.highlandAdminPassword

# Step 2: Create wallets for all 7 participants across 6 organisations
Write-WtStep "Step 2: Create Wallets (7 participants)"
$wallets = @{}
$participantDefs = @(
    @{ id = "self-builder"; name = "Self-Builder Wallet" }
    @{ id = "structural-engineer"; name = "Structural Engineer Wallet" }
    @{ id = "ecologist"; name = "Ecologist Wallet" }
    @{ id = "utilities-officer"; name = "Scottish Water Wallet" }
    @{ id = "planning-officer"; name = "Planning Officer Wallet" }
    @{ id = "building-standards-officer"; name = "Building Standards Officer Wallet" }
    @{ id = "building-inspector"; name = "Building Inspector Wallet" }
)

foreach ($p in $participantDefs) {
    $w = New-SorchaWallet -WalletUrl $env.WalletUrl -Name $p.name -Headers $admin.Headers -FetchPublicKey
    $wallets[$p.id] = $w.Address
    Write-WtInfo "  $($p.id) -> $($w.Address)"
}

# Step 3: Register participant + link primary wallet
Write-WtStep "Step 3: Register Participant"
$participant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl -WalletUrl $env.WalletUrl `
    -OrganizationId $admin.OrganizationId `
    -WalletAddress $wallets["self-builder"] `
    -DisplayName "Self-Builder" `
    -Headers $admin.Headers

# Step 4: Create TWO registers (planning + building standards)
Write-WtStep "Step 4: Create Registers (2)"

Write-WtInfo "  Creating Highland Planning Register..."
$planningRegister = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl -TenantUrl $env.TenantUrl `
    -Name "Highland Planning Register" `
    -Description "Planning applications, consultations, and decisions for the Highland Council area" `
    -TenantId $admin.OrganizationId `
    -OwnerUserId $admin.AdminUserId `
    -OwnerWalletAddress $wallets["planning-officer"] `
    -Headers $admin.Headers `
    -Metadata @{ createdBy = "SelfBuildHouse/setup.ps1"; registerType = "planning" }
Write-WtSuccess "  Planning Register: $($planningRegister.RegisterId)"

Write-WtInfo "  Creating Highland Building Standards Register..."
$buildingRegister = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl -TenantUrl $env.TenantUrl `
    -Name "Highland Building Standards Register" `
    -Description "Building warrants, staged inspections, and completion certificates for the Highland Council area" `
    -TenantId $admin.OrganizationId `
    -OwnerUserId $admin.AdminUserId `
    -OwnerWalletAddress $wallets["building-standards-officer"] `
    -Headers $admin.Headers `
    -Metadata @{ createdBy = "SelfBuildHouse/setup.ps1"; registerType = "building-standards" }
Write-WtSuccess "  Building Standards Register: $($buildingRegister.RegisterId)"

# Public org subscription so consumer users see both registers in their list.
# The Connect-SorchaAdmin call used the seed sysadmin, which carries the
# SystemAdmin role — so it can add itself to the Public org and subscribe.
$null = Add-SorchaPublicOrgSubscription `
    -TenantUrl $env.TenantUrl `
    -RegisterId $planningRegister.RegisterId `
    -RegisterName "Highland Planning Register" `
    -SysAdminHeaders $admin.Headers `
    -SysAdminEmail $secrets.highlandAdminEmail
$null = Add-SorchaPublicOrgSubscription `
    -TenantUrl $env.TenantUrl `
    -RegisterId $buildingRegister.RegisterId `
    -RegisterName "Highland Building Standards Register" `
    -SysAdminHeaders $admin.Headers `
    -SysAdminEmail $secrets.highlandAdminEmail

# Step 5: Publish TWO blueprints
Write-WtStep "Step 5: Publish Blueprints (2)"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-WtInfo "  Publishing Planning Permission blueprint..."
$planningBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "planning-permission-template.json") `
    -WalletMap $wallets `
    -Headers $admin.Headers `
    -IdPrefix "self-build-planning" `
    -RegisterId $planningRegister.RegisterId
Write-WtSuccess "  Planning Blueprint: $($planningBlueprint.BlueprintId)"

Write-WtInfo "  Publishing Building Warrant blueprint..."
$warrantBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "building-warrant-template.json") `
    -WalletMap $wallets `
    -Headers $admin.Headers `
    -IdPrefix "self-build-warrant" `
    -RegisterId $buildingRegister.RegisterId
Write-WtSuccess "  Warrant Blueprint: $($warrantBlueprint.BlueprintId)"

# Save state
$state = @{
    profile                = $Profile
    organizationId         = $admin.OrganizationId
    adminToken             = $admin.Token
    wallets                = $wallets
    planningRegisterId     = $planningRegister.RegisterId
    buildingRegisterId     = $buildingRegister.RegisterId
    planningBlueprintId    = $planningBlueprint.BlueprintId
    warrantBlueprintId     = $warrantBlueprint.BlueprintId
    blueprintUrl           = $env.BlueprintUrl
    walletUrl              = $env.WalletUrl
}

$stateFile = Join-Path $scriptDir "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — 7 wallets, 2 registers, 2 blueprints published"
Write-WtInfo "Run: pwsh walkthroughs/SelfBuildHouse/run.ps1"
