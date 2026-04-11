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
# Step 1: Authenticate as Government Admin
# ============================================================================
Write-WtStep "Step 1: Authenticate as Government Admin"

$govSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.govAdmin.email `
    -Password $state.roles.govAdmin.password `
    -OrganizationId $state.roles.govAdmin.organizationId

Write-WtSuccess "Authenticated as gov-admin"

# ============================================================================
# Step 2: Create Blueprint Instance
# ============================================================================
Write-WtStep "Step 2: Create Blueprint Instance"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.govOrgId
    metadata    = @{ source = "walkthrough"; walkthrough = "HaipIdentityAttestation" }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $govSession.Headers

$instanceId = $instance.id
Write-WtSuccess "Instance created: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Execute "Issue Identity Credential" Action
# ============================================================================
Write-WtStep "Step 3: Execute Issue Identity Credential Action"

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

$actionResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.govWalletAddress `
    -RegisterId $state.registerId `
    -Token $govSession.Token `
    -PayloadData $payloadData

if ($ShowJson) { $actionResponse | ConvertTo-Json -Depth 5 | Write-Host }

# Extract credential offer URI from action response
$credentialOffer = $actionResponse.credentialOffer
if (-not $credentialOffer) {
    Write-WtFail "Action response did not contain a credentialOffer. HAIP response pipeline may not be working."
    exit 1
}

$offerUri = $credentialOffer.credentialOfferUri
Write-WtSuccess "Credential offer created: $($credentialOffer.offerId)"
Write-WtInfo "Type: $($credentialOffer.credentialType)"
Write-WtInfo "Expires: $($credentialOffer.expiresAt)"

$truncatedUri = $offerUri.Substring(0, [Math]::Min(80, $offerUri.Length))
Write-WtInfo "Offer URI: $truncatedUri..."

# ============================================================================
# Step 4: sorcha-agent haip receive
# ============================================================================
Write-WtStep "Step 4: sorcha-agent haip receive"

$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent"

& dotnet run --project $agentProject -- haip receive --offer-uri $offerUri --wallet-dir $walletDir
if ($LASTEXITCODE -ne 0) {
    Write-WtFail "sorcha-agent haip receive failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# ============================================================================
# Step 5: Verify Credential
# ============================================================================
Write-WtStep "Step 5: Verify Credential"

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
