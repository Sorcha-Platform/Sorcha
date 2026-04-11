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
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Check if already set up (idempotent)
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json already exists. Use -Force to re-run setup."
    $state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
    Write-WtSuccess "Setup already complete. Org: $($state.govOrgName)"
    return
}

# --- Step 1: Connect as system admin ---
Write-WtStep 1 "Connecting as system admin"
$adminToken = Connect-SorchaAdmin -BaseUrl $env.BaseUrl -Secrets $secrets

# --- Step 2: Create Government Identity Authority org ---
Write-WtStep 2 "Creating Government Identity Authority"
$govOrg = Get-OrCreateOrganization -BaseUrl $env.BaseUrl -Token $adminToken `
    -Name "Government Identity Authority" `
    -AdminEmail "gov-admin@haip-walkthrough.local" `
    -AdminPassword $secrets.DefaultPassword

$govOrgId = $govOrg.id
Write-WtSuccess "Government org: $govOrgId"

# --- Step 3: Create citizen user on public org ---
Write-WtStep 3 "Creating citizen user"
$citizenEmail = "alice.obrien@haip-walkthrough.local"
$citizenPassword = $secrets.DefaultPassword

$citizen = Register-SorchaPublicUser -BaseUrl $env.BaseUrl `
    -Email $citizenEmail -Password $citizenPassword -DisplayName "Alice O'Brien"
Confirm-SorchaUserEmail -BaseUrl $env.BaseUrl -Token $adminToken -Email $citizenEmail

Write-WtSuccess "Citizen user: $citizenEmail"

# --- Step 4: Create wallet for Government org ---
Write-WtStep 4 "Creating Government wallet"
$govAdminToken = Connect-SorchaUser -BaseUrl $env.BaseUrl `
    -Email "gov-admin@haip-walkthrough.local" -Password $secrets.DefaultPassword `
    -OrganizationId $govOrgId

$govWallet = New-SorchaWallet -BaseUrl $env.BaseUrl -Token $govAdminToken `
    -Name "Government Identity Issuer" -Algorithm "ES256"

Write-WtSuccess "Government wallet: $($govWallet.address)"

# --- Step 5: Provision trust anchor ---
Write-WtStep 5 "Provisioning trust anchor for tenant"
try {
    $trustResult = Invoke-SorchaApi -BaseUrl $env.BaseUrl -Token $adminToken `
        -Method POST -Path "/api/v1/trust/tenants/$($env.TenantId)/provision" `
        -Body @{}
    Write-WtSuccess "Trust anchor provisioned: $($trustResult.serialNumber)"
} catch {
    Write-WtWarn "Trust anchor may already exist: $_"
}

# --- Step 6: Enrol Government org as HAIP issuer ---
Write-WtStep 6 "Enrolling Government org as HAIP issuer"
try {
    $enrolResult = Invoke-SorchaApi -BaseUrl $env.BaseUrl -Token $adminToken `
        -Method POST `
        -Path "/api/v1/trust/tenants/$($env.TenantId)/orgs/$($govWallet.address)/enrol" `
        -Body @{
            orgPublicKeyBase64 = $govWallet.publicKey
            orgDisplayName = "Government Identity Authority"
        }
    Write-WtSuccess "Org cert enrolled: $($enrolResult.serialNumber)"
} catch {
    Write-WtWarn "Enrolment may already exist: $_"
}

# --- Save state ---
$state = @{
    tenantId = $env.TenantId
    govOrgId = $govOrgId
    govOrgName = "Government Identity Authority"
    govWalletAddress = $govWallet.address
    citizenEmail = $citizenEmail
    citizenPassword = $citizenPassword
    citizenName = @{
        givenName = "Alice"
        familyName = "O'Brien"
    }
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
    baseUrl = $env.BaseUrl
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"
Write-WtBanner "HaipIdentityAttestation — Setup Complete"
