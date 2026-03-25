#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# MedicalEquipmentRefurb — Setup
# Bootstrap org, create 4 wallets, register participant, publish participants, create register, publish blueprint.

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "MedicalEquipmentRefurb — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "medical-equipment"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# Step 1: Bootstrap primary org (City General Hospital manages the walkthrough)
Write-WtStep "Step 1: Bootstrap Organization"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "City General Hospital" `
    -OrgSubdomain "city-general" `
    -AdminEmail $secrets.hospitalAdminEmail `
    -AdminName "Biomedical Engineer" `
    -AdminPassword $secrets.hospitalAdminPassword

# Step 2: Create wallets for all 4 participants
Write-WtStep "Step 2: Create Wallets (4 participants)"
$wallets = @{}
$publicKeys = @{}
$participantDefs = @(
    @{ id = "biomedical-engineer"; name = "Biomedical Engineer Wallet" }
    @{ id = "department-head"; name = "Department Head Wallet" }
    @{ id = "lead-technician"; name = "Lead Technician Wallet" }
    @{ id = "compliance-officer"; name = "Compliance Officer Wallet" }
)

foreach ($p in $participantDefs) {
    $w = New-SorchaWallet -WalletUrl $env.WalletUrl -Name $p.name -Headers $admin.Headers -FetchPublicKey
    $wallets[$p.id] = $w.Address
    $publicKeys[$p.id] = $w.PublicKey
    Write-WtInfo "  $($p.id) -> $($w.Address)"
}

# Step 3: Register participant + link primary wallet
Write-WtStep "Step 3: Register Participant"
$participant = Register-SorchaParticipant `
    -TenantUrl $env.TenantUrl -WalletUrl $env.WalletUrl `
    -OrganizationId $admin.OrganizationId `
    -WalletAddress $wallets["biomedical-engineer"] `
    -DisplayName "Biomedical Engineer" `
    -Headers $admin.Headers

# Step 4: Create register
Write-WtStep "Step 4: Create Register"
$register = New-SorchaRegister `
    -RegisterUrl $env.RegisterUrl -WalletUrl $env.WalletUrl `
    -Name "Medical Equipment Register" `
    -Description "Register for the medical equipment refurbishment walkthrough" `
    -TenantId $admin.OrganizationId `
    -OwnerUserId $admin.AdminUserId `
    -OwnerWalletAddress $wallets["biomedical-engineer"] `
    -Headers $admin.Headers `
    -Metadata @{ createdBy = "MedicalEquipmentRefurb/setup.ps1" }

# Step 5: Publish participants to register
Write-WtStep "Step 5: Publish Participants to Register"
$publishDefs = @(
    @{ role = "biomedical-engineer"; name = "Biomedical Engineer"; org = "City General Hospital" }
    @{ role = "department-head"; name = "Department Head"; org = "City General Hospital" }
    @{ role = "lead-technician"; name = "Lead Technician"; org = "MedTech Refurbishment Ltd" }
    @{ role = "compliance-officer"; name = "Compliance Officer"; org = "Regional Health Authority" }
)

foreach ($pd in $publishDefs) {
    $null = Publish-SorchaParticipant `
        -TenantUrl $env.TenantUrl `
        -OrganizationId $admin.OrganizationId `
        -RegisterId $register.RegisterId `
        -ParticipantName $pd.name `
        -OrganizationName $pd.org `
        -WalletAddress $wallets[$pd.role] `
        -PublicKey $publicKeys[$pd.role] `
        -Headers $admin.Headers
}

Write-WtInfo "Waiting 15s for docket processing..."
Start-Sleep -Seconds 15

# Step 6: Publish blueprint
Write-WtStep "Step 6: Publish Blueprint"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $env.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "medical-equipment-refurb-template.json") `
    -WalletMap $wallets `
    -Headers $admin.Headers `
    -IdPrefix "medical-equip" `
    -RegisterId $register.RegisterId

# Save state
$state = @{
    profile        = $Profile
    organizationId = $admin.OrganizationId
    adminToken     = $admin.Token
    wallets        = $wallets
    registerId     = $register.RegisterId
    blueprintId    = $blueprint.BlueprintId
    blueprintUrl   = $env.BlueprintUrl
}

$stateFile = Join-Path $scriptDir "state.json"
$state | ConvertTo-Json -Depth 5 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtSuccess "Setup complete — 4 wallets, participants published, register, blueprint"
Write-WtInfo "Run: pwsh walkthroughs/MedicalEquipmentRefurb/run.ps1"
