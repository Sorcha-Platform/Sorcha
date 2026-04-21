#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Phase 2 (Driving Licence chain)
# Feature 107 PR 2 (US2). Citizen submits a driving-licence application,
# Acme Licensing Co. verifies the citizen's presented AssuredIdentityCredential via
# HAIP OpenID4VP, then issues a DrivingLicenceCredential carrying the
# holder's identity forward from the presentation.
#
# Prerequisites: run-phase1-identity.ps1 must have run first so the
# citizen's HAIP wallet-dir holds an AssuredIdentityCredential.

param(
    [switch]$ShowJson,
    [switch]$IncludePortrait
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Phase 2 (Driving Licence)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$walletDir = Join-Path $scriptDir "wallet"
$identityCredPath = Join-Path $walletDir "credentials/AssuredIdentityCredential.sdjwt"
if (-not (Test-Path $identityCredPath)) {
    Write-WtFail "No AssuredIdentityCredential in the wallet. Run run-phase1-identity.ps1 first."
    exit 1
}

$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent"

# ============================================================================
# Step 1: Authenticate Citizen and Licensing Officer
# ============================================================================
Write-WtStep "Step 1: Authenticate Citizen and Licensing Officer"

$citizenSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.citizen.email `
    -Password $state.roles.citizen.password `
    -OrganizationId $state.roles.citizen.organizationId
Write-WtSuccess "Authenticated as citizen"

$licensingSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.licensingOfficer.email `
    -Password $state.roles.licensingOfficer.password `
    -OrganizationId $state.roles.licensingOfficer.organizationId
Write-WtSuccess "Authenticated as licensing officer"

# ============================================================================
# Step 2: Citizen creates the Driving Licence Blueprint instance
# ============================================================================
Write-WtStep "Step 2: Citizen creates Driving Licence Instance"

$instanceBody = @{
    blueprintId = $state.licenceBlueprintId
    registerId  = $state.registerId
    tenantId    = $state.publicOrgId
    metadata    = @{ source = "walkthrough"; walkthrough = "AssuredIdentity"; phase = 2 }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $citizenSession.Headers
$instanceId = $instance.id
Write-WtSuccess "Instance: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Citizen submits vehicle class (Action 1)
# ============================================================================
Write-WtStep "Step 3: Citizen submits Driving Licence Application (Action 1)"

$null = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.licenceBlueprintId `
    -SenderWallet $state.citizenWalletAddress `
    -RegisterId $state.registerId `
    -Token $citizenSession.Token `
    -PayloadData @{
        vehicleClass   = "Car (B)"
        applicantNotes = "Standard car licence application"
    }

# ============================================================================
# Step 4: Licensing officer verifies identity (Action 2 — HAIP presentation request)
# ============================================================================
Write-WtStep "Step 4: Licensing officer verifies applicant identity (Action 2)"

$verifyResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.licenceBlueprintId `
    -SenderWallet $state.licensingWalletAddress `
    -RegisterId $state.registerId `
    -Token $licensingSession.Token `
    -PayloadData @{ verificationNotes = "Assured Identity presentation requested via HAIP" }

if ($ShowJson) { $verifyResponse | ConvertTo-Json -Depth 5 | Write-Host }

$presentationRequest = $verifyResponse.presentationRequest
if (-not $presentationRequest) {
    Write-WtFail "Action 2 did not return a presentationRequest. HAIP verifier pipeline may be misconfigured."
    exit 1
}
Write-WtSuccess "Presentation request: $($presentationRequest.requestId)"
Write-WtInfo "Credential type: $($presentationRequest.credentialType)"
Write-WtInfo "Requested claims: $($presentationRequest.requestedClaims -join ', ')"

# ============================================================================
# Step 5: Citizen presents identity via sorcha-agent haip present
# ============================================================================
Write-WtStep "Step 5: sorcha-agent haip present"

$discloseClaims = "givenName,familyName,dateOfBirth"
if ($IncludePortrait) {
    $discloseClaims = "givenName,familyName,dateOfBirth,portrait"
}
Write-WtInfo "Disclosing: $discloseClaims"

$requestUri = "$($state.gatewayUrl)/api/v1/verifier/requests/$($presentationRequest.requestId)/request-object"

& dotnet run --project $agentProject -- haip present `
    --request-uri $requestUri `
    --credential "AssuredIdentityCredential" `
    --disclose $discloseClaims `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Presentation failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
Write-WtSuccess "Assured Identity presented and verified"

# ============================================================================
# Step 6: Wait for Action 3 (Issue Licence) to become current
# ============================================================================
Write-WtStep "Step 6: Wait for Action 3 to become current"

$maxWait = 60
$waited = 0
$ready = $false
while ($waited -lt $maxWait) {
    $inst = Invoke-SorchaApi -Method GET `
        -Uri "$($state.blueprintUrl)/instances/$instanceId" `
        -Headers $licensingSession.Headers
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
# Step 7: Licensing officer issues Driving Licence (Action 3 — credential mint)
# ============================================================================
# Walkthrough-level carry-forward: the licensing officer's submission payload
# includes holderName / holderDateOfBirth / holderPortrait drawn from the
# citizen's persona state (which is the source of truth the citizen just
# presented from their AssuredIdentityCredential). The claim mappings on
# the blueprint reference these payload fields directly — see follow-up
# issue #338 for proper server-side /presentedClaims/ plumbing.
Write-WtStep "Step 7: Licensing officer issues Driving Licence (Action 3)"

$today = (Get-Date).ToString("yyyy-MM-dd")
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-dd")

$persona = $state.persona
$licenceData = @{
    licenceNumber     = "DL-ACME-$(Get-Random -Maximum 99999)"
    vehicleClass      = "Car (B)"
    issuedDate        = $today
    expiryDate        = $expiry
    holderName        = $persona.fullName
    holderDateOfBirth = $persona.dateOfBirth
}

# Portrait carry-forward — optional. When -IncludePortrait was passed on
# Phase 1, the Assured Identity token is at $persona.portraitTokenBase64.
# The licensing officer reads the citizen's wallet-dir directly in a real browser
# integration; here the walkthrough stashed it into persona state for
# script-level simplicity.
if ($IncludePortrait -and $state.persona.portraitTokenBase64) {
    $licenceData.holderPortrait = $state.persona.portraitTokenBase64
    Write-WtInfo "Portrait carried forward ($($state.persona.portraitTokenBase64.Length) base64 chars)"
}

$licenceResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "3" `
    -BlueprintId $state.licenceBlueprintId `
    -SenderWallet $state.licensingWalletAddress `
    -RegisterId $state.registerId `
    -Token $licensingSession.Token `
    -PayloadData $licenceData

if ($ShowJson) { $licenceResponse | ConvertTo-Json -Depth 5 | Write-Host }

$credentialOffer = $licenceResponse.credentialOffer
if (-not $credentialOffer) {
    Write-WtFail "Action 3 did not return a credentialOffer. HAIP issuance pipeline may be misconfigured."
    exit 1
}
Write-WtSuccess "Licence offer: $($credentialOffer.offerId)"

# ============================================================================
# Step 8: Citizen claims Driving Licence via sorcha-agent haip receive
# ============================================================================
Write-WtStep "Step 8: sorcha-agent haip receive (Driving Licence)"

& dotnet run --project $agentProject -- haip receive `
    --offer-uri $credentialOffer.credentialOfferUri `
    --wallet-dir $walletDir

if ($LASTEXITCODE -ne 0) {
    Write-WtFail "Licence receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 9: Verify both credentials in wallet + carry-forward correctness
# ============================================================================
Write-WtStep "Step 9: Verify wallet contents"

$identityCred = Join-Path $walletDir "credentials/AssuredIdentityCredential.sdjwt"
$licenceCred  = Join-Path $walletDir "credentials/DrivingLicenceCredential.sdjwt"

if (-not (Test-Path $identityCred)) { Write-WtFail "Missing AssuredIdentityCredential"; exit 1 }
if (-not (Test-Path $licenceCred))  { Write-WtFail "Missing DrivingLicenceCredential";  exit 1 }

$identitySize = (Get-Item $identityCred).Length
$licenceSize  = (Get-Item $licenceCred).Length
Write-WtSuccess "Both credentials in wallet:"
Write-WtInfo "  AssuredIdentityCredential: $identitySize bytes"
Write-WtInfo "  DrivingLicenceCredential:  $licenceSize bytes"

# Spot-check: licence SD-JWT carries the holderName disclosure. SD-JWT
# disclosures are `~base64url(JSON-array)~` after the main JWT, so the
# literal string "holderName" is only present after base64-decoding the
# disclosure segments. Decode + scan for the expected claim names.
$licenceBody = Get-Content $licenceCred -Raw
$segments = $licenceBody.TrimEnd('~').Split('~')
$decodedDisclosures = @()
for ($i = 1; $i -lt $segments.Length; $i++) {
    $seg = $segments[$i]
    if ([string]::IsNullOrWhiteSpace($seg)) { continue }
    $padded = $seg.Replace('-', '+').Replace('_', '/')
    switch ($padded.Length % 4) {
        2 { $padded += '==' }
        3 { $padded += '=' }
    }
    try {
        $json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($padded))
        $decodedDisclosures += $json
    } catch { }
}

$decodedText = ($decodedDisclosures -join "`n")
$expectedClaims = @("holderName", "holderDateOfBirth", "licenceNumber", "vehicleClass")
$missing = $expectedClaims | Where-Object { $decodedText -notmatch $_ }
if ($missing.Count -gt 0) {
    Write-WtWarn "Licence missing expected claim disclosures: $($missing -join ', ')"
} else {
    Write-WtSuccess "Licence credential carries all expected claim disclosures (including holderName + holderDateOfBirth carry-forward)"
}

$state | Add-Member -NotePropertyName "licenceInstanceId" -NotePropertyValue $instanceId -Force
$state | Add-Member -NotePropertyName "licenceCredentialPath" -NotePropertyValue $licenceCred -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "AssuredIdentity — Phase 2 Complete"
Write-WtSuccess "DrivingLicenceCredential issued via credential-chain (AssuredIdentity -> Driving Licence)"
