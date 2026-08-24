# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    The Feature 194 live acceptance test: an instance runs the definition it started on.
.DESCRIPTION
    Six steps, from the design's section 6. Step 6 — restart blueprint-service, then advance the
    OLD instance — is the one most likely to fail and the one most worth having: it is the only
    step that proves recovery restored the superseded definition rather than only the latest.

    Nothing here aborts on the first failure. A conformance run that dies at step 3 tells you one
    thing when it was about to tell you six. Every check records a verdict and the run continues.

    Read the exit line, not the absence of red: EVERY failure mode of this feature degrades to the
    OLD BEHAVIOUR rather than to an error, so "no exception" is not evidence of anything.
#>
[CmdletBinding()]
param(
    [string]$StatePath = (Join-Path $PSScriptRoot 'state.json'),
    [switch]$SkipRestart,
    [string]$SshTarget = 'sorcha@51.105.7.135',
    # Compose -f list used to recreate blueprint-service in step 6. It must match how the target
    # node was brought up, or the restart quietly swaps the image out from under the test.
    [string]$ComposeFiles = '-f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.seed.yml -f docker-compose.smtp.yml -f docker-compose.ports.yml'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot '..' 'modules' 'SorchaWalkthrough' 'SorchaWalkthrough.psm1'
Import-Module $modulePath -Force

if (-not (Test-Path $StatePath)) { throw "No state.json — run setup.ps1 first." }
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

$script:Results = [System.Collections.Generic.List[object]]::new()

function Check {
    param([string]$Phase, [string]$What, [bool]$Ok, [string]$Detail = '')
    $script:Results.Add([pscustomobject]@{ Phase = $Phase; What = $What; Ok = $Ok; Detail = $Detail })
    $tag = if ($Ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}" -f $tag, $What) -ForegroundColor $colour
    if (-not $Ok -and $Detail) { Write-Host ("         {0}" -f $Detail) -ForegroundColor DarkYellow }
}

function Try-Action {
    <#
      Returns a result object rather than throwing, so one refused submission cannot end the run.
      A schema refusal can surface EITHER as a synchronous 400 (the engine validates before
      building the transaction) OR as a 202 whose transaction never seals (the validator's own
      schema check). Both are refusals; only the shape differs, so both are captured.
    #>
    # NOT $Args. PowerShell's automatic $args wins the binding, so a parameter of that name
    # receives an Object[] and the cast to hashtable throws on EVERY call — which, since this
    # wrapper fronts every action submission, means the script could never run at all.
    param([hashtable]$ActionArgs)
    try {
        $r = Invoke-SorchaAction @ActionArgs
        return [pscustomobject]@{ Accepted = $true; Response = $r; Error = $null }
    } catch {
        # A status line is not a diagnosis. "400 (Bad Request)" is the SAME text for a schema
        # refusal and for a submission that merely arrived before the projector folded the previous
        # seal, and those mean opposite things. Capture the body on the first attempt or you will
        # re-run the whole thing to get it.
        $body = ''
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $body = " body=$($_.ErrorDetails.Message)" }
        return [pscustomobject]@{ Accepted = $false; Response = $null; Error = "$($_.Exception.Message)$body" }
    }
}

$bp = $state.blueprintUrl
$registerId = $state.registerId

Write-Host ""
Write-Host "=== Feature 194 live acceptance ===" -ForegroundColor Cyan
Write-Host "Gateway  : $($state.gatewayUrl)"
Write-Host "Register : $registerId"

$officer = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.officer.email -Password $state.officer.password -OrganizationId $state.officer.organizationId
$applicant = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.applicant.email -Password $state.applicant.password -OrganizationId $state.applicant.organizationId

# The applicant is an OPEN participant and must NOT appear in the wallet map — a baked-in wallet
# defeats late binding and rejects every real submitter.
$walletMap = @{ 'officer' = $state.officer.wallet }

# ---------------------------------------------------------------------------------------------
# STEP 1 — publish v1
# ---------------------------------------------------------------------------------------------
Write-Host "`n[1] Publish blueprint v1" -ForegroundColor Cyan
$v1 = Publish-SorchaBlueprint -BlueprintUrl $bp `
    -TemplatePath (Join-Path $PSScriptRoot 'blueprints' 'version-pinning-v1.json') `
    -WalletMap $walletMap -Headers $officer.Headers -IdPrefix "vpin-$($state.stamp)" -RegisterId $registerId
$blueprintId = $v1.BlueprintId
Write-Host "  blueprint $blueprintId"

$versions = Invoke-SorchaApi -Method GET -Uri "$bp/blueprints/$blueprintId/versions" -Headers $officer.Headers
$v1Entry = $versions | Sort-Object version | Select-Object -Last 1
$v1Hash = $v1Entry.execDefHash
# Feature 195: an instance is pinned to the PUBLICATION TRANSACTION, not to the behavioural hash.
# The two are different value spaces and several publications may share one execDefHash, so the
# pin assertions below must compare publication ids or they compare nothing.
$v1Pub = $v1Entry.publicationTxId
Check '1' 'v1 published and carries an executable-definition hash' ([bool]$v1Hash) "execDefHash=$v1Hash"
Check '1' 'v1 carries a publication transaction id (its identity)' ([bool]$v1Pub) "publicationTxId=$v1Pub"

# ---------------------------------------------------------------------------------------------
# STEP 2 — start instance A and leave it mid-flow
# ---------------------------------------------------------------------------------------------
Write-Host "`n[2] Start instance A, execute action 1, leave it mid-flow" -ForegroundColor Cyan
$instA = Invoke-SorchaApi -Method POST -Uri "$bp/instances/" -Headers $applicant.Headers -Body @{
    blueprintId = $blueprintId; registerId = $registerId; tenantId = $state.organizationId
}
$instanceA = $instA.id
Write-Host "  instance A $instanceA"

$a1 = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceA; ActionId = '1'; BlueprintId = $blueprintId
    SenderWallet = $state.applicant.wallet; RegisterId = $registerId; Token = $applicant.Token
    PayloadData  = @{ projectName = 'Riverside Depot' }; WaitForSeal = $true
}
Check '2' 'instance A action 1 accepted and sealed' $a1.Accepted $a1.Error

$pinA = Invoke-SorchaApi -Method GET -Uri "$bp/instances/$instanceA/definition" -Headers $applicant.Headers
Check '2' 'instance A is pinned to v1' ($pinA.blueprintDefinitionTxId -eq $v1Pub) `
    "pinState=$($pinA.pinState) pin=$($pinA.blueprintDefinitionTxId) expected=$v1Pub"
Check '2' 'instance A reports itself pinned to the latest (nothing republished yet)' `
    ($pinA.isPinnedToLatest -eq $true) "isPinnedToLatest=$($pinA.isPinnedToLatest)"

# ---------------------------------------------------------------------------------------------
# STEP 3 — republish with a BEHAVIOURAL change
# ---------------------------------------------------------------------------------------------
Write-Host "`n[3] Republish as v2 — action 2 gains a REQUIRED field" -ForegroundColor Cyan
# Publish-SorchaBlueprint always MINTS a new blueprint id from -IdPrefix, so it cannot republish.
# The whole test turns on this being the SAME blueprint id, so the republish is done directly:
# update the draft in place, then publish it again to the same register.
$v2Model = Get-Content (Join-Path $PSScriptRoot 'blueprints' 'version-pinning-v2.json') -Raw | ConvertFrom-Json
$v2Model.id = $blueprintId
foreach ($p in $v2Model.participants) {
    # The officer is a known participant; the applicant is OPEN and must keep a null wallet or
    # late binding is defeated and every real submitter is rejected.
    if ($walletMap.ContainsKey($p.id)) { $p | Add-Member -NotePropertyName walletAddress -NotePropertyValue $walletMap[$p.id] -Force }
}

Invoke-SorchaApi -Method PUT -Uri "$bp/blueprints/$blueprintId" -Headers $officer.Headers `
    -Body ($v2Model | ConvertTo-Json -Depth 40 | ConvertFrom-Json -AsHashtable) | Out-Null

$v2 = Invoke-SorchaApi -Method POST -Uri "$bp/blueprints/$blueprintId/publish" -Headers $officer.Headers -Body @{
    registerId = $registerId
    override   = @{ confirm = $true; reason = 'F194 acceptance: scripted republish, no UI rehearsal.' }
}
$v2Hash = $v2.execDefHash
$v2Pub = $v2.publicationTxId
Check '3' 'republish accepted while an instance is in flight' ([bool]$v2Hash) "execDefHash=$v2Hash"
Check '3' 'v2 is a DIFFERENT definition from v1' ($v2Pub -ne $v1Pub) "v1=$v1Pub v2=$v2Pub"
Check '3' 'v2 is BEHAVIOURALLY different from v1' ($v2Hash -ne $v1Hash) "v1=$v1Hash v2=$v2Hash"

$pinAAfter = Invoke-SorchaApi -Method GET -Uri "$bp/instances/$instanceA/definition" -Headers $applicant.Headers
Check '3' 'instance A''s pin is UNCHANGED by the republish' ($pinAAfter.blueprintDefinitionTxId -eq $v1Pub) `
    "pin=$($pinAAfter.blueprintDefinitionTxId) expected=$v1Pub"
Check '3' 'instance A now reports it is NOT on the latest definition' `
    ($pinAAfter.isPinnedToLatest -eq $false) "isPinnedToLatest=$($pinAAfter.isPinnedToLatest)"

# ---------------------------------------------------------------------------------------------
# STEP 4 — advance A against v1's schema, WITHOUT the new required field
# ---------------------------------------------------------------------------------------------
Write-Host "`n[4] Advance instance A — v1's rules, no complianceRef" -ForegroundColor Cyan
$a2 = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceA; ActionId = '2'; BlueprintId = $blueprintId
    SenderWallet = $state.officer.wallet; RegisterId = $registerId; Token = $officer.Token
    PayloadData  = @{ reviewNote = 'Reviewed under the rules in force when the applicant applied.' }
    WaitForSeal  = $true
}
Check '4' 'THE FEATURE: instance A advances under v1 WITHOUT the field v2 added' $a2.Accepted $a2.Error

# ---------------------------------------------------------------------------------------------
# STEP 5 — a NEW instance is governed by v2, and v2 is genuinely ENFORCED
# ---------------------------------------------------------------------------------------------
Write-Host "`n[5] Start instance B — must be pinned to v2 and must REQUIRE the new field" -ForegroundColor Cyan
$instB = Invoke-SorchaApi -Method POST -Uri "$bp/instances/" -Headers $applicant.Headers -Body @{
    blueprintId = $blueprintId; registerId = $registerId; tenantId = $state.organizationId
}
$instanceB = $instB.id
Write-Host "  instance B $instanceB"

$b1 = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceB; ActionId = '1'; BlueprintId = $blueprintId
    SenderWallet = $state.applicant.wallet; RegisterId = $registerId; Token = $applicant.Token
    PayloadData  = @{ projectName = 'Hillfoot Yard' }; WaitForSeal = $true
}
Check '5' 'instance B action 1 accepted and sealed' $b1.Accepted $b1.Error

$pinB = Invoke-SorchaApi -Method GET -Uri "$bp/instances/$instanceB/definition" -Headers $applicant.Headers
Check '5' 'instance B is pinned to v2' ($pinB.blueprintDefinitionTxId -eq $v2Pub) `
    "pin=$($pinB.blueprintDefinitionTxId) expected=$v2Pub"

# The counterfactual. Without this, "B is pinned to v2" is only a recorded value — this is what
# proves v2's rules are ENFORCED against B rather than merely noted.
$b2Bad = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceB; ActionId = '2'; BlueprintId = $blueprintId
    SenderWallet = $state.officer.wallet; RegisterId = $registerId; Token = $officer.Token
    PayloadData  = @{ reviewNote = 'Missing the field v2 requires.' }; WaitForSeal = $true
}
Check '5' 'instance B is REFUSED the v1-shaped payload (v2 is enforced, not just recorded)' `
    (-not $b2Bad.Accepted) "the submission was accepted — v2's required field is not being enforced"

$b2Good = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceB; ActionId = '2'; BlueprintId = $blueprintId
    SenderWallet = $state.officer.wallet; RegisterId = $registerId; Token = $officer.Token
    PayloadData  = @{ reviewNote = 'Reviewed.'; complianceRef = 'CR-2026-0001' }; WaitForSeal = $true
}
Check '5' 'instance B advances once the v2 field is supplied' $b2Good.Accepted $b2Good.Error

# ---------------------------------------------------------------------------------------------
# STEP 6 — THE GATE. Restart blueprint-service, then advance A again.
# ---------------------------------------------------------------------------------------------
Write-Host "`n[6] Restart blueprint-service, then advance instance A again" -ForegroundColor Cyan
if ($SkipRestart) {
    Write-Host "  -SkipRestart set; NOT restarting. Step 6 proves nothing without it." -ForegroundColor Yellow
    Check '6' 'blueprint-service restarted before the final advance' $false 'SKIPPED by -SkipRestart'
} else {
    # The -f list MUST match the one the node was brought up with. Recreating a service with a
    # SHORTER list silently reverts it to whatever `image:` the base files name — so a restart
    # meant to prove recovery would instead deploy a different build of the service under test,
    # and step 6 would be measuring the wrong binary while reporting a clean pass.
    $composeCmd = "cd /opt/sorcha && docker compose $ComposeFiles up -d --force-recreate --no-deps blueprint-service"
    Write-Host "  restarting..." -ForegroundColor DarkGray
    & ssh -o ConnectTimeout=15 $SshTarget $composeCmd 2>&1 | Select-Object -Last 3 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    # Wait for health rather than sleeping a guessed interval.
    $healthy = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 5
        $status = (& ssh -o ConnectTimeout=15 $SshTarget "docker ps --filter name=sorcha-blueprint-service --format '{{.Status}}'" 2>&1) -join ' '
        if ($status -match 'healthy') { $healthy = $true; break }
    }
    Check '6' 'blueprint-service came back healthy' $healthy
}

# Re-login: the restart drops nothing server-side, but a fresh token removes any doubt about
# token age being the reason a later call behaves differently.
$officer = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.officer.email -Password $state.officer.password -OrganizationId $state.officer.organizationId

$pinAfterRestart = Invoke-SorchaApi -Method GET -Uri "$bp/instances/$instanceA/definition" -Headers $officer.Headers
Check '6' 'instance A STILL resolves its v1 definition after the restart' `
    ($pinAfterRestart.pinState -eq 'pinned' -and $pinAfterRestart.blueprintDefinitionTxId -eq $v1Pub) `
    "pinState=$($pinAfterRestart.pinState) pin=$($pinAfterRestart.blueprintDefinitionTxId) expected=$v1Pub"

$a3 = Try-Action @{
    BlueprintUrl = $bp; InstanceId = $instanceA; ActionId = '3'; BlueprintId = $blueprintId
    SenderWallet = $state.officer.wallet; RegisterId = $registerId; Token = $officer.Token
    PayloadData  = @{ outcome = 'approved' }; WaitForSeal = $true
}
Check '6' 'THE GATE: instance A advances against v1 AFTER a restart' $a3.Accepted $a3.Error

# ---------------------------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------------------------
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
Write-Host "  instance A (v1): $instanceA"
Write-Host "  instance B (v2): $instanceB"
Write-Host "  blueprint      : $blueprintId"
Write-Host "  v1 / v2 pin    : $v1Pub / $v2Pub"
Write-Host "  v1 / v2 hash   : $v1Hash / $v2Hash"
Write-Host ""
Write-Host "  Remember: absence of errors is NOT evidence here. Confirm pin_fallback reads zero." -ForegroundColor Yellow

if ($fail -gt 0) { exit 1 }
