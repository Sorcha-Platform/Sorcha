#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipDrivingLicence — Run
# Presents identity credential → receives driving licence credential.
# Exercises both HAIP verification (spec 098) and issuance (spec 097).

param(
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "HaipDrivingLicence — Run"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$walletDir = $state.walletDir
$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent"

# ============================================================================
# Step 1: Authenticate as Council admin
# ============================================================================
Write-WtStep "Step 1: Authenticate as Council Admin"

$councilSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.councilAdmin.email `
    -Password $state.roles.councilAdmin.password `
    -OrganizationId $state.roles.councilAdmin.organizationId

Write-WtSuccess "Authenticated"

# ============================================================================
# Step 2: Create presentation request for identity credential
# ============================================================================
Write-WtStep "Step 2: Create Presentation Request"

$presRequestBody = @{
    credentialType = "VerifiedIdentityCredential"
    requiredClaims = @("givenName", "familyName", "dateOfBirth")
}

try {
    $presRequest = Invoke-SorchaApi -Method POST `
        -Uri "$($state.gatewayUrl)/api/v1/verifier/requests" `
        -Headers $councilSession.Headers `
        -Body $presRequestBody

    Write-WtSuccess "Presentation request: $($presRequest.requestId)"
    if ($ShowJson) { $presRequest | ConvertTo-Json -Depth 5 | Write-Host }
} catch {
    Write-WtFail "Failed to create presentation request: $_"
    exit 1
}

# ============================================================================
# Step 3: Present identity credential via sorcha-agent
# ============================================================================
Write-WtStep "Step 3: sorcha-agent haip present"
Write-WtInfo "Disclosing: givenName, familyName, dateOfBirth"

& dotnet run --project $agentProject -- haip present `
    --request-uri $presRequest.requestUri `
    --credential "VerifiedIdentityCredential" `
    --disclose "givenName,familyName,dateOfBirth" `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Presentation failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
Write-WtSuccess "Identity credential presented and verified"

# ============================================================================
# Step 4: Create credential offer for driving licence
# ============================================================================
Write-WtStep "Step 4: Create Driving Licence Offer"

$today = (Get-Date).ToString("yyyy-MM-dd")
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-dd")

$licenceOfferBody = @{
    issuerWalletAddress = $state.councilWalletAddress
    tenantId = $state.tenantId
    credentialType = "DrivingLicenceCredential"
    claims = @{
        licenceNumber = "DL-$(Get-Random -Maximum 99999)"
        vehicleClass = "B"
        issuedDate = $today
        expiryDate = $expiry
        holderName = "Alice O'Brien"
    }
    disclosablePaths = @(
        "licenceNumber", "vehicleClass", "issuedDate", "expiryDate", "holderName"
    )
}

try {
    $licenceOffer = Invoke-SorchaApi -Method POST `
        -Uri "$($state.gatewayUrl)/api/v1/offers" `
        -Headers $councilSession.Headers `
        -Body $licenceOfferBody

    Write-WtSuccess "Licence offer: $($licenceOffer.offerId)"
    if ($ShowJson) { $licenceOffer | ConvertTo-Json -Depth 5 | Write-Host }
} catch {
    Write-WtFail "Failed to create licence offer: $_"
    exit 1
}

# ============================================================================
# Step 5: Receive driving licence credential
# ============================================================================
Write-WtStep "Step 5: sorcha-agent haip receive"

& dotnet run --project $agentProject -- haip receive `
    --offer-uri $licenceOffer.credentialOfferUri `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Credential receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 6: Verify both credentials in wallet
# ============================================================================
Write-WtStep "Step 6: Verify Wallet Contents"

$identityCred = Join-Path $walletDir "credentials/VerifiedIdentityCredential.sdjwt"
$licenceCred = Join-Path $walletDir "credentials/DrivingLicenceCredential.sdjwt"

$allPresent = (Test-Path $identityCred) -and (Test-Path $licenceCred)

if ($allPresent) {
    Write-WtSuccess "Both credentials in wallet:"
    Write-WtInfo "  VerifiedIdentityCredential: $((Get-Item $identityCred).Length) bytes"
    Write-WtInfo "  DrivingLicenceCredential:   $((Get-Item $licenceCred).Length) bytes"
} else {
    if (-not (Test-Path $identityCred)) { Write-WtFail "Missing: VerifiedIdentityCredential" }
    if (-not (Test-Path $licenceCred))  { Write-WtFail "Missing: DrivingLicenceCredential" }
    exit 1
}

Write-WtBanner "HaipDrivingLicence — Complete"
Write-WtSuccess "Full HAIP round-trip: present identity -> verify -> issue licence"
