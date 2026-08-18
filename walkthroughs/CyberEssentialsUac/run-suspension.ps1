# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# run-suspension.ps1 — Scenario 4: suspension is REVERSIBLE (Feature 192 live gate, T015).
#
# Sibling of run-revocation.ps1, and deliberately shaped like it. That script proves a
# revoked credential is refused; this one proves a SUSPENDED credential is refused *and
# then comes back*:
#
#     suspend -> present  (MUST be refused)  -> reinstate -> present  (MUST be accepted)
#
# The reinstate leg is the whole point. A refusal assertion alone cannot tell suspension
# apart from revocation — both refuse — so a regression that quietly made suspension
# terminal would pass every "is it blocked?" test ever written. Only getting the SAME
# credential accepted again proves the reversibility is real end to end, and that is the
# leg no unit test covers.
#
# Prerequisite: setup.ps1 then run-agents.ps1 (which issues the posture credential).

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$GatewayUrl,
    [switch]$ShowJson
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) `
    "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CyberEssentialsUac — Scenario 4 (Suspension is reversible)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "state.json not found at $stateFile — run setup.ps1 first, then run-agents.ps1"
    exit 1
}
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$initParams = @{ Profile = $Profile }
if ($GatewayUrl) { $initParams.GatewayUrl = $GatewayUrl }
$sorchaEnv = Initialize-SorchaEnvironment @initParams

function Assert ([bool]$cond, [string]$msg) {
    if (-not $cond) {
        Write-WtFail "ASSERTION FAILED: $msg"
        exit 1
    } else {
        Write-WtSuccess "ASSERT OK: $msg"
    }
}

$CredType = "https://sorcha.dev/vc/cyber-essentials-uac/v1"

# ============================================================================
# Environment gate — same constraint as revocation: the status list must be a
# TLS-reachable HTTPS origin signed into the credential at issuance.
# ============================================================================
$effectiveGatewayUrl = if ($GatewayUrl) { $GatewayUrl } elseif ($state.gatewayUrl) { $state.gatewayUrl } else { "" }

$isN1 = ($Profile -eq 'n1') `
     -or ($GatewayUrl  -match 'n1\.sorcha\.dev') `
     -or ($effectiveGatewayUrl -match 'n1\.sorcha\.dev')

if (-not $isN1) {
    Write-WtBanner "Scenario 4 (suspension) — SKIPPED"
    Write-WtInfo "Needs the same HTTPS status-list configuration as Scenario 3 — see run-revocation.ps1."
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-suspension.ps1 -Profile n1"
    exit 0
}

# ============================================================================
# S4-0: Authenticate
# ============================================================================
Write-WtStep "S4-0: authenticate assessor (issuer) + subject-org (holder)"

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
# S4-1: Locate an ACTIVE posture credential
# ============================================================================
Write-WtStep "S4-1: Locate an ACTIVE posture credential in the subject-org wallet"

$walletCredUrl = "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.'subject-org'.walletAddress)/credentials/?status=All"
$walletCreds = Invoke-SorchaApi `
    -Method  GET `
    -Uri     $walletCredUrl `
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

# Must be ACTIVE: suspend refuses anything else, and this wallet accumulates one posture
# credential per happy-path run plus every credential earlier scenarios revoked. Picking
# first-of-type would make the scenario non-deterministic across runs (the #1483 mistake).
$cred = $null
if ($walletCreds) {
    $ofType = @($walletCreds) | Where-Object { $_.type -eq $CredType }
    $cred = $ofType | Where-Object { $_.status -eq 'active' } | Select-Object -First 1
}

if (-not $cred) {
    Write-WtFail "No ACTIVE CyberEssentialsUacPosture credential in the subject-org wallet."
    Write-WtFail "Run run-agents.ps1 to issue a fresh one (earlier scenarios may have consumed them)."
    exit 1
}

Write-WtInfo "Posture credential id: $($cred.id) (status: $($cred.status))"

# ============================================================================
# S4-2: Suspend
# ============================================================================
Write-WtStep "S4-2: Suspend the credential as the assessor (issuer)"

$suspend = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.GatewayUrl)/api/v1/credentials/$($cred.id)/suspend" `
    -Body    @{
        issuerWallet = $state.roles.assessor.walletAddress
        reason       = "F192 live gate — temporary hold pending re-assessment"
    } `
    -Headers $assessorSession.Headers `
    -ShowJson:$ShowJson

Assert ($suspend.status -eq "Suspended") "suspend endpoint reports Suspended (got: $($suspend.status))"

# The STATUS WORD decides which list the bit lands in, never the list id an event names
# (#1491). A suspension that reported "Revoked" here would be writing the revocation bit.
Assert ($suspend.status -ne "Revoked") "suspend did NOT report Revoked — the two purposes have separate lists since #1491"

if ($null -ne $suspend.statusListUpdated) {
    Assert ($suspend.statusListUpdated -eq $true) "suspension status-list bit updated"
}

Write-WtSuccess "Credential $($cred.id) suspended"

# ============================================================================
# S4-3: Present while suspended — expect refusal
# ============================================================================
Write-WtStep "S4-3: Present the SUSPENDED credential — expect HTTP 400"

$instS = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    @{
        blueprintId = $state.blueprints.'cyber-insurance-application'.id
        registerId  = $state.registerId
        tenantId    = $state.roles.'subject-org'.organizationId
    } `
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

Assert ([bool]$instS.id) "suspended-phase Blueprint B instance created"

$presS = Get-SorchaCredentialPresentation `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -WalletAddress  $state.roles.'subject-org'.walletAddress `
    -CredentialType $CredType `
    -CredentialId   $cred.id `
    -Token          $subjectSession.Token

Assert ($presS.credentialId -eq $cred.id) `
    "presentation is built from the SUSPENDED credential ($($cred.id)), not merely one of that type"

$rejected = $false
$status   = $null
try {
    Invoke-SorchaAction `
        -BlueprintUrl            $sorchaEnv.BlueprintUrl `
        -InstanceId              $instS.id `
        -ActionId                "0" `
        -BlueprintId             $state.blueprints.'cyber-insurance-application'.id `
        -SenderWallet            $state.roles.'subject-org'.walletAddress `
        -RegisterId              $state.registerId `
        -Token                   $subjectSession.Token `
        -PayloadData             @{ coverAmountGbp = 1000000 } `
        -CredentialPresentations @($presS)
} catch {
    $rejected = $true
    try { $status = $_.Exception.Response.StatusCode.value__ } catch {}
    Write-WtInfo "  Action 0 threw as expected (HTTP $status): $($_.Exception.Message)"
}

Assert $rejected "SUSPENDED credential REFUSED — enforcement must not be narrowed while the reason is renamed (#1495)"
if ($null -ne $status) {
    Assert ($status -eq 400) "refusal surfaced as HTTP 400 (FailClosed)"
} else {
    Assert $false "refusal has no extractable HTTP status — likely a network/auth failure, not a FailClosed 400"
}

# ============================================================================
# S4-4: Reinstate
# ============================================================================
Write-WtStep "S4-4: Reinstate the credential as the assessor"

$reinstate = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.GatewayUrl)/api/v1/credentials/$($cred.id)/reinstate" `
    -Body    @{
        issuerWallet = $state.roles.assessor.walletAddress
        reason       = "F192 live gate — re-assessment passed"
    } `
    -Headers $assessorSession.Headers `
    -ShowJson:$ShowJson

Assert ($reinstate.status -eq "Active") "reinstate endpoint reports Active (got: $($reinstate.status))"
Write-WtSuccess "Credential $($cred.id) reinstated"

# ============================================================================
# S4-5: Present after reinstatement — expect ACCEPTANCE
# ============================================================================
Write-WtStep "S4-5: Present the REINSTATED credential — expect acceptance"

$instA = Invoke-SorchaApi `
    -Method  POST `
    -Uri     "$($sorchaEnv.BlueprintUrl)/instances/" `
    -Body    @{
        blueprintId = $state.blueprints.'cyber-insurance-application'.id
        registerId  = $state.registerId
        tenantId    = $state.roles.'subject-org'.organizationId
    } `
    -Headers $subjectSession.Headers `
    -ShowJson:$ShowJson

Assert ([bool]$instA.id) "reinstated-phase Blueprint B instance created"

$presA = Get-SorchaCredentialPresentation `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -WalletAddress  $state.roles.'subject-org'.walletAddress `
    -CredentialType $CredType `
    -CredentialId   $cred.id `
    -Token          $subjectSession.Token

Assert ($presA.credentialId -eq $cred.id) "presentation is built from the SAME credential that was suspended and reinstated"

$accepted = $true
$acceptErr = $null
try {
    Invoke-SorchaAction `
        -BlueprintUrl            $sorchaEnv.BlueprintUrl `
        -InstanceId              $instA.id `
        -ActionId                "0" `
        -BlueprintId             $state.blueprints.'cyber-insurance-application'.id `
        -SenderWallet            $state.roles.'subject-org'.walletAddress `
        -RegisterId              $state.registerId `
        -Token                   $subjectSession.Token `
        -PayloadData             @{ coverAmountGbp = 1000000 } `
        -CredentialPresentations @($presA) `
        -WaitForSeal | Out-Null
} catch {
    $accepted  = $false
    $acceptErr = $_.Exception.Message
    Write-WtFail "  Action 0 refused AFTER reinstatement: $acceptErr"
}

Assert $accepted `
    "REINSTATED credential ACCEPTED — the assertion that proves suspension is reversible, and the one a suspension/revocation conflation fails"

Write-Host ""
Write-WtBanner "Scenario 4 (suspension) — PASS on n1"
Write-WtInfo "The SAME credential was refused while suspended and accepted after reinstatement,"
Write-WtInfo "verified by docket seal. Revocation stays terminal — that is run-revocation.ps1's job."
Write-Host ""
