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

# --- Step 1: Authenticate ---
Write-WtStep "Step 1: Authenticating as Council admin"
$secrets = Get-SorchaSecrets -WalkthroughName "haip-licence"
$councilToken = Connect-SorchaUser -BaseUrl $state.baseUrl `
    -Email "council-admin@haip-walkthrough.local" -Password $secrets.DefaultPassword `
    -OrganizationId $state.councilOrgId

# --- Step 2: Create presentation request for identity credential ---
Write-WtStep "Step 2: Creating presentation request for VerifiedIdentityCredential"

$presRequestBody = @{
    credentialType = "VerifiedIdentityCredential"
    requiredClaims = @("givenName", "familyName", "dateOfBirth")
    acceptedIssuers = $null
}

try {
    $presRequest = Invoke-SorchaApi -BaseUrl $state.baseUrl -Token $councilToken `
        -Method POST -Path "/api/v1/verifier/requests" -Body $presRequestBody

    $requestUri = $presRequest.requestUri
    Write-WtSuccess "Presentation request created: $($presRequest.requestId)"
    if ($ShowJson) { $presRequest | ConvertTo-Json -Depth 5 | Write-Host }
} catch {
    Write-WtFail "Failed to create presentation request: $_"
    exit 1
}

# --- Step 3: Present identity credential via sorcha-agent ---
Write-WtStep "Step 3: Presenting VerifiedIdentityCredential"
Write-WtInfo "Disclosing: givenName, familyName, dateOfBirth"

try {
    & dotnet run --project $agentProject -- haip present `
        --request-uri $requestUri `
        --credential "VerifiedIdentityCredential" `
        --disclose "givenName,familyName,dateOfBirth" `
        --wallet-dir $walletDir

    if ($LASTEXITCODE -ne 0) {
        Write-WtFail "Presentation failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-WtSuccess "Identity credential presented and verified"
} catch {
    Write-WtFail "Presentation threw an exception: $_"
    exit 1
}

# --- Step 4: Create credential offer for driving licence ---
Write-WtStep "Step 4: Creating credential offer for DrivingLicenceCredential"

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
    $licenceOffer = Invoke-SorchaApi -BaseUrl $state.baseUrl -Token $councilToken `
        -Method POST -Path "/api/v1/offers" -Body $licenceOfferBody

    Write-WtSuccess "Licence offer created: $($licenceOffer.offerId)"
    if ($ShowJson) { $licenceOffer | ConvertTo-Json -Depth 5 | Write-Host }
} catch {
    Write-WtFail "Failed to create licence offer: $_"
    exit 1
}

# --- Step 5: Receive driving licence credential ---
Write-WtStep "Step 5: Receiving DrivingLicenceCredential"

try {
    & dotnet run --project $agentProject -- haip receive `
        --offer-uri $licenceOffer.credentialOfferUri `
        --wallet-dir $walletDir

    if ($LASTEXITCODE -ne 0) {
        Write-WtFail "Credential receive failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-WtSuccess "Driving licence credential received"
} catch {
    Write-WtFail "Credential receive threw an exception: $_"
    exit 1
}

# --- Step 6: Verify both credentials in wallet ---
Write-WtStep "Step 6: Verifying wallet contents"

$identityCred = Join-Path $walletDir "credentials/VerifiedIdentityCredential.sdjwt"
$licenceCred = Join-Path $walletDir "credentials/DrivingLicenceCredential.sdjwt"

$identityOk = Test-Path $identityCred
$licenceOk = Test-Path $licenceCred

if ($identityOk -and $licenceOk) {
    Write-WtSuccess "Both credentials present in wallet:"
    Write-WtInfo "  VerifiedIdentityCredential: $((Get-Item $identityCred).Length) bytes"
    Write-WtInfo "  DrivingLicenceCredential:   $((Get-Item $licenceCred).Length) bytes"
} else {
    if (-not $identityOk) { Write-WtFail "Missing: VerifiedIdentityCredential" }
    if (-not $licenceOk)  { Write-WtFail "Missing: DrivingLicenceCredential" }
    exit 1
}

Write-WtBanner "HaipDrivingLicence — Complete"
Write-WtSuccess "Full HAIP round-trip: present identity → verify → issue licence"
