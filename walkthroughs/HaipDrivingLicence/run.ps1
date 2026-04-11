#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipDrivingLicence — Run
# Presents identity credential (OID4VP) → receives driving licence (OID4VCI).
# Creates a Blueprint instance, executes both actions through the Blueprint
# Service, and uses sorcha-agent to simulate the external HAIP wallet.

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
# Step 1: Authenticate as Council Admin
# ============================================================================
Write-WtStep "Step 1: Authenticate as Council Admin"

$councilSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.councilAdmin.email `
    -Password $state.roles.councilAdmin.password `
    -OrganizationId $state.roles.councilAdmin.organizationId

Write-WtSuccess "Authenticated"

# ============================================================================
# Step 2: Create Blueprint Instance
# ============================================================================
Write-WtStep "Step 2: Create Blueprint Instance"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.councilOrgId
    metadata    = @{ source = "walkthrough"; walkthrough = "HaipDrivingLicence" }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $councilSession.Headers

$instanceId = $instance.id
Write-WtSuccess "Instance: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Execute "Verify Applicant Identity" (creates presentation request QR)
# ============================================================================
Write-WtStep "Step 3: Verify Applicant Identity"

$verifyResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.councilWalletAddress `
    -RegisterId $state.registerId `
    -Token $councilSession.Token `
    -PayloadData @{ verificationNotes = "HAIP walkthrough identity verification" }

if ($ShowJson) { $verifyResponse | ConvertTo-Json -Depth 5 | Write-Host }

# Extract presentation request URI
$presentationRequest = $verifyResponse.presentationRequest
if (-not $presentationRequest) {
    Write-WtFail "Action response did not contain a presentationRequest. HAIP response pipeline may not be working."
    exit 1
}

Write-WtSuccess "Presentation request: $($presentationRequest.requestId)"
Write-WtInfo "Credential type: $($presentationRequest.credentialType)"
Write-WtInfo "Requested claims: $($presentationRequest.requestedClaims -join ', ')"

# ============================================================================
# Step 4: sorcha-agent haip present (citizen presents identity credential)
# ============================================================================
Write-WtStep "Step 4: sorcha-agent haip present"
Write-WtInfo "Disclosing: givenName, familyName, dateOfBirth"

# The presentation request URI contains the request object URL
# sorcha-agent uses the request URI from the HAIP service
$requestUri = "$($state.gatewayUrl)/api/v1/verifier/requests/$($presentationRequest.requestId)/request-object"

& dotnet run --project $agentProject -- haip present `
    --request-uri $requestUri `
    --credential "VerifiedIdentityCredential" `
    --disclose "givenName,familyName,dateOfBirth" `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Presentation failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
Write-WtSuccess "Identity credential presented and verified"

# ============================================================================
# Step 5: Wait for Action 2 to become current
# ============================================================================
Write-WtStep "Step 5: Wait for Action 2"

$maxWait = 60
$waited = 0
$ready = $false
while ($waited -lt $maxWait) {
    $inst = Invoke-SorchaApi -Method GET `
        -Uri "$($state.blueprintUrl)/instances/$instanceId" `
        -Headers $councilSession.Headers
    if ($inst.currentActionIds -contains 2) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 1
    $waited++
}
if (-not $ready) {
    Write-WtWarn "Action 2 not yet current after ${waited}s — proceeding anyway"
} else {
    Write-WtInfo "Action 2 ready (waited ${waited}s)"
}

# ============================================================================
# Step 6: Execute "Issue Driving Licence" (creates credential offer QR)
# ============================================================================
Write-WtStep "Step 6: Issue Driving Licence"

$today = (Get-Date).ToString("yyyy-MM-dd")
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-dd")

$licenceData = @{
    licenceNumber = "DL-$(Get-Random -Maximum 99999)"
    vehicleClass  = "B"
    issuedDate    = $today
    expiryDate    = $expiry
    holderName    = "Alice O'Brien"
}

$licenceResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.councilWalletAddress `
    -RegisterId $state.registerId `
    -Token $councilSession.Token `
    -PayloadData $licenceData

if ($ShowJson) { $licenceResponse | ConvertTo-Json -Depth 5 | Write-Host }

# Extract credential offer URI
$credentialOffer = $licenceResponse.credentialOffer
if (-not $credentialOffer) {
    Write-WtFail "Action response did not contain a credentialOffer. HAIP response pipeline may not be working."
    exit 1
}

Write-WtSuccess "Licence offer: $($credentialOffer.offerId)"

# ============================================================================
# Step 7: sorcha-agent haip receive (citizen collects driving licence)
# ============================================================================
Write-WtStep "Step 7: sorcha-agent haip receive"

& dotnet run --project $agentProject -- haip receive `
    --offer-uri $credentialOffer.credentialOfferUri `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Credential receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 8: Verify both credentials in wallet
# ============================================================================
Write-WtStep "Step 8: Verify Wallet Contents"

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

# Update state with instance ID
$state | Add-Member -NotePropertyName "instanceId" -NotePropertyValue $instanceId -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "HaipDrivingLicence — Complete"
Write-WtSuccess "Full HAIP round-trip via Blueprint: present identity -> verify -> issue licence"
