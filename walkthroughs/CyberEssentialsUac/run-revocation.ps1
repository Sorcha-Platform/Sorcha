#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CyberEssentialsUac — Scenario 3: Mid-Cycle Revocation → FailClosed
#
# Verifies that a posture credential that was valid at the time the initial
# Blueprint B "Request Cover" action was submitted CANNOT be re-used after
# the issuer revokes it.  A fresh Blueprint B instance is created, the
# revoked credential is presented as the gate, and the engine must reject the
# submission with HTTP 400 (FailClosed).
#
# HARD-SKIP on local Docker stack:
#   The revocation check relies on the Blueprint Service fetching and verifying
#   the signed IETF Token Status List JWT over HTTPS.  The local Docker stack
#   cannot satisfy this because:
#     (a) the gateway is plain HTTP and the credential's embedded statusListUrl
#         points to an HTTP origin — the status-list embedding guard rejects it;
#     (b) even if the URL were reachable, Schannel on Windows cannot verify
#         self-signed container certs.
#   Run this scenario on n1 where StatusList__BaseUrl and
#   CredentialStatus__EnableEmbedding=true are configured.

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

Write-WtBanner "CyberEssentialsUac — Scenario 3 (Revocation)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ============================================================================
# Load state.json (written by setup.ps1 + run-agents.ps1)
# ============================================================================
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "state.json not found at $stateFile — run setup.ps1 first, then run-agents.ps1"
    exit 1
}

$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# ============================================================================
# Initialise environment (skip Docker check — n1 profile has no local Docker)
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
# Environment gate — hard-skip on non-n1
# ============================================================================
$effectiveGatewayUrl = if ($GatewayUrl) { $GatewayUrl } elseif ($state.gatewayUrl) { $state.gatewayUrl } else { "" }

$isN1 = ($Profile -eq 'n1') `
     -or ($GatewayUrl  -match 'n1\.sorcha\.dev') `
     -or ($effectiveGatewayUrl -match 'n1\.sorcha\.dev')

if (-not $isN1) {
    Write-WtBanner "Scenario 3 (revocation) — SKIPPED"
    Write-WtInfo "Mid-cycle revocation needs a TLS-reachable HTTPS status list signed into the"
    Write-WtInfo "credential at issuance + status-list embedding ON.  The local Docker stack"
    Write-WtInfo "cannot satisfy this:"
    Write-WtInfo "  • StatusList__BaseUrl must resolve to an HTTPS origin reachable by the"
    Write-WtInfo "    Blueprint Service container at presentation-verify time."
    Write-WtInfo "  • CredentialStatus__EnableEmbedding=true must be set on the issuing node."
    Write-WtInfo "  • The issuance guard forbids plain-HTTP status-list URLs, and Schannel"
    Write-WtInfo "    cannot verify self-signed container certificates."
    Write-Host ""
    Write-WtInfo "Run against n1 once those are configured:"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/setup.ps1    -GatewayUrl https://n1.sorcha.dev"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1 -GatewayUrl https://n1.sorcha.dev"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-revocation.ps1 -GatewayUrl https://n1.sorcha.dev"
    exit 0
}

# ============================================================================
# n1 path — Revoke → Re-present → Assert 400
# ============================================================================
Write-WtStep "Scenario 3 (n1): authenticate assessor + subject-org"

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

Write-WtSuccess "Both roles authenticated"

# ============================================================================
# S3-1: Locate the posture credential in the subject-org wallet
# ============================================================================
Write-WtStep "S3-1: Locate posture credential in subject-org wallet"

$walletCredUrl = "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials/?status=All"
$walletCreds = Invoke-SorchaApi `
    -Method  GET `
    -Uri     $walletCredUrl `
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

# Pick an ACTIVE credential to revoke. Selecting first-of-type makes the scenario
# non-deterministic across runs: this wallet gains one posture credential per happy-path run and
# keeps the revoked ones, so a re-run can pick an already-revoked credential and then fail at S3-2
# with "must be in Active or Suspended state to revoke".
# There is deliberately NO "otherwise take the first one" fallback: that is the very failure
# this comment describes. Falling back to a revoked credential turns a missing precondition
# into an unrecognisable "must be in Active or Suspended state" error one step later, blaming
# the revoke endpoint for the selection.
$cred = $null
$ofType = @()
if ($walletCreds) {
    $ofType = @($walletCreds) | Where-Object { $_.type -eq "https://sorcha.dev/vc/cyber-essentials-uac/v1" }
    $cred = $ofType | Where-Object { $_.status -eq 'active' } | Select-Object -First 1
}

if (-not $cred) {
    if ($ofType.Count -gt 0) {
        Write-WtFail "No ACTIVE CyberEssentialsUacPosture credential in the subject-org wallet — this scenario needs one to revoke."
        Write-WtFail "The wallet holds $($ofType.Count) of that type, all in a non-active state:"
        $ofType | ForEach-Object { Write-WtFail "   $($_.id)  status=$($_.status)" }
    } else {
        Write-WtFail "No CyberEssentialsUacPosture credential found in subject-org wallet."
    }
    Write-WtFail "Run run-agents.ps1 first to complete the happy-path scenario and issue a fresh credential."
    exit 1
}

Write-WtInfo "Posture credential id: $($cred.id)"
Write-WtInfo "Posture credential status: $($cred.status)"

# ============================================================================
# S3-2: Revoke the posture credential (assessor is the issuer)
# ============================================================================
Write-WtStep "S3-2: Revoke posture credential as assessor (issuer)"

$revokeBody = @{
    issuerWallet = $state.roles.assessor.walletAddress
    reason       = "Mid-cycle control lapse: admin MFA disabled"
}

$revoke = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.GatewayUrl)/api/v1/credentials/$($cred.id)/revoke" `
    -Body    $revokeBody `
    -Headers $assessorSession.Headers `
    -ShowJson:$ShowJson

Assert ($revoke.status -eq "Revoked") "revoke endpoint reports Revoked (got: $($revoke.status))"
Write-WtInfo "Revoke response: status=$($revoke.status), revokedAt=$($revoke.revokedAt)"

# statusListUpdated is only present when an IETF Token Status List bit was flipped
if ($null -ne $revoke.statusListUpdated) {
    Assert ($revoke.statusListUpdated -eq $true) "status-list bit updated"
}

Write-WtSuccess "Credential $($cred.id) revoked"

# ============================================================================
# S3-3: Create a new Blueprint B instance (subject-org)
# ============================================================================
Write-WtStep "S3-3: Creating new Blueprint B instance for post-revocation test"

$instRBody = @{
    blueprintId = $state.blueprints.'cyber-insurance-application'.id
    registerId  = $state.registerId
    tenantId    = $state.roles.'subject-org'.organizationId
}
$instR = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    $instRBody `
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

Assert ([bool]$instR.id) "post-revocation Blueprint B instance created"
Write-WtInfo "Post-revocation Blueprint B instance: $($instR.id)"

# ============================================================================
# S3-4: Build presentation from the (now-revoked) credential
# ============================================================================
Write-WtStep "S3-4: Building presentation from revoked credential"

# Get-SorchaCredentialPresentation auto-accepts PendingAcceptance creds and
# exports the raw token.  The credential is Revoked in the issuer wallet but
# the subject-org wallet may still hold the cached copy — the status-list check
# happens server-side at blueprint action execute time, not client-side here.
# Pin the EXACT credential that was just revoked. Selecting by type alone presents whichever
# credential happens to be first in the wallet, and this wallet accumulates one posture credential
# per run — so the scenario silently presented an ACTIVE credential and then asserted that the
# platform should have refused it. The platform was right and the test was wrong (n1, 2026-08-17).
$presR = Get-SorchaCredentialPresentation `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -WalletAddress  $state.roles.'subject-org'.walletAddress `
    -CredentialType "https://sorcha.dev/vc/cyber-essentials-uac/v1" `
    -CredentialId   $cred.id `
    -Token          $subjectSession.Token

Assert ($presR.credentialId -eq $cred.id) `
    "presentation is built from the REVOKED credential ($($cred.id)), not merely one of that type"

Assert ($presR -ne $null) "presentation object constructed from revoked credential"

# ============================================================================
# S3-5: Submit Blueprint B action 0 — expect HTTP 400 (FailClosed)
# ============================================================================
Write-WtStep "S3-5: Submitting Blueprint B action 0 with revoked credential — expect rejection"

# Do NOT pass -WaitForSeal: we expect Invoke-SorchaAction to throw before any
# transaction is created (the engine rejects at presentation-verify time, before
# sealing begins).

$rejected = $false
$status   = $null

try {
    Invoke-SorchaAction `
        -BlueprintUrl            $sorchaEnv.BlueprintUrl `
        -InstanceId              $instR.id `
        -ActionId                "0" `
        -BlueprintId             $state.blueprints.'cyber-insurance-application'.id `
        -SenderWallet            $state.roles.'subject-org'.walletAddress `
        -RegisterId              $state.registerId `
        -Token                   $subjectSession.Token `
        -PayloadData             @{ coverAmountGbp = 1000000 } `
        -CredentialPresentations @($presR)
    # If we reach here the action was NOT rejected — the revocation check failed to fire.
} catch {
    $rejected = $true
    try { $status = $_.Exception.Response.StatusCode.value__ } catch {}
    Write-WtInfo "  Action 0 threw as expected (HTTP $status): $($_.Exception.Message)"
}

Assert $rejected "post-revocation Request Cover REJECTED (FailClosed)"

if ($null -ne $status) {
    Assert ($status -eq 400) "rejection surfaced as HTTP 400 (generic body — the revoked reason is logged server-side, not returned)"
} else {
    Assert $false "rejection has no extractable HTTP status — likely a network/auth failure, not a FailClosed 400. Cannot confirm revocation rejection."
}

Write-Host ""
Write-WtBanner "Scenario 3 (revocation) — PASS on n1"
Write-WtInfo "Revoked CyberEssentialsUacPosture credential correctly blocked Blueprint B"
Write-WtInfo "action 0 (Request Cover) with FailClosed behaviour (HTTP 400)."
Write-Host ""
