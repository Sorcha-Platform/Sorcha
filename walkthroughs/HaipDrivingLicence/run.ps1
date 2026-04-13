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
# Step 1: Authenticate as Applicant and Council
# ============================================================================
# Two distinct identities:
#   - The applicant submits Action 1 (their own licence application) under
#     their own user token, in the public org.
#   - The council submits Actions 2 and 3 (verify identity + issue licence)
#     under the council admin token.
Write-WtStep "Step 1: Authenticate as Applicant and Council"

$applicantSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.applicant.email `
    -Password $state.roles.applicant.password `
    -OrganizationId $state.roles.applicant.organizationId
Write-WtSuccess "Authenticated as applicant ($($state.roles.applicant.email))"

$councilSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.councilAdmin.email `
    -Password $state.roles.councilAdmin.password `
    -OrganizationId $state.roles.councilAdmin.organizationId
Write-WtSuccess "Authenticated as council"

# ============================================================================
# Step 2: Applicant Creates the Blueprint Instance
# ============================================================================
# Created under the applicant token because they're the starting-action
# sender. Instance metadata is correctly attributed to them.
Write-WtStep "Step 2: Applicant creates Blueprint Instance"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.roles.applicant.organizationId
    metadata    = @{ source = "walkthrough"; walkthrough = "HaipDrivingLicence" }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $applicantSession.Headers

$instanceId = $instance.id
Write-WtSuccess "Instance: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Applicant Submits Driving Licence Application (Action 1)
# ============================================================================
Write-WtStep "Step 3: Applicant submits Driving Licence Application (Action 1)"

$null = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.applicantWalletAddress `
    -RegisterId $state.registerId `
    -Token $applicantSession.Token `
    -PayloadData @{
        givenName    = "Alice"
        familyName   = "O'Brien"
        dateOfBirth  = "1990-03-15"
        email        = $state.roles.applicant.email
        vehicleClass = "Car (B)"
        applicantNotes = "Standard car licence application"
    }

# ============================================================================
# Step 4: Council Verifies Applicant Identity (Action 2 — HAIP presentation)
# ============================================================================
Write-WtStep "Step 4: Council verifies applicant identity (Action 2)"

$verifyResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
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
# Step 5: sorcha-agent haip present (citizen presents identity credential)
# ============================================================================
Write-WtStep "Step 5: sorcha-agent haip present"
Write-WtInfo "Disclosing: givenName, familyName, dateOfBirth"

# The presentation request URI contains the request object URL
# sorcha-agent uses the request URI from the HAIP service
$requestUri = "$($state.gatewayUrl)/api/v1/verifier/requests/$($presentationRequest.requestId)/request-object"

& dotnet run --project $agentProject -- haip present `
    --request-uri $requestUri `
    --credential "VerifiedCitizenCredential" `
    --disclose "givenName,familyName,dateOfBirth" `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Presentation failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
Write-WtSuccess "Identity credential presented and verified"

# ============================================================================
# Step 6: Wait for Action 3 (Issue Licence) to become current
# ============================================================================
Write-WtStep "Step 6: Wait for Action 3"

$maxWait = 60
$waited = 0
$ready = $false
while ($waited -lt $maxWait) {
    $inst = Invoke-SorchaApi -Method GET `
        -Uri "$($state.blueprintUrl)/instances/$instanceId" `
        -Headers $councilSession.Headers
    if ($inst.currentActionIds -contains 3) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 1
    $waited++
}
if (-not $ready) {
    Write-WtWarn "Action 3 not yet current after ${waited}s — proceeding anyway"
} else {
    Write-WtInfo "Action 3 ready (waited ${waited}s)"
}

# ============================================================================
# Step 7: Council Issues Driving Licence (Action 3 — HAIP credential offer)
# ============================================================================
Write-WtStep "Step 7: Council issues Driving Licence (Action 3)"

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
    -ActionId "3" `
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
# Step 8: sorcha-agent haip receive (citizen collects driving licence)
# ============================================================================
Write-WtStep "Step 8: sorcha-agent haip receive"

& dotnet run --project $agentProject -- haip receive `
    --offer-uri $credentialOffer.credentialOfferUri `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Credential receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 9: Verify both credentials in wallet
# ============================================================================
Write-WtStep "Step 9: Verify Wallet Contents"

$identityCred = Join-Path $walletDir "credentials/VerifiedCitizenCredential.sdjwt"
$licenceCred = Join-Path $walletDir "credentials/DrivingLicenceCredential.sdjwt"

$allPresent = (Test-Path $identityCred) -and (Test-Path $licenceCred)

if ($allPresent) {
    Write-WtSuccess "Both credentials in wallet:"
    Write-WtInfo "  VerifiedCitizenCredential: $((Get-Item $identityCred).Length) bytes"
    Write-WtInfo "  DrivingLicenceCredential:   $((Get-Item $licenceCred).Length) bytes"
} else {
    if (-not (Test-Path $identityCred)) { Write-WtFail "Missing: VerifiedCitizenCredential" }
    if (-not (Test-Path $licenceCred))  { Write-WtFail "Missing: DrivingLicenceCredential" }
    exit 1
}

# Update state with instance ID
$state | Add-Member -NotePropertyName "instanceId" -NotePropertyValue $instanceId -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "HaipDrivingLicence — Complete"
Write-WtSuccess "Full HAIP round-trip via Blueprint: present identity -> verify -> issue licence"
