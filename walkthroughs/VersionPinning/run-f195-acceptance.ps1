# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Feature 195 live acceptance — definition identity.
.DESCRIPTION
    A COMPANION to run-acceptance.ps1, not a replacement. That script proves Feature 194's
    guarantee (an in-flight instance keeps its definition across a republish and a restart) and
    still must pass. This one proves the four things Feature 195 adds, each unprovable before:

      1. A behavioural republish WRITES A SECOND TRANSACTION to the register. This is the check
         that fails on the pre-195 platform — the version-blind txId deduped every republish away
         while the endpoint answered 200 and the caller logged success (#1563).
      2. A byte-identical republish is a recognisable no-op: same id, no new transaction.
      3. A PRESENTATIONAL republish writes a new publication (so a relabel ships) while leaving
         execDefHash unchanged (so no fresh rehearsal is owed).
      4. The SAME definition published to a SECOND register gets a DIFFERENT identity.

    ⚠ TWO OF THESE PASS VACUOUSLY IF WRITTEN CARELESSLY, and both are handled deliberately:

      * Check 3 asserts something is UNCHANGED, which is the default outcome of doing nothing. It
        is therefore PAIRED with the behavioural republish in the same run: the pair only passes if
        execDefHash moves for one and not the other. Alone, either half is worthless.
      * Check 4 needs the two registers to receive BYTE-IDENTICAL definitions. The draft is not
        touched between the two publishes, and the script asserts the two execDefHashes are equal
        BEFORE comparing their ids.

    ⚠ THE LEDGER IS ASYNCHRONOUS. A publish returns before its transaction seals, so every count of
    publication transactions POLLS for the expected value instead of reading once. Reading once
    turns "not sealed yet" into a false FAIL on the single check that matters most.

    Self-contained: it publishes its own blueprint, so it neither depends on nor disturbs
    run-acceptance.ps1's world and the two can run in either order.
#>
[CmdletBinding()]
param(
    [string]$StatePath = (Join-Path $PSScriptRoot 'state.json'),
    [int]$SealTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot '..' 'modules' 'SorchaWalkthrough' 'SorchaWalkthrough.psm1'
Import-Module $modulePath -Force

if (-not (Test-Path $StatePath)) { throw "No state.json - run setup.ps1 first." }
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

$script:Results = [System.Collections.Generic.List[object]]::new()

function Check {
    param([string]$Phase, [string]$What, [bool]$Ok, [string]$Detail = '')
    $script:Results.Add([pscustomobject]@{ Phase = $Phase; What = $What; Ok = $Ok; Detail = $Detail })
    $tag = if ($Ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}" -f $tag, $What) -ForegroundColor $colour
    if ($Detail) { Write-Host ("         {0}" -f $Detail) -ForegroundColor DarkYellow }
}

# Nothing aborts the run. A harness that stops at the first red tells you about one defect when
# there may be four.
function Try-Get {
    param([scriptblock]$Block)
    try { return [pscustomobject]@{ Ok = $true; Value = (& $Block); Error = $null } }
    catch {
        # The status line is not a diagnosis. Capture the response BODY on the FIRST attempt.
        $body = ''
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $body = " body=$($_.ErrorDetails.Message)" }
        return [pscustomobject]@{ Ok = $false; Value = $null; Error = "$($_.Exception.Message)$body" }
    }
}

# Safe property read: under StrictMode a missing property throws, and several assertions here are
# ABOUT whether a property is present at all.
function Get-Prop {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $p = $Object.PSObject.Properties[$Name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

$bp = $state.blueprintUrl
$registerUrl = $state.registerUrl
$registerId = $state.registerId
$stamp = Get-Date -Format 'yyyyMMddHHmmss'

Write-Host ""
Write-Host "=== Feature 195 live acceptance: definition identity ===" -ForegroundColor Cyan
Write-Host "Gateway  : $($state.gatewayUrl)"
Write-Host "Register : $registerId"
Write-Host ""

$officer = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.officer.email -Password $state.officer.password -OrganizationId $state.officer.organizationId
$walletMap = @{ 'officer' = $state.officer.wallet }

function Publish-Definition {
    param([string]$BlueprintId, [string]$RegisterId)
    # The F142 rehearsal SOFT gate returns 409 REHEARSAL_REQUIRED for a scripted publish that never
    # drove the designer's rehearsal. The audited override is the documented way through it.
    Invoke-SorchaApi -Method POST -Uri "$bp/blueprints/$BlueprintId/publish" -Headers $officer.Headers -Body @{
        registerId = $RegisterId
        override   = @{ confirm = $true; reason = 'F195 acceptance: scripted republish, no UI rehearsal.' }
    }
}

function Get-PublicationsOnLedger {
    <#
      THE LEDGER, not the publishing node's memory. This endpoint reads blueprint-publish
      transactions back off the register, which is exactly what #1563 made impossible to observe:
      the endpoint answered 200 while nothing was ever written here.
    #>
    param([string]$RegisterId, [string]$BlueprintId)
    $r = Invoke-SorchaApi -Method GET -Uri "$registerUrl/registers/$RegisterId/blueprints/published" -Headers $officer.Headers
    $all = @(Get-Prop $r 'blueprints')
    return @($all | Where-Object { $_.blueprintId -eq $BlueprintId })
}

function Wait-PublicationCount {
    <#
      Polls until the ledger shows AT LEAST $Expected publications, or the timeout expires.
      A publish returns before its transaction seals; reading once would score "not sealed yet"
      as a failure of the feature — on the one check that matters most.
    #>
    param([string]$RegisterId, [string]$BlueprintId, [int]$Expected, [int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = -1
    do {
        $r = Try-Get { Get-PublicationsOnLedger -RegisterId $RegisterId -BlueprintId $BlueprintId }
        if ($r.Ok) {
            $last = @($r.Value).Count
            if ($last -ge $Expected) { return $last }
        }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    return $last
}

# =============================================================================================
# 0. Publish v1 of a blueprint of this run's own.
# =============================================================================================
Write-Host "0. Publish v1" -ForegroundColor Cyan

$v1 = Try-Get {
    Publish-SorchaBlueprint -BlueprintUrl $bp `
        -TemplatePath (Join-Path $PSScriptRoot 'blueprints' 'version-pinning-v1.json') `
        -WalletMap $walletMap -Headers $officer.Headers -IdPrefix "f195-$stamp" -RegisterId $registerId
}
if (-not $v1.Ok) {
    Check '0' 'v1 published' $false $v1.Error
    Write-Host ""
    Write-Host "Cannot continue without a published v1." -ForegroundColor Red
    exit 1
}
$blueprintId = $v1.Value.BlueprintId
Write-Host "  blueprint $blueprintId"

$ver1 = Try-Get { Invoke-SorchaApi -Method GET -Uri "$bp/blueprints/$blueprintId/versions" -Headers $officer.Headers }
$v1Entry = if ($ver1.Ok) { @($ver1.Value) | Sort-Object version | Select-Object -Last 1 } else { $null }
$v1PubTx = Get-Prop $v1Entry 'publicationTxId'
$v1Hash = Get-Prop $v1Entry 'execDefHash'

Check '0' 'v1 carries a publication transaction id (the definition IS its publish transaction)' `
    ([bool]$v1PubTx) "publicationTxId=$v1PubTx execDefHash=$v1Hash"

$countAfterV1 = Wait-PublicationCount -RegisterId $registerId -BlueprintId $blueprintId -Expected 1 -TimeoutSeconds $SealTimeoutSeconds
Check '0' 'v1 reached the LEDGER (not just the publishing node)' ($countAfterV1 -ge 1) `
    "publications on register=$countAfterV1"

# =============================================================================================
# 1. Behavioural republish writes a SECOND transaction. THE check that fails pre-195.
# =============================================================================================
Write-Host ""
Write-Host "1. Behavioural republish - action 2 gains a REQUIRED field" -ForegroundColor Cyan

# Publish-SorchaBlueprint always MINTS a new blueprint id, so it cannot republish. The whole test
# turns on this being the SAME blueprint id: update the draft in place, then publish it again.
$v2 = Try-Get {
    $v2Model = Get-Content (Join-Path $PSScriptRoot 'blueprints' 'version-pinning-v2.json') -Raw | ConvertFrom-Json
    $v2Model.id = $blueprintId
    foreach ($p in $v2Model.participants) {
        # The officer is a known participant; the applicant is OPEN and must keep a null wallet or
        # late binding is defeated and every real submitter is rejected.
        if ($walletMap.ContainsKey($p.id)) {
            $p | Add-Member -NotePropertyName walletAddress -NotePropertyValue $walletMap[$p.id] -Force
        }
    }
    Invoke-SorchaApi -Method PUT -Uri "$bp/blueprints/$blueprintId" -Headers $officer.Headers `
        -Body ($v2Model | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable) | Out-Null
    Publish-Definition -BlueprintId $blueprintId -RegisterId $registerId
}
Check '1' 'behavioural republish accepted' $v2.Ok $v2.Error

$v2PubTx = Get-Prop $v2.Value 'publicationTxId'
$v2Hash = Get-Prop $v2.Value 'execDefHash'

$countAfterV2 = Wait-PublicationCount -RegisterId $registerId -BlueprintId $blueprintId -Expected 2 -TimeoutSeconds $SealTimeoutSeconds
Check '1' 'THE #1563 CHECK: a behavioural republish writes a SECOND publication transaction' `
    ($countAfterV2 -ge 2) `
    ("publications on register: after v1=$countAfterV1, after v2=$countAfterV2 - " +
     "pre-195 this stays EQUAL while the endpoint answers 200 and the caller logs success")

Check '1' 'the republished definition has a DISTINCT identity' `
    ([bool]$v2PubTx -and $v2PubTx -ne $v1PubTx) "v1=$v1PubTx v2=$v2PubTx"

$behaviouralHashMoved = ([bool]$v2Hash -and $v2Hash -ne $v1Hash)

# =============================================================================================
# 2. A byte-identical republish is a recognisable no-op.
# =============================================================================================
Write-Host ""
Write-Host "2. Identical republish" -ForegroundColor Cyan

$noop = Try-Get { Publish-Definition -BlueprintId $blueprintId -RegisterId $registerId }
Check '2' 'identical republish still returns 200' $noop.Ok $noop.Error

$noopPubTx = Get-Prop $noop.Value 'publicationTxId'
Check '2' 'identical content yields the SAME identity' `
    ([bool]$noopPubTx -and $noopPubTx -eq $v2PubTx) "v2=$v2PubTx noop=$noopPubTx"

# Give the ledger the same chance to write a transaction it should NOT write. Asserting "no new
# transaction" the instant the call returns would pass even if one were on its way.
Start-Sleep -Seconds 30
$countAfterNoop = @((Try-Get { Get-PublicationsOnLedger -RegisterId $registerId -BlueprintId $blueprintId }).Value).Count
Check '2' 'identical republish writes NO new transaction' `
    ($countAfterNoop -eq $countAfterV2) `
    "publications before=$countAfterV2 observed after 30s=$countAfterNoop"

# The discriminator. Indistinguishability is precisely how #1563 stayed invisible, so whether a
# CALLER can tell a no-op from a real publish is itself part of the acceptance.
$noopFlag = Get-Prop $noop.Value 'alreadyPublished'
$noopFlagShown = if ($null -eq $noopFlag) { '<absent from the response>' } else { "$noopFlag" }
Check '2' 'the no-op is DISTINGUISHABLE by the caller (alreadyPublished on the publish response)' `
    ($noopFlag -eq $true) "alreadyPublished=$noopFlagShown"

# =============================================================================================
# 3. Presentational republish - PAIRED with check 1, or it proves nothing.
# =============================================================================================
Write-Host ""
Write-Host "3. Presentational republish (paired with 1)" -ForegroundColor Cyan

$relabel = Try-Get {
    $draft = Invoke-SorchaApi -Method GET -Uri "$bp/blueprints/$blueprintId" -Headers $officer.Headers
    $draft.actions[0].title = "Relabelled at $stamp"
    Invoke-SorchaApi -Method PUT -Uri "$bp/blueprints/$blueprintId" -Headers $officer.Headers `
        -Body ($draft | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable) | Out-Null
    Publish-Definition -BlueprintId $blueprintId -RegisterId $registerId
}
Check '3' 'presentational republish accepted' $relabel.Ok $relabel.Error

$relabelPubTx = Get-Prop $relabel.Value 'publicationTxId'
$relabelHash = Get-Prop $relabel.Value 'execDefHash'

Check '3' 'a relabel writes a NEW publication (so the new wording ships)' `
    ([bool]$relabelPubTx -and $relabelPubTx -ne $v2PubTx) "v2=$v2PubTx relabel=$relabelPubTx"

Check '3' 'a relabel leaves execDefHash UNCHANGED (no fresh rehearsal owed)' `
    ([bool]$relabelHash -and $relabelHash -eq $v2Hash) "v2=$v2Hash relabel=$relabelHash"

# THE COUNTERFACTUAL. Without it, "unchanged" above would also pass for a hasher that ignores
# EVERYTHING. Only the pair discriminates.
Check '3' 'PAIRED COUNTERFACTUAL: the behavioural republish DID move execDefHash' `
    $behaviouralHashMoved `
    "v1=$v1Hash v2=$v2Hash - if this fails, the check above is vacuous"

# =============================================================================================
# 4. Two registers, byte-identical definition, two identities.
# =============================================================================================
Write-Host ""
Write-Host "4. Register scoping" -ForegroundColor Cyan

$second = Try-Get {
    New-SorchaRegister -RegisterUrl $registerUrl -WalletUrl $state.walletUrl `
        -Name "F195 Second $stamp" -Description 'Feature 195 register-scoping counterfactual' `
        -TenantId $state.organizationId -OwnerUserId $officer.UserId `
        -OwnerWalletAddress $state.officer.wallet -Headers $officer.Headers
}

if (-not $second.Ok) {
    Check '4' 'second register created' $false $second.Error
} else {
    $secondId = $second.Value.RegisterId
    Write-Host "  second register $secondId"
    $null = Try-Get { Wait-SorchaRegisterRoster -GatewayUrl $state.gatewayUrl -RegisterId $secondId -Headers $officer.Headers }

    # The draft has NOT been touched since the relabel publish, so the two registers receive
    # byte-identical definitions. That is the whole point: distinctness must come from the
    # register, not from a difference in the payload.
    $onSecond = Try-Get { Publish-Definition -BlueprintId $blueprintId -RegisterId $secondId }
    Check '4' 'the same definition publishes to a second register' $onSecond.Ok $onSecond.Error

    $secondPubTx = Get-Prop $onSecond.Value 'publicationTxId'
    $secondHash = Get-Prop $onSecond.Value 'execDefHash'

    # THE COUNTERFACTUAL: identical bytes, or distinctness proves nothing.
    Check '4' 'both registers received the SAME definition (execDefHash equal)' `
        ([bool]$secondHash -and $secondHash -eq $relabelHash) `
        "first=$relabelHash second=$secondHash - if these differ, the check below passes for the wrong reason"

    Check '4' 'the same definition on two registers has TWO identities' `
        ([bool]$secondPubTx -and $secondPubTx -ne $relabelPubTx) `
        "first=$relabelPubTx second=$secondPubTx"
}

# =============================================================================================
# 5. Summary
# =============================================================================================
Write-Host ""
Write-Host "=== Result ===" -ForegroundColor Cyan
# @() around each pipeline: with StrictMode a pipeline that matches NOTHING yields $null, and
# $null.Count throws. That makes the summary blow up on precisely the run where every check
# passed — the one you most want a clean report from.
$pass = @($script:Results | Where-Object { $_.Ok }).Count
$fail = @($script:Results | Where-Object { -not $_.Ok }).Count
$script:Results | Where-Object { -not $_.Ok } | ForEach-Object {
    Write-Host ("  FAILED [step {0}] {1}" -f $_.Phase, $_.What) -ForegroundColor Red
}
Write-Host ""
Write-Host ("  {0} passed, {1} failed" -f $pass, $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
Write-Host ""
Write-Host "  blueprint : $blueprintId"
Write-Host "  v1 / v2   : $v1PubTx / $v2PubTx"
Write-Host ""
Write-Host "  NOT DONE BY THIS SCRIPT, and it outranks every check above:" -ForegroundColor Yellow
Write-Host "  the pin-fallback counter must read ZERO. There is no /metrics endpoint on this" -ForegroundColor Yellow
Write-Host "  platform (the counter is an OTLP meter), so read the log line emitted at the same" -ForegroundColor Yellow
Write-Host "  site as every increment:" -ForegroundColor Yellow
Write-Host "    docker logs sorcha-blueprint-service 2>&1 | grep -c 'pre-Feature-194 fallback'   # must be 0" -ForegroundColor DarkGray
Write-Host "  run-acceptance.ps1 (Feature 194) must ALSO pass - this script does not replace it." -ForegroundColor Yellow

if ($fail -gt 0) { exit 1 }
