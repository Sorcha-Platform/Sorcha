#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CyberEssentialsUac — Run Agents
# Feature: Cyber Essentials UAC posture assessment + cyber insurance application.
#
# Executes two scenarios using state.json written by setup.ps1:
#   Scenario 1 — Happy path:  compliant evidence → posture credential issued →
#                              credential-gated insurance application → quote issued.
#   Scenario 2 — Auto-fail:   non-compliant evidence → compliance gate fails →
#                              workflow routes to Record Non-Compliance (action 2);
#                              asserts that action 1 (issue) is UNREACHABLE.
#
# All assertions are hard exits (exit 1) on failure.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    # Deploy-anywhere override — point at any node by its gateway base URL.
    # Passed through to Initialize-SorchaEnvironment; ignored when empty.
    [string]$GatewayUrl,
    [switch]$ShowJson
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) `
    "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CyberEssentialsUac — Run Agents"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ============================================================================
# Load state.json (written by setup.ps1)
# ============================================================================
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "state.json not found at $stateFile — run setup.ps1 first"
    exit 1
}

$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# ============================================================================
# Initialise environment
# ============================================================================
$initParams = @{ Profile = $Profile }
if ($GatewayUrl) { $initParams.GatewayUrl = $GatewayUrl }
$sorchaEnv = Initialize-SorchaEnvironment @initParams

# ============================================================================
# Local assertion helper
# ============================================================================
function Assert ([bool]$cond, [string]$msg) {
    if (-not $cond) {
        Write-WtFail "ASSERTION FAILED: $msg"
        exit 1
    } else {
        Write-WtSuccess "ASSERT OK: $msg"
    }
}

# ============================================================================
# Authenticate all three roles
# ============================================================================
Write-WtStep "Authenticating three roles"

$assessorSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $state.roles.assessor.email `
    -Password       $state.roles.assessor.password `
    -OrganizationId $state.roles.assessor.organizationId

$subjectSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $state.roles.'subject-org'.email `
    -Password       $state.roles.'subject-org'.password `
    -OrganizationId $state.roles.'subject-org'.organizationId

$insurerSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $state.roles.insurer.email `
    -Password       $state.roles.insurer.password `
    -OrganizationId $state.roles.insurer.organizationId

Write-WtSuccess "All three roles authenticated"

# ============================================================================
# SCENARIO 1 — Happy path
# ============================================================================
Write-WtStep "Scenario 1: Happy Path — compliant evidence, credential issuance, insurance quote"

# ------------------------------------------------------------------
# S1-1: Create Blueprint A instance (assessor)
# ------------------------------------------------------------------
Write-WtInfo "S1-1: Creating Blueprint A (CE UAC Assessment) instance"

$instABody = @{
    blueprintId = $state.blueprints.'ce-uac-assessment'.id
    registerId  = $state.registerId
    tenantId    = $state.roles.assessor.organizationId
}
$instA = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    $instABody `
    -Headers $assessorSession.Headers

Assert ([bool]$instA.id) "Blueprint A instance created — response carries .id"
$instanceAId = $instA.id
Write-WtInfo "Blueprint A instance: $instanceAId"

# ------------------------------------------------------------------
# S1-2: Submit action 0 — compliant evidence
# ------------------------------------------------------------------
Write-WtInfo "S1-2: Loading compliant evidence and submitting action 0"

$evidenceCompliant = Get-Content -Path (Join-Path $scriptDir "data/evidence-compliant.json") -Raw |
    ConvertFrom-Json -AsHashtable

# Patch the orgDid placeholder with the real assessor DID from state.
$evidenceCompliant.assessment.orgDid = $state.assessorDid

$r0 = Invoke-SorchaAction `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -InstanceId    $instanceAId `
    -ActionId      "0" `
    -BlueprintId   $state.blueprints.'ce-uac-assessment'.id `
    -SenderWallet  $state.roles.assessor.walletAddress `
    -RegisterId    $state.registerId `
    -Token         $assessorSession.Token `
    -PayloadData   $evidenceCompliant `
    -WaitForSeal

# calculatedValues is a PSObject from ConvertFrom-Json; access property directly.
# NOTE: The response field is `calculatedValues` (NOT `calculations`) — confirmed
#       against every existing walkthrough that uses Invoke-SorchaAction.
Assert ($r0.calculatedValues -ne $null) "Action 0 response carries calculatedValues"
Assert ($r0.calculatedValues.computedCompliant -eq $true) `
    "Compliant evidence => computedCompliant=true"

# ------------------------------------------------------------------
# S1-3: Wait for action 1 to surface, then issue posture credential
# ------------------------------------------------------------------
Write-WtInfo "S1-3: Waiting for action 1 (Issue Posture Credential) to become current"

Wait-SorchaActorReady `
    -Mode       AwaitingInbox `
    -InstanceId $instanceAId `
    -ActionId   1 `
    -RegisterId $state.registerId `
    -Headers    $assessorSession.Headers `
    -GatewayUrl $sorchaEnv.GatewayUrl

Write-WtInfo "S1-3: Submitting action 1 — Issue Posture Credential"

$r1 = Invoke-SorchaAction `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -InstanceId    $instanceAId `
    -ActionId      "1" `
    -BlueprintId   $state.blueprints.'ce-uac-assessment'.id `
    -SenderWallet  $state.roles.assessor.walletAddress `
    -RegisterId    $state.registerId `
    -Token         $assessorSession.Token `
    -PayloadData   @{ issuanceNote = "Issued on UAC pass" } `
    -WaitForSeal

# Primary assertion: credentialIssued object on the response.
# NOTE: The response field is `credentialIssued` (NOT `issuedCredentialId`) —
#       confirmed against every existing walkthrough that issues credentials.
$credentialIssuedObj = $r1.credentialIssued
$credentialIdFromResponse = if ($credentialIssuedObj) { $credentialIssuedObj.credentialId } else { $null }

if ($credentialIssuedObj) {
    Assert ([bool]$credentialIssuedObj) "Action 1 response carries credentialIssued object"
    Write-WtInfo "Posture credential type: $($credentialIssuedObj.credentialType)"
    Write-WtInfo "Posture credential id  : $credentialIdFromResponse"
} else {
    # Fallback: poll the wallet for a CyberEssentialsUacPosture credential.
    # This handles the case where the server does not echo credentialIssued on
    # the action response (some versions omit it when the VC delivery is async).
    Write-WtWarn "credentialIssued not on response — polling subject-org wallet for CyberEssentialsUacPosture"

    $walletCredUrl = "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials/?status=All"
    $walletCreds = Invoke-SorchaApi `
        -Method  GET `
        -Uri     $walletCredUrl `
        -Headers $subjectSession.Headers

    $postureCred = $null
    if ($walletCreds) {
        $postureCred = @($walletCreds) | Where-Object { $_.type -eq "CyberEssentialsUacPosture" } |
            Select-Object -First 1
    }

    Assert ($postureCred -ne $null) "Posture credential appears in subject-org wallet after action 1 (wallet poll fallback)"
    $credentialIdFromResponse = if ($postureCred) { $postureCred.id } else { $null }
    Write-WtInfo "Posture credential found via wallet poll: $credentialIdFromResponse"
}

# ------------------------------------------------------------------
# S1-4: Create Blueprint B instance and get presentation
# ------------------------------------------------------------------
Write-WtInfo "S1-4: Creating Blueprint B (Cyber Insurance Application) instance as subject-org"

$instBBody = @{
    blueprintId = $state.blueprints.'cyber-insurance-application'.id
    registerId  = $state.registerId
    tenantId    = $state.roles.'subject-org'.organizationId
}
$instB = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    $instBBody `
    -Headers $subjectSession.Headers

Assert ([bool]$instB.id) "Blueprint B instance created — response carries .id"
$instanceBId = $instB.id
Write-WtInfo "Blueprint B instance: $instanceBId"

Write-WtInfo "S1-4: Fetching CyberEssentialsUacPosture presentation from subject-org wallet"

$pres = Get-SorchaCredentialPresentation `
    -WalletUrl       $sorchaEnv.WalletUrl `
    -WalletAddress   $state.roles.'subject-org'.walletAddress `
    -CredentialType  "CyberEssentialsUacPosture" `
    -Token           $subjectSession.Token

Assert ($pres -ne $null) "subject-org holds a presentable CyberEssentialsUacPosture credential"

# ------------------------------------------------------------------
# S1-5: Freshness assertion on assessmentDate
# ------------------------------------------------------------------
Write-WtInfo "S1-5: Asserting posture credential freshness (assessmentDate within P1Y)"

# disclosedClaims may be a hashtable (ConvertFrom-Json -AsHashtable) or PSObject;
# handle both access patterns defensively.
$assessmentDateRaw = $null
if ($pres.disclosedClaims -is [hashtable]) {
    $assessmentDateRaw = $pres.disclosedClaims['assessmentDate']
} else {
    $assessmentDateRaw = $pres.disclosedClaims.assessmentDate
}

Assert ($assessmentDateRaw -ne $null) "disclosedClaims carries assessmentDate claim"

$assessmentDate = [datetime]$assessmentDateRaw
Assert ($assessmentDate -gt (Get-Date).AddYears(-1)) `
    "posture assessmentDate ($assessmentDate) within P1Y (freshness)"

# ------------------------------------------------------------------
# S1-6: Submit Blueprint B action 0 — Request Cover (credential-gated)
# ------------------------------------------------------------------
Write-WtInfo "S1-6: Submitting Blueprint B action 0 (Request Cover) with posture credential"

$rb0 = Invoke-SorchaAction `
    -BlueprintUrl           $sorchaEnv.BlueprintUrl `
    -InstanceId             $instanceBId `
    -ActionId               "0" `
    -BlueprintId            $state.blueprints.'cyber-insurance-application'.id `
    -SenderWallet           $state.roles.'subject-org'.walletAddress `
    -RegisterId             $state.registerId `
    -Token                  $subjectSession.Token `
    -PayloadData            @{ coverAmountGbp = 1000000; sector = "Software"; employeeCount = 58 } `
    -CredentialPresentations @($pres) `
    -WaitForSeal

Assert ([bool]$rb0.transactionId) `
    "Insurer requirement satisfied — Request Cover accepted (FailClosed, issuer-pinned)"

# ------------------------------------------------------------------
# S1-7: Wait for action 1, then submit Issue Quote (insurer)
# ------------------------------------------------------------------
Write-WtInfo "S1-7: Waiting for Blueprint B action 1 (Issue Quote) to become current"

Wait-SorchaActorReady `
    -Mode       AwaitingInbox `
    -InstanceId $instanceBId `
    -ActionId   1 `
    -RegisterId $state.registerId `
    -Headers    $insurerSession.Headers `
    -GatewayUrl $sorchaEnv.GatewayUrl

Write-WtInfo "S1-7: Submitting Blueprint B action 1 — Issue Quote"

$rb1 = Invoke-SorchaAction `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -InstanceId    $instanceBId `
    -ActionId      "1" `
    -BlueprintId   $state.blueprints.'cyber-insurance-application'.id `
    -SenderWallet  $state.roles.insurer.walletAddress `
    -RegisterId    $state.registerId `
    -Token         $insurerSession.Token `
    -PayloadData   @{ premiumGbp = 4200.0; quoteRef = "CE-Q-0001"; validUntil = "2027-06-02" } `
    -WaitForSeal

Assert ([bool]$rb1.transactionId) "Issue Quote completed — happy path green"

# ------------------------------------------------------------------
# S1-8: Persist issued credential id + Blueprint B instance id into state.json
# ------------------------------------------------------------------
Write-WtInfo "S1-8: Persisting credential id + Blueprint B instance id into state.json"

$stateObj = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$stateObj | Add-Member -NotePropertyName "issuedPostureCredentialId" -NotePropertyValue $credentialIdFromResponse -Force
$stateObj | Add-Member -NotePropertyName "insuranceInstanceId"       -NotePropertyValue $instanceBId             -Force
$stateObj | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State updated: issuedPostureCredentialId=$credentialIdFromResponse, insuranceInstanceId=$instanceBId"

Write-WtSuccess "========== SCENARIO 1 COMPLETE — ALL ASSERTIONS PASSED =========="

# ============================================================================
# SCENARIO 2 — Auto-fail (non-compliant evidence withholds credential)
# ============================================================================
Write-WtStep "Scenario 2: Auto-Fail — non-compliant evidence, credential withheld, action 1 unreachable"

# ------------------------------------------------------------------
# S2-1: New Blueprint A instance + submit non-compliant evidence
# ------------------------------------------------------------------
Write-WtInfo "S2-1: Creating second Blueprint A instance for auto-fail scenario"

$instA2 = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    $instABody `
    -Headers $assessorSession.Headers

Assert ([bool]$instA2.id) "Blueprint A (second) instance created"
$instanceA2Id = $instA2.id
Write-WtInfo "Blueprint A (auto-fail) instance: $instanceA2Id"

Write-WtInfo "S2-1: Loading auto-fail evidence and submitting action 0"

$evidenceAutofail = Get-Content -Path (Join-Path $scriptDir "data/evidence-autofail.json") -Raw |
    ConvertFrom-Json -AsHashtable

# Patch the orgDid placeholder.
$evidenceAutofail.assessment.orgDid = $state.assessorDid

$r0b = Invoke-SorchaAction `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -InstanceId    $instanceA2Id `
    -ActionId      "0" `
    -BlueprintId   $state.blueprints.'ce-uac-assessment'.id `
    -SenderWallet  $state.roles.assessor.walletAddress `
    -RegisterId    $state.registerId `
    -Token         $assessorSession.Token `
    -PayloadData   $evidenceAutofail `
    -WaitForSeal

Assert ($r0b.calculatedValues -ne $null) "Auto-fail action 0 response carries calculatedValues"
Assert ($r0b.calculatedValues.computedCompliant -eq $false) `
    "Auto-fail evidence => computedCompliant=false"

# ------------------------------------------------------------------
# S2-2: Assert that the route went to action 2, not action 1.
# Wait-SorchaActorReady[AwaitingInbox] for action 2 (Record Non-Compliance)
# to confirm the non-compliant route fired. If action 2 is current then
# action 1 was bypassed by the engine's route evaluation.
# ------------------------------------------------------------------
Write-WtInfo "S2-2: Asserting route went to action 2 (Record Non-Compliance), not action 1"

# Poll the instance to confirm action 2 is now the current action.
$instanceUrl = "$($sorchaEnv.GatewayUrl)/api/instances/$instanceA2Id"
$routeDeadline = (Get-Date).AddSeconds(60)
$action2Current = $false
while ((Get-Date) -lt $routeDeadline) {
    try {
        $instState = Invoke-SorchaApi `
            -Method  GET `
            -Uri     $instanceUrl `
            -Headers $assessorSession.Headers
        if ($instState.currentActionIds -contains 2) {
            $action2Current = $true
            break
        }
    } catch {
        # 404 is expected during projector fold window — keep polling silently
    }
    Start-Sleep -Seconds 1
}

Assert $action2Current "Auto-fail route: action 2 (Record Non-Compliance) is current (not action 1)"

# ------------------------------------------------------------------
# S2-3: Assert action 1 (Issue Posture Credential) is UNREACHABLE
# ------------------------------------------------------------------
Write-WtInfo "S2-3: Attempting action 1 (must throw — unreachable on auto-fail route)"

$threw = $false
try {
    $null = Invoke-SorchaAction `
        -BlueprintUrl  $sorchaEnv.BlueprintUrl `
        -InstanceId    $instanceA2Id `
        -ActionId      "1" `
        -BlueprintId   $state.blueprints.'ce-uac-assessment'.id `
        -SenderWallet  $state.roles.assessor.walletAddress `
        -RegisterId    $state.registerId `
        -Token         $assessorSession.Token `
        -PayloadData   @{ issuanceNote = "Should be rejected" }
    # No -WaitForSeal: if the call somehow succeeds we want to catch it here,
    # not after an expensive seal wait.
} catch {
    $threw = $true
    Write-WtInfo "Action 1 threw as expected: $($_.Exception.Message)"
}

Assert $threw "Issue action (1) unreachable on auto-fail route — no posture credential minted"

# Confirm that no CyberEssentialsUacPosture credential was delivered to
# the subject-org's wallet as a result of the auto-fail run.
Write-WtInfo "S2-3: Confirming no new posture credential was delivered to subject-org wallet"

$walletCredsAfterFail = Invoke-SorchaApi `
    -Method  GET `
    -Uri     "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials/?status=All" `
    -Headers $subjectSession.Headers

# Count posture credentials — should still be exactly the one from Scenario 1.
$postureCredCount = 0
if ($walletCredsAfterFail) {
    $postureCredCount = (@($walletCredsAfterFail) | Where-Object { $_.type -eq "CyberEssentialsUacPosture" }).Count
}
Assert ($postureCredCount -le 1) `
    "No additional posture credential issued by auto-fail run (count after both scenarios: $postureCredCount)"

Write-WtSuccess "========== SCENARIO 2 COMPLETE — ALL ASSERTIONS PASSED =========="

# ============================================================================
# Final success banner
# ============================================================================
Write-Host ""
Write-WtBanner "CyberEssentialsUac — All Scenarios PASSED"
Write-WtInfo "Scenario 1 (happy path):  Blueprint A (3 actions) + Blueprint B (2 actions) — posture credential issued + insurance quote issued"
Write-WtInfo "Scenario 2 (auto-fail):   Blueprint A action 0 routed to non-compliance (action 2); issue action (1) correctly unreachable; no credential minted"
Write-Host ""
