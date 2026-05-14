#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Phase 1 (Identity issuance to the Citizen Wallet PWA)
# Feature 124 swapped this from the legacy HAIP filesystem-wallet path to the
# Citizen Wallet PWA (SorchaLocalWallet target audience). The credential now
# lands directly in the citizen's PWA via register-native delivery, so the
# script no longer drives sorcha-agent haip receive.
#
# Script-driven beats (the operator runs this from a terminal while the
# citizen's wallet is open on a phone/second browser):
#   1. Citizen creates a blueprint instance
#   2. Citizen submits Action 1 — application details
#   3. Set the wallet's pending-application notice ("Assured Identity")
#      — the PWA now shows the waiting-card on Home
#   4. Verification analyst approves Action 2 → credential mints into the
#      citizen's PWA register-natively
#   5. Poll the citizen's wallet /credentials snapshot until the credential
#      appears, then Clear the pending-application notice
#
# When the credential lands, the PWA's CredentialAvailable hub push (or its
# poll-on-open path) fires the first-credential takeover (Feature 124 US3/US4).

param(
    [switch]$ShowJson,
    [switch]$IncludePortrait,
    [int]$DeliveryTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Phase 1 (Identity, PWA delivery)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

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
# Step 4: Set the pending-application notice (Feature 124 US2 / FR-009)
# ============================================================================
Write-WtStep "Step 4: Set pending-application notice on Citizen Wallet"

Set-SorchaCitizenPendingApplication `
    -WalletUrl $state.walletUrl `
    -Label "Assured Identity" `
    -Headers $citizenSession.Headers | Out-Null

Write-WtInfo "PWA will now render the waiting-card on Home"

# ============================================================================
# Step 5: Verification Analyst approves Action 2
# ============================================================================
Write-WtStep "Step 5: Verification Analyst verifies application (Action 2)"

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
Write-WtSuccess "Action 2 approved — credential issuance triggered (target: SorchaLocalWallet)"

# ============================================================================
# Step 6: Poll the citizen's wallet for the credential, then clear the notice
# ============================================================================
Write-WtStep "Step 6: Poll Citizen Wallet /credentials for AssuredIdentityCredential"

$deadline = (Get-Date).AddSeconds($DeliveryTimeoutSeconds)
$delivered = $false
$credentialId = $null

while ((Get-Date) -lt $deadline) {
    try {
        $snapshot = Invoke-SorchaApi -Method GET `
            -Uri "$($state.walletUrl)/v1/wallet/credentials" `
            -Headers $citizenSession.Headers
        if ($snapshot.credentials -and $snapshot.credentials.Count -gt 0) {
            $hit = $snapshot.credentials | Where-Object { $_.vct -match "AssuredIdentity" -or $_.displayLabel -match "Assured" } | Select-Object -First 1
            if ($hit) {
                $delivered = $true
                $credentialId = $hit.id
                break
            }
        }
    } catch {
        Write-WtWarn "  /credentials poll transient error: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 2
}

if (-not $delivered) {
    # Best-effort clear so the wallet doesn't sit in a stale waiting-state on failure.
    try { Clear-SorchaCitizenPendingApplication -WalletUrl $state.walletUrl -Headers $citizenSession.Headers } catch { }
    Write-WtFail "AssuredIdentityCredential did not land in the citizen's wallet within $DeliveryTimeoutSeconds s."
    exit 1
}

Write-WtSuccess "AssuredIdentityCredential delivered to PWA (credentialId=$credentialId)"

Clear-SorchaCitizenPendingApplication `
    -WalletUrl $state.walletUrl `
    -Headers $citizenSession.Headers

# Persist phase outcomes — no walletDir, no credentialPath; the credential
# lives in the PWA's IndexedDB on the citizen's device, not on the operator
# host. We only record the instance + credential id for cross-script use.
$state | Add-Member -NotePropertyName "instanceId" -NotePropertyValue $instanceId -Force
$state | Add-Member -NotePropertyName "credentialId" -NotePropertyValue $credentialId -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "AssuredIdentity — Phase 1 Complete"
Write-WtSuccess "Open the citizen's PWA — the first-credential welcome takeover should now fire."
