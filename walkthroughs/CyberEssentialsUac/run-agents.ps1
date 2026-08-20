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

# Gate verification is via instance ROUTING, not the async /execute response.
# Under F145 the response is non-deterministic — sometimes operation-queued with
# calculations=null and transactionId="" — so reading $r0.calculations is unreliable.
# The compliant gate (computedCompliant=true) routes action 0 -> action 1; waiting
# for action 1 to become current below proves the gate fired (the wait throws and
# fails the run if action 1 never surfaces). The auto-fail mirror (action 2) is in S2.

# ------------------------------------------------------------------
# S1-3: Wait for action 1 to surface (proves compliant gate), then issue
# ------------------------------------------------------------------
Write-WtInfo "S1-3: Waiting for action 1 (Issue Posture Credential) to become current"

Wait-SorchaActorReady `
    -Mode       AwaitingInbox `
    -InstanceId $instanceAId `
    -ActionId   1 `
    -RegisterId $state.registerId `
    -Headers    $assessorSession.Headers `
    -GatewayUrl $sorchaEnv.GatewayUrl

Assert $true "Compliant gate routed action 0 -> action 1 (computedCompliant=true)"

Write-WtInfo "S1-3: Submitting action 1 — Issue Posture Credential"

# Re-carry the evidence in action 1's payload so the credential claim mappings
# (/uac/compliant, /assessment/date, ...) resolve directly from this action's data.
# A same-sender gate->issue split cannot reconstruct the gate action's evidence
# (the assessor's own /* disclosure produces no reconstructable envelope), so the
# evidence is passed forward explicitly here.
$action1Payload = @{
    issuanceNote     = "Issued on UAC pass"
    assessment       = $evidenceCompliant.assessment
    uac              = $evidenceCompliant.uac
    mfa              = $evidenceCompliant.mfa
    offboarding      = $evidenceCompliant.offboarding
    provisioning     = $evidenceCompliant.provisioning
    passwordPolicy   = $evidenceCompliant.passwordPolicy
    privilegedAccess = $evidenceCompliant.privilegedAccess
}

# Snapshot the wallet BEFORE issuing. "A credential of this type exists" is a VACUOUS
# assertion in a wallet that accumulates one per run — it passes on a credential this run
# did not issue, including a revoked one, and then the failure surfaces downstream as a 400
# that looks like a platform fault (#1503). Only a credential absent before and present
# after was issued by this action.
# NOTE: this snapshot deliberately does NOT swallow a read failure. Defaulting to an empty
# set on error makes every credential look new, so the guard would go inert at exactly the
# moment the wallet read is broken — fail-open, silently, in the one case that matters.
$postureVct   = "https://sorcha.dev/vc/cyber-essentials-uac/v1"
$subjectCreds = Get-SorchaWalletCredentialUri -WalletUrl $sorchaEnv.WalletUrl `
    -WalletAddress $state.roles.'subject-org'.walletAddress
$preIssueIds  = Get-SorchaCredentialIdSnapshot -ListUri $subjectCreds `
    -Headers $subjectSession.Headers -CredentialType $postureVct
Write-WtInfo "Wallet holds $($preIssueIds.Count) posture credential(s) before issuance"

$r1 = Invoke-SorchaAction `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -InstanceId    $instanceAId `
    -ActionId      "1" `
    -BlueprintId   $state.blueprints.'ce-uac-assessment'.id `
    -SenderWallet  $state.roles.assessor.walletAddress `
    -RegisterId    $state.registerId `
    -Token         $assessorSession.Token `
    -PayloadData   $action1Payload `
    -WaitForSeal

# The posture credential is delivered SorchaLocalWallet (Feature 106): sealed into
# the action tx, peer-replicated, then the recipient's InboundCredentialDetector
# decrypts + persists it (PendingAcceptance) a few seconds later. The async /execute
# response often omits issuedCredentialId, so poll the recipient wallet until the
# credential appears (up to 45s) rather than trusting the response field.
$walletCredUrl = $subjectCreds
$postureCred = Wait-SorchaNewCredential -ListUri $walletCredUrl -Headers $subjectSession.Headers `
    -CredentialType $postureVct -ExcludeIds $preIssueIds -TimeoutSeconds 45 -IntervalSeconds 2

if (-not $postureCred) {
    Write-WtInfo "No NEW posture credential arrived within 45s. Wallet currently holds:"
    try {
        $dbg = Invoke-SorchaApi -Method GET -Uri $walletCredUrl -Headers $subjectSession.Headers
        Resolve-SorchaCollection -Response $dbg -PropertyName 'credentials' |
            Where-Object { $_.type -eq $postureVct } |
            ForEach-Object { Write-WtInfo "   $($_.id)  status=$($_.status)" }
    } catch { }
    Write-WtInfo "The action may have minted one that could not be delivered — check the Blueprint"
    Write-WtInfo "Service log for 'Public key not found on register ... recipient skipped'."
}

Assert ($postureCred -ne $null) "action 1 issued a NEW posture credential and it reached the subject-org wallet"
Assert ($postureCred.status -ne 'revoked') "the newly issued credential is not revoked"

$credentialIdFromResponse = $postureCred.id
Write-WtInfo "Posture credential id: $credentialIdFromResponse (status: $($postureCred.status))"

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
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

Assert ([bool]$instB.id) "Blueprint B instance created — response carries .id"
$instanceBId = $instB.id
Write-WtInfo "Blueprint B instance: $instanceBId"

Write-WtInfo "S1-4: Fetching CyberEssentialsUacPosture presentation from subject-org wallet"

# Pin the EXACT credential this run issued. Selecting by type alone presents whichever is
# first in a wallet that accumulates them — the direct cause of #1477 defect 2, #1483 and
# #1503.
$pres = Get-SorchaCredentialPresentation `
    -WalletUrl       $sorchaEnv.WalletUrl `
    -WalletAddress   $state.roles.'subject-org'.walletAddress `
    -CredentialType  $postureVct `
    -CredentialId    $credentialIdFromResponse `
    -Token           $subjectSession.Token

Assert ($pres -ne $null) "subject-org holds a presentable CyberEssentialsUacPosture credential"
Assert ($pres.credentialId -eq $credentialIdFromResponse) `
    "the presentation is built from the credential this run issued ($credentialIdFromResponse)"

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

# Baseline the recipient's posture-credential count BEFORE the auto-fail run, so
# the "no credential minted" assertion is a delta (re-run safe — the wallet
# persists across invocations and accumulates one credential per happy-path run).
function Get-PostureCount {
    param($url, $headers)
    try {
        $r = Invoke-SorchaApi -Method GET -Uri $url -Headers $headers
        $items = Resolve-SorchaCollection -Response $r -PropertyName 'credentials'
        return (@($items) | Where-Object { $_.type -eq "https://sorcha.dev/vc/cyber-essentials-uac/v1" }).Count
    } catch { return 0 }
}
$postureWalletUrl = "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials/?status=All"
$postureBaseline  = Get-PostureCount $postureWalletUrl $subjectSession.Headers
Write-WtInfo "Baseline posture credentials in subject-org wallet: $postureBaseline"

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

# Gate verification is via routing (S2-2 below), not the async response — the
# /execute response is non-deterministic under F145. The auto-fail gate
# (computedCompliant=false) routes action 0 -> action 2 (Record Non-Compliance).

# ------------------------------------------------------------------
# S2-2: Assert that the route went to action 2, not action 1.
# Wait-SorchaActorReady[AwaitingInbox] for action 2 (Record Non-Compliance)
# to confirm the non-compliant route fired. If action 2 is current then
# action 1 was bypassed by the engine's route evaluation.
# ------------------------------------------------------------------
Write-WtInfo "S2-2: Asserting route went to action 2 (Record Non-Compliance), not action 1"

# Use Wait-SorchaActorReady (90s timeout, throws on miss) instead of inline poll.
# A timeout here means the non-compliance route never fired — hard fail is correct.
Wait-SorchaActorReady `
    -Mode       AwaitingInbox `
    -InstanceId $instanceA2Id `
    -ActionId   2 `
    -RegisterId $state.registerId `
    -Headers    $assessorSession.Headers `
    -GatewayUrl $sorchaEnv.GatewayUrl

# Re-fetch the instance once to make the assertion explicit.
$instStateAfterWait = Invoke-SorchaApi `
    -Method  GET `
    -Uri     "$($sorchaEnv.GatewayUrl)/api/instances/$instanceA2Id" `
    -Headers $assessorSession.Headers `
    -ShowJson:$ShowJson
Assert ($instStateAfterWait.currentActionIds -contains 2) `
    "Auto-fail route: action 2 (Record Non-Compliance) is current (not action 1)"

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
    $errStatus = $null
    try { $errStatus = $_.Exception.Response.StatusCode.value__ } catch {}
    Write-WtInfo "  Action 1 threw as expected (HTTP $errStatus): $($_.Exception.Message)"
    if ($errStatus) {
        Assert (($errStatus -ge 400) -and ($errStatus -lt 500)) "S2-3: action 1 rejected with a 4xx domain error (HTTP $errStatus), not a network/auth failure"
    }
}

Assert $threw "Issue action (1) unreachable on auto-fail route — no posture credential minted"

# Confirm the auto-fail run delivered NO new posture credential — compare against
# the baseline captured before S2 (re-run safe; the wallet persists across runs).
# Brief settle delay so a (hypothetical) erroneous delivery would have landed.
Write-WtInfo "S2-3: Confirming no new posture credential was delivered to subject-org wallet"
Start-Sleep -Seconds 5
$postureCountAfterFail = Get-PostureCount $postureWalletUrl $subjectSession.Headers
Assert ($postureCountAfterFail -eq $postureBaseline) `
    "No posture credential minted by the auto-fail run (count unchanged at $postureBaseline; S2 withheld issuance)"

Write-WtSuccess "========== SCENARIO 2 COMPLETE — ALL ASSERTIONS PASSED =========="

# ============================================================================
# Final success banner
# ============================================================================
Write-Host ""
Write-WtBanner "CyberEssentialsUac — All Scenarios PASSED"
Write-WtInfo "Scenario 1 (happy path):  Blueprint A (3 actions) + Blueprint B (2 actions) — posture credential issued + insurance quote issued"
Write-WtInfo "Scenario 2 (auto-fail):   Blueprint A action 0 routed to non-compliance (action 2); issue action (1) correctly unreachable; no credential minted"
Write-Host ""
