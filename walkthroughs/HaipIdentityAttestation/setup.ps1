#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipIdentityAttestation — Setup
# Creates a Government Identity Authority and a citizen user with persona data.
# Provisions trust anchor and enrols the Government org as a HAIP issuer.
#
# Idempotent — safe to run multiple times.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HaipIdentityAttestation — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "haip-identity"
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
$baseUrl = $sorchaEnv.GatewayUrl

# Check if already set up (idempotent)
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json already exists. Use -Force to re-run setup."
    $state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
    Write-WtSuccess "Setup already complete. Org: $($state.govOrgName)"
    return
}

# --- Step 1: Connect as system admin ---
Write-WtStep "Step 1: Connecting as system admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

$adminToken = $sysAdmin.Token
$tenantId = $sysAdmin.OrganizationId
Write-WtSuccess "Connected as admin (org: $tenantId)"

# --- Step 2: Create Government Identity Authority org ---
Write-WtStep "Step 2: Creating Government Identity Authority"
$govOrg = Get-OrCreateOrganization -BaseUrl $baseUrl -Token $adminToken `
    -Name "Government Identity Authority" `
    -AdminEmail "gov-admin@haip-walkthrough.local" `
    -AdminPassword $secrets.DefaultPassword

$govOrgId = $govOrg.id
Write-WtSuccess "Government org: $govOrgId"

# --- Step 3: Create citizen user ---
Write-WtStep "Step 3: Creating citizen user"
$citizenEmail = "alice.obrien@haip-walkthrough.local"
$citizenPassword = $secrets.DefaultPassword

try {
    Register-SorchaPublicUser -BaseUrl $baseUrl `
        -Email $citizenEmail -Password $citizenPassword -DisplayName "Alice O'Brien"
} catch { Write-WtInfo "Citizen user may already exist" }

try {
    Confirm-SorchaUserEmail -BaseUrl $baseUrl -Token $adminToken -Email $citizenEmail
} catch { Write-WtInfo "Email may already be confirmed" }

Write-WtSuccess "Citizen user: $citizenEmail"

# --- Step 4: Create Government wallet ---
Write-WtStep "Step 4: Creating Government wallet"
$govLogin = Connect-SorchaUser -BaseUrl $baseUrl `
    -Email "gov-admin@haip-walkthrough.local" -Password $secrets.DefaultPassword `
    -OrganizationId $govOrgId

$govWallet = New-SorchaWallet -BaseUrl $baseUrl -Token $govLogin.Token `
    -Name "Government Identity Issuer" -Algorithm "ES256"
Write-WtSuccess "Government wallet: $($govWallet.address)"

# --- Step 5: Provision trust anchor ---
Write-WtStep "Step 5: Provisioning trust anchor"
try {
    Invoke-SorchaApi -Method POST `
        -Uri "$baseUrl/api/v1/trust/tenants/$tenantId/provision" `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -Body @{}
    Write-WtSuccess "Trust anchor provisioned"
} catch { Write-WtWarn "Trust anchor may already exist" }

# --- Step 6: Enrol Government org as HAIP issuer ---
Write-WtStep "Step 6: Enrolling Government org as HAIP issuer"
try {
    Invoke-SorchaApi -Method POST `
        -Uri "$baseUrl/api/v1/trust/tenants/$tenantId/orgs/$($govWallet.address)/enrol" `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -Body @{
            orgPublicKeyBase64 = $govWallet.publicKey
            orgDisplayName = "Government Identity Authority"
        }
    Write-WtSuccess "Org cert enrolled"
} catch { Write-WtWarn "Enrolment may already exist" }

# --- Save state ---
$state = @{
    tenantId = $tenantId
    govOrgId = $govOrgId
    govOrgName = "Government Identity Authority"
    govWalletAddress = $govWallet.address
    citizenEmail = $citizenEmail
    citizenPassword = $citizenPassword
    persona = @{
        givenName = "Alice"
        familyName = "O'Brien"
        fullName = "Alice O'Brien"
        dateOfBirth = "1990-03-15"
        defaultEmail = $citizenEmail
        defaultAddress = @{
            street = "42 Grafton Street"
            locality = "Dublin"
            region = "Leinster"
            postcode = "D02 Y1K8"
            country = "Ireland"
        }
    }
    gatewayUrl = $baseUrl
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipIdentityAttestation — Setup Complete"
