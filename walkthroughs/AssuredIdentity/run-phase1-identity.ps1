#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Phase 1 (Identity issuance)
# Feature 107. Creates the blueprint instance, submits the citizen's Assured
# Identity application (Action 1), the verification analyst approves (Action 2),
# and the citizen claims the AssuredIdentityCredential into their external
# HAIP wallet via sorcha-agent haip receive.

param(
    [switch]$ShowJson,
    [switch]$IncludePortrait
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Phase 1 (Identity)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$walletDir = Join-Path $scriptDir "wallet"

# ============================================================================
# Step 1: Authenticate both roles
# ============================================================================
Write-WtStep "Step 1: Authenticate Citizen and Verification Analyst"

$citizenSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.citizen.email `
    -Password $state.roles.citizen.password `
    -OrganizationId $state.roles.citizen.organizationId
Write-WtSuccess "Authenticated as citizen ($($state.roles.citizen.email))"

$analystSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.verificationAnalyst.email `
    -Password $state.roles.verificationAnalyst.password `
    -OrganizationId $state.roles.verificationAnalyst.organizationId
Write-WtSuccess "Authenticated as verification-analyst"

# ============================================================================
# Step 2: Create Blueprint Instance (citizen-owned)
# ============================================================================
Write-WtStep "Step 2: Citizen creates Blueprint Instance"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.publicOrgId
    metadata    = @{ source = "walkthrough"; walkthrough = "AssuredIdentity" }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $citizenSession.Headers
$instanceId = $instance.id
Write-WtSuccess "Instance created: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 3: Citizen submits Action 1
# ============================================================================
Write-WtStep "Step 3: Citizen submits Identity Application (Action 1)"

$persona = $state.persona
$payloadData = @{
    name = @{
        givenName  = $persona.givenName
        middleName = $persona.middleName
        familyName = $persona.familyName
        fullName   = $persona.fullName
    }
    dob = @{
        dateOfBirth = $persona.dateOfBirth
    }
    email = @{
        email = $persona.defaultEmail
    }
    address = @{
        line1    = $persona.defaultAddress.street
        town     = $persona.defaultAddress.locality
        region   = $persona.defaultAddress.region
        postcode = $persona.defaultAddress.postcode
        country  = $persona.defaultAddress.country
    }
}

# Optional portrait — the Feature 107 server-side gate accepts a base64 JPEG
# token up to ~27KB. The walkthrough's PowerShell path bypasses the browser
# canvas resize, so the caller supplies an already-sized sample via
# data/sample-portrait.jpg. When absent, the credential is issued without
# the portrait claim (the blueprint marks portrait optional).
if ($IncludePortrait) {
    $samplePath = Join-Path $scriptDir "data/sample-portrait.jpg"
    if (Test-Path $samplePath) {
        $bytes = [System.IO.File]::ReadAllBytes($samplePath)
        $base64 = [Convert]::ToBase64String($bytes)
        if ($base64.Length -gt 27000) {
            Write-WtWarn "sample-portrait.jpg is $($base64.Length) base64 chars — over the 27KB server gate. Skipping."
        } else {
            $payloadData.portrait = @{
                tokenImageBase64 = $base64
            }
            Write-WtInfo "Portrait attached ($($base64.Length) base64 chars)"
        }
    } else {
        Write-WtWarn "sample-portrait.jpg not found at $samplePath — submitting without portrait"
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
# Step 4: Verification Analyst approves Action 2
# ============================================================================
Write-WtStep "Step 4: Verification Analyst verifies application (Action 2)"

$actionResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.verificationWalletAddress `
    -RegisterId $state.registerId `
    -Token $analystSession.Token `
    -PayloadData @{
        decision          = "approved"
        verificationNotes = "Identity verified against submitted persona."
    }

if ($ShowJson) { $actionResponse | ConvertTo-Json -Depth 5 | Write-Host }

$credentialOffer = $actionResponse.credentialOffer
if (-not $credentialOffer) {
    Write-WtFail "Action 2 did not return a credentialOffer. HAIP response pipeline may be misconfigured."
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

$credFile = Join-Path $walletDir "credentials/AssuredIdentityCredential.sdjwt"
if (Test-Path $credFile) {
    $credSize = (Get-Item $credFile).Length
    Write-WtSuccess "Credential stored: $credFile ($credSize bytes)"
} else {
    Write-WtFail "Credential file not found at $credFile"
    exit 1
}

$state | Add-Member -NotePropertyName "instanceId" -NotePropertyValue $instanceId -Force
$state | Add-Member -NotePropertyName "credentialPath" -NotePropertyValue $credFile -Force
$state | Add-Member -NotePropertyName "walletDir" -NotePropertyValue $walletDir -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "AssuredIdentity — Phase 1 Complete"
Write-WtSuccess "AssuredIdentityCredential issued to citizen wallet via blueprint action"
