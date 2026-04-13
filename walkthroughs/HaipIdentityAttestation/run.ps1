#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# HaipIdentityAttestation — Run
# Issues a VerifiedIdentityCredential to the citizen via the HAIP OID4VCI flow.
# Creates a Blueprint instance, executes the issuance action, and uses
# sorcha-agent haip receive to simulate an external HAIP wallet.

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

# ============================================================================
# Step 1: Authenticate as Citizen and Government Assessor
# ============================================================================
# Two distinct identities are needed:
#   - The citizen submits Action 1 (their own identity application) under
#     their own user token, in the public org.
#   - The government assessor submits Action 2 (review + issue VC) under the
#     gov org admin token. Action 2 carries the credentialIssuanceConfig.
Write-WtStep "Step 1: Authenticate as Citizen and Gov Assessor"

$citizenSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.citizen.email `
    -Password $state.roles.citizen.password `
    -OrganizationId $state.roles.citizen.organizationId
Write-WtSuccess "Authenticated as citizen ($($state.roles.citizen.email))"

$assessorSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.govAssessor.email `
    -Password $state.roles.govAssessor.password `
    -OrganizationId $state.roles.govAssessor.organizationId
Write-WtSuccess "Authenticated as government-assessor"

# ============================================================================
# Step 2: Citizen Creates the Blueprint Instance
# ============================================================================
# The instance is created under the citizen's token because they're the
# starting-action sender. tenantId reflects their org (public), so the
# audit trail correctly attributes the application to them.
Write-WtStep "Step 2: Citizen creates Blueprint Instance"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.publicOrgId
    metadata    = @{ source = "walkthrough"; walkthrough = "HaipIdentityAttestation" }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $citizenSession.Headers

$instanceId = $instance.id
Write-WtSuccess "Instance created: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Citizen Submits Identity Application (Action 1)
# ============================================================================
Write-WtStep "Step 3: Citizen submits Identity Application (Action 1)"

$persona = $state.persona
$payloadData = @{
    givenName   = $persona.givenName
    familyName  = $persona.familyName
    fullName    = $persona.fullName
    dateOfBirth = $persona.dateOfBirth
    email       = $persona.defaultEmail
    address     = @{
        street   = $persona.defaultAddress.street
        locality = $persona.defaultAddress.locality
        region   = $persona.defaultAddress.region
        postcode = $persona.defaultAddress.postcode
        country  = $persona.defaultAddress.country
    }
}

$null = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.citizenWalletAddress `
    -RegisterId $state.registerId `
    -Token $citizenSession.Token `
    -PayloadData $payloadData

# ============================================================================
# Step 4: Government Assessor Reviews and Issues Credential (Action 2)
# ============================================================================
# Action 2 carries the credentialIssuanceConfig — submitting it triggers the
# HAIP credential offer (OpenID4VCI pre-authorized code flow) and returns
# the offerUri the citizen will scan with their external HAIP wallet.
Write-WtStep "Step 4: Gov Assessor reviews and issues credential (Action 2)"

$actionResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.govWalletAddress `
    -RegisterId $state.registerId `
    -Token $assessorSession.Token `
    -PayloadData @{
        verificationDecision = "approved"
        reviewerNotes        = "Identity verified against persona of record."
    }

if ($ShowJson) { $actionResponse | ConvertTo-Json -Depth 5 | Write-Host }

# Extract credential offer URI from action response
$credentialOffer = $actionResponse.credentialOffer
if (-not $credentialOffer) {
    Write-WtFail "Action 2 response did not contain a credentialOffer. HAIP response pipeline may not be working."
    exit 1
}

$offerUri = $credentialOffer.credentialOfferUri
Write-WtSuccess "Credential offer created: $($credentialOffer.offerId)"
Write-WtInfo "Type: $($credentialOffer.credentialType)"
Write-WtInfo "Expires: $($credentialOffer.expiresAt)"

$truncatedUri = $offerUri.Substring(0, [Math]::Min(80, $offerUri.Length))
Write-WtInfo "Offer URI: $truncatedUri..."

# ============================================================================
# Step 5: sorcha-agent haip receive (simulates citizen's external wallet)
# ============================================================================
Write-WtStep "Step 5: sorcha-agent haip receive"

$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent"

& dotnet run --project $agentProject -- haip receive --offer-uri $offerUri --wallet-dir $walletDir
if ($LASTEXITCODE -ne 0) {
    Write-WtFail "sorcha-agent haip receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 6: Verify Credential
# ============================================================================
Write-WtStep "Step 6: Verify Credential"

$credFile = Join-Path $walletDir "credentials/VerifiedIdentityCredential.sdjwt"
if (Test-Path $credFile) {
    $credSize = (Get-Item $credFile).Length
    Write-WtSuccess "Credential stored: $credFile ($credSize bytes)"
} else {
    Write-WtFail "Credential file not found"
    exit 1
}

# Update state with instance and wallet info
$state | Add-Member -NotePropertyName "instanceId" -NotePropertyValue $instanceId -Force
$state | Add-Member -NotePropertyName "credentialPath" -NotePropertyValue $credFile -Force
$state | Add-Member -NotePropertyName "walletDir" -NotePropertyValue $walletDir -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "HaipIdentityAttestation — Complete"
Write-WtSuccess "VerifiedIdentityCredential issued to citizen wallet via Blueprint action"
