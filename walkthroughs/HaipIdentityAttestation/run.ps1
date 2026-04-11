#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipIdentityAttestation — Run
# Issues a VerifiedIdentityCredential to the citizen via the HAIP OID4VCI flow.
# Uses sorcha-agent haip receive to simulate a HAIP wallet.

param(
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HaipIdentityAttestation — Run"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$walletDir = Join-Path $scriptDir "wallet"
$secrets = Get-SorchaSecrets -WalkthroughName "haip-identity"

# ============================================================================
# Step 1: Authenticate as system admin (offer creation needs service-level access)
# ============================================================================
Write-WtStep "Step 1: Authenticate"

$sorchaEnv = Initialize-SorchaEnvironment -Profile gateway -SkipHealthCheck
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

Write-WtSuccess "Authenticated as admin"

# ============================================================================
# Step 2: Create credential offer for VerifiedIdentityCredential
# ============================================================================
Write-WtStep "Step 2: Create Credential Offer"

$persona = $state.persona
$offerBody = @{
    issuerWalletAddress = $state.govWalletAddress
    tenantId = $state.tenantId
    credentialType = "VerifiedIdentityCredential"
    claims = @{
        givenName = $persona.givenName
        familyName = $persona.familyName
        fullName = $persona.fullName
        dateOfBirth = $persona.dateOfBirth
        email = $persona.defaultEmail
        address = @{
            street = $persona.defaultAddress.street
            locality = $persona.defaultAddress.locality
            region = $persona.defaultAddress.region
            postcode = $persona.defaultAddress.postcode
            country = $persona.defaultAddress.country
        }
    }
    disclosablePaths = @(
        "givenName", "familyName", "fullName", "dateOfBirth", "email",
        "/address/street", "/address/locality", "/address/region",
        "/address/postcode", "/address/country"
    )
}

try {
    $offerResult = Invoke-SorchaApi -Method POST `
        -Uri "$($state.gatewayUrl)/api/v1/offers" `
        -Headers $sysAdmin.Headers `
        -Body $offerBody

    $offerUri = $offerResult.credentialOfferUri
    Write-WtSuccess "Credential offer created: $($offerResult.offerId)"
    if ($ShowJson) { $offerResult | ConvertTo-Json -Depth 5 | Write-Host }
} catch {
    Write-WtFail "Failed to create credential offer: $_"
    exit 1
}

# ============================================================================
# Step 3: Invoke sorcha-agent haip receive
# ============================================================================
Write-WtStep "Step 3: sorcha-agent haip receive"

$truncatedUri = $offerUri.Substring(0, [Math]::Min(80, $offerUri.Length))
Write-WtInfo "Offer URI: $truncatedUri..."

$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent"

& dotnet run --project $agentProject -- haip receive --offer-uri $offerUri --wallet-dir $walletDir
if ($LASTEXITCODE -ne 0) {
    Write-WtFail "sorcha-agent haip receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 4: Verify credential stored
# ============================================================================
Write-WtStep "Step 4: Verify Credential"

$credFile = Join-Path $walletDir "credentials/VerifiedIdentityCredential.sdjwt"
if (Test-Path $credFile) {
    $credSize = (Get-Item $credFile).Length
    Write-WtSuccess "Credential stored: $credFile ($credSize bytes)"
} else {
    Write-WtFail "Credential file not found"
    exit 1
}

# Update state with wallet path
$state | Add-Member -NotePropertyName "credentialPath" -NotePropertyValue $credFile -Force
$state | Add-Member -NotePropertyName "walletDir" -NotePropertyValue $walletDir -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "HaipIdentityAttestation — Complete"
Write-WtSuccess "VerifiedIdentityCredential issued to citizen wallet"
