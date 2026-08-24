# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Feature 195 live acceptance — definition identity.
#
# A COMPANION to run-acceptance.ps1, not a replacement. That script proves Feature 194's guarantee
# (an in-flight instance keeps its definition across a republish and a restart) and still must pass.
# This one proves the four things Feature 195 adds, each of which was unprovable before:
#
#   1. A behavioural republish WRITES A SECOND TRANSACTION to the register. This is the check that
#      fails on the pre-195 platform — the version-blind txId deduped every republish away while the
#      endpoint answered 200 and the caller logged success (#1563).
#   2. A byte-identical republish is a recognisable no-op: same id, alreadyPublished = true, and no
#      new transaction.
#   3. A PRESENTATIONAL republish writes a new publication (so a relabel ships) while leaving
#      execDefHash unchanged (so no fresh rehearsal is owed).
#   4. The SAME definition published to a SECOND register gets a DIFFERENT identity.
#
# ⚠ TWO OF THESE PASS VACUOUSLY IF WRITTEN CARELESSLY, and both are handled deliberately below:
#
#   * Check 3 asserts something is UNCHANGED, which is the default outcome of doing nothing. It is
#     therefore PAIRED with check 1's behavioural republish in the same run: the pair only passes if
#     execDefHash moves for one and not the other. Alone, either half is worthless.
#   * Check 4 needs the two registers to receive BYTE-IDENTICAL definitions. A scripted run would
#     otherwise publish two slightly different blueprints and "prove" distinctness for the wrong
#     reason. The script asserts the two payloads are equal BEFORE comparing their ids.
#
# ⚠ And the positive check that outranks all of them: pin_fallback must read ZERO. Every failure
# mode of this feature degrades to the OLD behaviour, not to an error, so absence of errors is not
# evidence.

param(
    [string]$StatePath = (Join-Path $PSScriptRoot 'state.json'),
    [string]$SecondRegisterName = 'f195-second-register'
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

# Never abort the run on one failure — a harness that stops at the first red tells you about one
# defect when there may be four.
function Try-Get {
    param([scriptblock]$Block)
    try { return [pscustomobject]@{ Ok = $true; Value = (& $Block); Error = $null } }
    catch { return [pscustomobject]@{ Ok = $false; Value = $null; Error = $_.Exception.Message } }
}

$bp = $state.blueprintUrl
$registerId = $state.registerId

Write-Host ""
Write-Host "=== Feature 195 live acceptance: definition identity ===" -ForegroundColor Cyan
Write-Host "Gateway  : $($state.gatewayUrl)"
Write-Host "Register : $registerId"
Write-Host ""

$officer = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.officer.email -Password $state.officer.password

function Publish-Definition {
    param([string]$BlueprintId, [string]$RegisterId)
    Invoke-RestMethod -Method Post -Uri "$bp/blueprints/$BlueprintId/publish" `
        -Headers @{ Authorization = "Bearer $($officer.AccessToken)" } `
        -ContentType 'application/json' `
        -Body (@{ registerId = $RegisterId } | ConvertTo-Json)
}

function Count-PublishTransactions {
    param([string]$RegisterId)
    $r = Invoke-RestMethod -Method Get -Uri "$($state.registerUrl)/api/registers/$RegisterId/blueprints/published" `
        -Headers @{ Authorization = "Bearer $($officer.AccessToken)" }
    return @($r.blueprints).Count
}

# ---------------------------------------------------------------------------------------------
# 1. A behavioural republish writes a SECOND transaction. THE check that fails pre-195.
# ---------------------------------------------------------------------------------------------
Write-Host "1. Behavioural republish" -ForegroundColor Cyan

$before = Try-Get { Count-PublishTransactions -RegisterId $registerId }
$v2 = Try-Get { Publish-Definition -BlueprintId $state.blueprintId -RegisterId $registerId }
$after = Try-Get { Count-PublishTransactions -RegisterId $registerId }

Check '1' 'behavioural republish writes a second publication transaction' `
    ($before.Ok -and $after.Ok -and $after.Value -gt $before.Value) `
    ("publications before=$($before.Value) after=$($after.Value) - " +
     "pre-195 this stays EQUAL while the endpoint answers 200")

Check '1' 'the republished definition has a distinct identity' `
    ($v2.Ok -and $v2.Value.publicationTxId -and $v2.Value.publicationTxId -ne $state.v1PublicationTxId) `
    "v1=$($state.v1PublicationTxId) v2=$($v2.Value.publicationTxId)"

$behaviouralHashMoved = $v2.Ok -and $v2.Value.execDefHash -ne $state.v1ExecDefHash

# ---------------------------------------------------------------------------------------------
# 2. A byte-identical republish is a recognisable no-op.
# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "2. Identical republish" -ForegroundColor Cyan

$countBeforeNoop = Try-Get { Count-PublishTransactions -RegisterId $registerId }
$noop = Try-Get { Publish-Definition -BlueprintId $state.blueprintId -RegisterId $registerId }
$countAfterNoop = Try-Get { Count-PublishTransactions -RegisterId $registerId }

Check '2' 'identical content yields the same identity' `
    ($noop.Ok -and $v2.Ok -and $noop.Value.publicationTxId -eq $v2.Value.publicationTxId) `
    "id=$($noop.Value.publicationTxId)"

Check '2' 'identical republish is reported as alreadyPublished' `
    ($noop.Ok -and $noop.Value.alreadyPublished -eq $true) `
    "alreadyPublished=$($noop.Value.alreadyPublished) - indistinguishability is how #1563 stayed invisible"

Check '2' 'identical republish writes NO new transaction' `
    ($countBeforeNoop.Ok -and $countAfterNoop.Ok -and $countAfterNoop.Value -eq $countBeforeNoop.Value) `
    "publications before=$($countBeforeNoop.Value) after=$($countAfterNoop.Value)"

# ---------------------------------------------------------------------------------------------
# 3. Presentational republish — PAIRED with check 1, or it proves nothing.
# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "3. Presentational republish (paired with 1)" -ForegroundColor Cyan

$relabel = Try-Get {
    $draft = Invoke-RestMethod -Method Get -Uri "$bp/blueprints/$($state.blueprintId)" `
        -Headers @{ Authorization = "Bearer $($officer.AccessToken)" }
    $draft.actions[0].title = "Relabelled at $(Get-Date -Format o)"
    Invoke-RestMethod -Method Put -Uri "$bp/blueprints/$($state.blueprintId)" `
        -Headers @{ Authorization = "Bearer $($officer.AccessToken)" } `
        -ContentType 'application/json' -Body ($draft | ConvertTo-Json -Depth 40)
    Publish-Definition -BlueprintId $state.blueprintId -RegisterId $registerId
}

Check '3' 'a relabel writes a NEW publication (so the new wording ships)' `
    ($relabel.Ok -and $v2.Ok -and $relabel.Value.publicationTxId -ne $v2.Value.publicationTxId) `
    "id=$($relabel.Value.publicationTxId)"

Check '3' 'a relabel leaves execDefHash UNCHANGED (no fresh rehearsal owed)' `
    ($relabel.Ok -and $v2.Ok -and $relabel.Value.execDefHash -eq $v2.Value.execDefHash) `
    "execDefHash=$($relabel.Value.execDefHash)"

# The counterfactual. Without this the assertion above passes for a hasher that ignores EVERYTHING.
Check '3' 'PAIRED COUNTERFACTUAL: the behavioural republish DID move execDefHash' `
    $behaviouralHashMoved `
    "v1=$($state.v1ExecDefHash) v2=$($v2.Value.execDefHash) - if this fails, check 3 above is vacuous"

# ---------------------------------------------------------------------------------------------
# 4. Two registers, byte-identical definition, two identities.
# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "4. Register scoping" -ForegroundColor Cyan

$second = Try-Get { New-SorchaRegister -RegisterUrl $state.registerUrl -Name $SecondRegisterName -Session $officer }

if (-not $second.Ok) {
    Check '4' 'second register created' $false $second.Error
} else {
    $secondId = $second.Value.registerId
    $onSecond = Try-Get { Publish-Definition -BlueprintId $state.blueprintId -RegisterId $secondId }

    # THE COUNTERFACTUAL: the two registers must receive the SAME BYTES, or distinctness proves
    # nothing. The draft was not touched between the two publishes, so the payloads are identical.
    Check '4' 'both registers received the same definition (execDefHash equal)' `
        ($onSecond.Ok -and $relabel.Ok -and $onSecond.Value.execDefHash -eq $relabel.Value.execDefHash) `
        "first=$($relabel.Value.execDefHash) second=$($onSecond.Value.execDefHash)"

    Check '4' 'the same definition on two registers has TWO identities' `
        ($onSecond.Ok -and $relabel.Ok -and $onSecond.Value.publicationTxId -ne $relabel.Value.publicationTxId) `
        "first=$($relabel.Value.publicationTxId) second=$($onSecond.Value.publicationTxId)"
}

# ---------------------------------------------------------------------------------------------
# 5. The positive check.
# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "5. pin_fallback must read ZERO" -ForegroundColor Cyan
Write-Host "   Every failure mode of this feature degrades to the OLD behaviour, not to an error," -ForegroundColor Yellow
Write-Host "   so a clean log proves nothing. Read the counter." -ForegroundColor Yellow
Write-Host ""
Write-Host "     curl -s $($state.gatewayUrl)/metrics | grep pin_fallback" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Result ===" -ForegroundColor Cyan
$pass = ($script:Results | Where-Object { $_.Ok }).Count
$fail = ($script:Results | Where-Object { -not $_.Ok }).Count
$script:Results | Where-Object { -not $_.Ok } | ForEach-Object {
    Write-Host ("  FAILED [step {0}] {1}" -f $_.Phase, $_.What) -ForegroundColor Red
}
Write-Host ""
Write-Host ("  {0} passed, {1} failed" -f $pass, $fail) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
Write-Host ""
Write-Host "  run-acceptance.ps1 (Feature 194) must ALSO pass — this script does not replace it." -ForegroundColor Yellow

if ($fail -gt 0) { exit 1 }
