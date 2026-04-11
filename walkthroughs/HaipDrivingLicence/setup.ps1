#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipDrivingLicence — Setup
# Creates a Council Licensing Authority org and publishes the driving licence blueprint.
# Checks for HaipIdentityAttestation and runs it if missing.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HaipDrivingLicence — Setup"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
$identityDir = Join-Path (Split-Path -Parent $scriptDir) "HaipIdentityAttestation"
$identityState = Join-Path $identityDir "state.json"

# Check if already set up
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json already exists. Use -Force to re-run setup."
    return
}

# --- Step 1: Ensure identity attestation is complete ---
Write-WtStep "Step 1: Checking for identity credential"
if (-not (Test-Path $identityState)) {
    Write-WtWarn "HaipIdentityAttestation not run — running it now"
    & (Join-Path $identityDir "setup.ps1") -Profile $Profile -SkipHealthCheck:$SkipHealthCheck
    & (Join-Path $identityDir "run.ps1")
}

$idState = Get-Content -Path $identityState -Raw | ConvertFrom-Json
Write-WtSuccess "Identity credential available from $identityDir"

$secrets = Get-SorchaSecrets -WalkthroughName "haip-licence"
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# --- Step 2: Connect as system admin ---
Write-WtStep "Step 2: Connecting as system admin"
$adminToken = Connect-SorchaAdmin -BaseUrl $env.BaseUrl -Secrets $secrets

# --- Step 3: Create Council Licensing Authority ---
Write-WtStep "Step 3: Creating Council Licensing Authority"
$councilOrg = Get-OrCreateOrganization -BaseUrl $env.BaseUrl -Token $adminToken `
    -Name "Council Licensing Authority" `
    -AdminEmail "council-admin@haip-walkthrough.local" `
    -AdminPassword $secrets.DefaultPassword

$councilOrgId = $councilOrg.id
Write-WtSuccess "Council org: $councilOrgId"

# --- Step 4: Create Council wallet ---
Write-WtStep "Step 4: Creating Council wallet"
$councilToken = Connect-SorchaUser -BaseUrl $env.BaseUrl `
    -Email "council-admin@haip-walkthrough.local" -Password $secrets.DefaultPassword `
    -OrganizationId $councilOrgId

$councilWallet = New-SorchaWallet -BaseUrl $env.BaseUrl -Token $councilToken `
    -Name "Council Licence Issuer" -Algorithm "ES256"

Write-WtSuccess "Council wallet: $($councilWallet.address)"

# --- Step 5: Enrol Council as HAIP issuer ---
Write-WtStep "Step 5: Enrolling Council as HAIP issuer"
try {
    $enrolResult = Invoke-SorchaApi -BaseUrl $env.BaseUrl -Token $adminToken `
        -Method POST `
        -Path "/api/v1/trust/tenants/$($env.TenantId)/orgs/$($councilWallet.address)/enrol" `
        -Body @{
            orgPublicKeyBase64 = $councilWallet.publicKey
            orgDisplayName = "Council Licensing Authority"
        }
    Write-WtSuccess "Council cert enrolled"
} catch {
    Write-WtWarn "Enrolment may already exist: $_"
}

# --- Step 6: Publish driving licence blueprint ---
Write-WtStep "Step 6: Publishing driving licence blueprint"
$blueprintPath = Join-Path $scriptDir "blueprints/driving-licence.json"
$blueprint = Get-Content -Path $blueprintPath -Raw | ConvertFrom-Json

# Create a register for the blueprint
$register = New-SorchaRegister -BaseUrl $env.BaseUrl -Token $councilToken `
    -Name "Driving Licence Register" -Description "HAIP walkthrough register"

$publishedBlueprint = Publish-SorchaBlueprint -BaseUrl $env.BaseUrl -Token $councilToken `
    -Blueprint $blueprint -RegisterId $register.id

Write-WtSuccess "Blueprint published: $($publishedBlueprint.id)"

# --- Save state ---
$state = @{
    tenantId = $env.TenantId
    councilOrgId = $councilOrgId
    councilOrgName = "Council Licensing Authority"
    councilWalletAddress = $councilWallet.address
    registerId = $register.id
    blueprintId = $publishedBlueprint.id
    identityStateFile = $identityState
    citizenEmail = $idState.citizenEmail
    citizenPassword = $idState.citizenPassword
    walletDir = $idState.walletDir
    baseUrl = $env.BaseUrl
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipDrivingLicence — Setup Complete"
