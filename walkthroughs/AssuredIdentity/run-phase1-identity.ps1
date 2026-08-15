#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Phase 1 (Identity issuance to the Sorcha Wallet (PWA))
# Feature 124 swapped this from the legacy HAIP filesystem-wallet path to the
# Sorcha Wallet (PWA) (SorchaLocalWallet target audience). The credential now
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

# ----------------------------------------------------------------------------
# Step 1b: Ensure the issuing org (Acme Verification Co.) has a master key
# ----------------------------------------------------------------------------
# Feature 155 (R-003 / T010): the AssuredIdentityCredential MUST sign with the
# org's Feature 120 VC-issuance key (iss = resolvable did:sorcha:org:{C} with a
# #vc-issuance-n kid) so the Open Verifier PWA can resolve the issuer signature.
# Without a master key the credential signs with the bare wallet key and the
# verifier's DID-backed resolver can't verify it (the documented split-brain).
# Set-SorchaOrgMasterKey needs an Administrator + platform-audience token, so we
# log in as the Tier-2 org admin (verification-admin) rather than the Tier-3
# analyst. Idempotent — returns silently if already provisioned.
Write-WtStep "Step 1b: Ensure issuing org has a master key (Feature 120 VC-issuance)"

$verificationAdminSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.verificationAdmin.email `
    -Password $state.roles.verificationAdmin.password `
    -OrganizationId $state.roles.verificationAdmin.organizationId

Set-SorchaOrgMasterKey `
    -WalletUrl $state.walletUrl `
    -OrganizationId $state.roles.verificationAdmin.organizationId `
    -Headers $verificationAdminSession.Headers

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

# Feature 137 (cross-node submission, C3): carry the citizen's public delivery
# keys in the starting-action payload. The blueprint's holderKeys field
# (format: sorcha-holder-key) is auto-filled by HolderKeyRenderer in the UI; the
# scripted path fetches the same keys from the Wallet Service so the issuer can
# bind the credential (SD-JWT cnf) and encrypt it to the citizen even when the
# owner node holds no published participant record for them (Stage-5 round-trip).
$holderKeys = Invoke-SorchaApi -Method GET `
    -Uri "$($state.walletUrl)/v1/wallet/holder-keys" `
    -Headers $citizenSession.Headers
$payloadData.holderKeys = @{
    holderJwk           = $holderKeys.holderJwk
    encryptionPublicKey = $holderKeys.encryptionPublicKey
    algorithm           = $holderKeys.algorithm
}
Write-WtInfo "Holder keys captured ($($holderKeys.algorithm)) — credential will be bound + delivered to wallet $($holderKeys.walletAddress)"

$null = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.citizenWalletAddress `
    -RegisterId $state.registerId `
    -Token $citizenSession.Token `
    -PayloadData $payloadData `
    -WaitForSeal

# ============================================================================
# Step 4: Set the pending-application notice (Feature 124 US2 / FR-009)
# ============================================================================
Write-WtStep "Step 4: Set pending-application notice on Sorcha Wallet"

Set-SorchaCitizenPendingApplication `
    -WalletUrl $state.walletUrl `
    -Label "Assured Identity" `
    -Headers $citizenSession.Headers | Out-Null

Write-WtInfo "PWA will now render the waiting-card on Home"

# ============================================================================
# Step 5: Verification Analyst approves Action 2
# ============================================================================
Write-WtStep "Step 5: Verification Analyst verifies application (Action 2)"

# Feature 145: the citizen's submit returns 202 and the instance advances only when the
# InstanceProjector folds the sealed docket (a beat after the tx seals). Wait for the
# projection to surface Action 2 as a current action before the analyst acts — the script
# equivalent of the analyst receiving the SignalR action-available notification. Without
# this the analyst races the fold and gets "Action 2 is not a current action".
Wait-SorchaActorReady -Mode AwaitingInbox `
    -InstanceId $instanceId `
    -ActionId 2 `
    -RegisterId $state.registerId `
    -Headers $analystSession.Headers `
    -GatewayUrl $state.gatewayUrl

$actionResponse = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.roles.verificationAnalyst.walletAddress `
    -RegisterId $state.registerId `
    -Token $analystSession.Token `
    -PayloadData @{
        decision          = "approved"
        verificationNotes = "Identity verified against submitted persona."
        # Feature 155 (T009): analyst confirms the citizen is 18+ from the submitted
        # date of birth. Issued as the selectively-disclosable age_over_18 claim so a
        # verifier can check age without seeing the date of birth. The persona DOB
        # ($($state.persona.dateOfBirth)) is well over 18.
        ageCheck = @{
            over18 = $true
        }
    } `
    -WaitForSeal

if ($ShowJson) { $actionResponse | ConvertTo-Json -Depth 5 | Write-Host }
Write-WtSuccess "Action 2 approved — credential issuance triggered (target: SorchaLocalWallet)"

# ============================================================================
# Step 6: Poll the citizen's wallet for the credential, then clear the notice
# ============================================================================
Write-WtStep "Step 6: Poll Sorcha Wallet /credentials for AssuredIdentityCredential"

# The vct declared by blueprints/assured-identity.json action 2 (credentialIssuanceConfig.vct).
# Kept beside the poll it drives so the two cannot drift apart silently again.
$AssuredIdentityVct = "https://sorcha.dev/vc/assured-identity/v1"

$deadline = (Get-Date).AddSeconds($DeliveryTimeoutSeconds)
$delivered = $false
$credentialId = $null

while ((Get-Date) -lt $deadline) {
    try {
        $snapshot = Invoke-SorchaApi -Method GET `
            -Uri "$($state.walletUrl)/v1/wallet/credentials" `
            -Headers $citizenSession.Headers
        if ($snapshot.credentials -and $snapshot.credentials.Count -gt 0) {
            # Match the vct the blueprint actually issues. It is a URI, and a kebab-case one:
            #   https://sorcha.dev/vc/assured-identity/v1
            # The old test `-match "AssuredIdentity"` was written when the vct was the PascalCase
            # type name, and after the VCT decoupling (#1187 — vct-only URIs) it matches NOTHING.
            # displayLabel is empty on this endpoint, so the `-or` arm never saved it either. The
            # credential arrived within seconds; the script polled the full 60 s past it and reported
            # "did not land" for a credential sitting in the response it was reading (#1427).
            $hit = $snapshot.credentials |
                Where-Object { $_.vct -eq $AssuredIdentityVct } |
                Select-Object -First 1
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

# Holder-accept claim — mirror what a real citizen does when they tap "Claim" on
# the pending-credential card in the PWA. For SorchaLocalWallet credentials the
# acceptance contract is a PATCH on the citizen-scoped wallet credential resource
# with `status: Active`. The credential lands at `PendingAcceptance` from
# InboundCredentialDetector; without this PATCH it stays in that state until the
# citizen acts in-app, so an automated walkthrough that wants to leave the wallet
# in "credential held and usable for presentation" must drive the claim itself.
# (Blueprint action-3 is HAIP-offer-shaped, not this path — see MEMORY 2026-05-25.)
$claimUri = "$($state.walletUrl)/v1/wallets/$($state.citizenWalletAddress)/credentials/$credentialId"
$claimResp = Invoke-SorchaApi -Method PATCH `
    -Uri $claimUri `
    -Body @{ status = "Active" } `
    -Headers $citizenSession.Headers
if ($claimResp.status -ne "Active") {
    Write-WtFail "Holder-accept PATCH returned status='$($claimResp.status)' (expected 'Active') for credential $credentialId"
    exit 1
}
Write-WtSuccess "Credential claimed (PATCH status=Active) — citizen wallet holding the AssuredIdentityCredential"

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
