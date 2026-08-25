# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Proves — from the bytes a node actually stored — that a Normal register encrypts field VALUES
    and a DevMode register does not, by PROMOTING one register between the two halves.
    Covers #1580 (encryption at rest is unverified) and #1579 (the promotion has no coverage).

.DESCRIPTION
    THE PAIRING IS THE WHOLE DESIGN.

    "The sentinel is absent" is not evidence of encryption. It is equally the result of a probe
    looking at the wrong register, at the wrong transaction, or one that silently failed to decode
    — the vacuous pass this codebase keeps producing. Only a PAIR discriminates: the SAME probe
    must FIND the sentinel while the register is in DevMode and FAIL to find it once it is Normal.

    So the DevMode half runs FIRST and is a HARD GATE. If it cannot see plaintext, the run stops
    and reports that it cannot trust anything the Normal half would say. Nothing downstream is
    reported as passing on the strength of a probe that has not demonstrated it can see.

    The controlled variable is kept to exactly one. Both halves execute the SAME ACTION of the SAME
    BLUEPRINT on the SAME REGISTER with the same field names — two instances, one before promotion
    and one after. A design that compared action 1 with action 2, or two different registers, would
    let a difference be attributed to the action or the register rather than to encryption.

    Three further things it asserts that "sentinel absent" alone does not:

      * That what is stored is ENCRYPTED rather than merely ENCODED. Payloads[].Data is Base64Url
        either way, so an unencrypted DevMode payload looks exactly as opaque in mongosh as a real
        ciphertext. Every sentinel is therefore searched for as raw text, base64, base64url, hex and
        UTF-16 — and searched for INSIDE the decoded ciphertext, which an AEAD output cannot contain.
      * That the probe is not a yes-machine. A sentinel that was never seeded must be absent from
        the very bytes where the seeded ones were found.
      * That only field NAMES are in the clear. disclosedFields is public by design so a node can
        route without decrypting; a field VALUE in the clear is the defect.

.PARAMETER OwnerSshHost
    ssh target for the node that OWNS the register ("sorcha@51.105.7.135"). Omit when its MongoDB
    is reachable through docker on this machine.

.PARAMETER ReplicaSshHost
    Optional. ssh target for a second node that should hold a replica. When given, the check is
    GATED on the transaction actually being present there: a register that never replicated makes
    "the sentinel is absent on the replica" the emptiest pass in the whole design, so that case is
    reported as NOT RUN, never as a pass.

.PARAMETER ReplicaGatewayUrl
    Optional. When given alongside -ReplicaSshHost, the replica node is subscribed to the register
    so it has a chance to pull it before the replica probe runs.
#>
[CmdletBinding()]
param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'n1',
    [string]$GatewayUrl,
    [string]$OwnerSshHost,
    [string]$ReplicaSshHost,
    [string]$ReplicaGatewayUrl,
    [int]$PromotionTimeoutSeconds = 180,
    [int]$ReplicaTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot '..' 'modules' 'SorchaWalkthrough' 'SorchaWalkthrough.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'StorageProbe.psm1') -Force

$statePath = Join-Path $PSScriptRoot 'state.json'
if (-not (Test-Path $statePath)) { throw "No state.json. Run setup.ps1 first." }
$state = Get-Content $statePath -Raw | ConvertFrom-Json

# ---------------------------------------------------------------------------------------------
# Scoring. Nothing aborts except the two declared hard gates: a run that dies on the first red
# tells you about one property when it was about to tell you fifteen.
# ---------------------------------------------------------------------------------------------
$script:Results = New-Object System.Collections.Generic.List[object]

function Check {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Claim,
        [Parameter(Mandatory)][bool]$Passed,
        [string]$Detail = ''
    )
    $script:Results.Add([pscustomobject]@{ Id = $Id; Claim = $Claim; Status = if ($Passed) { 'PASS' } else { 'FAIL' }; Detail = $Detail })
    $colour = if ($Passed) { 'Green' } else { 'Red' }
    $tag = if ($Passed) { '[PASS]' } else { '[FAIL]' }
    Write-Host ("  {0} {1}  {2}" -f $tag, $Id, $Claim) -ForegroundColor $colour
    if (-not $Passed -and $Detail) { Write-Host ("         -> {0}" -f $Detail) -ForegroundColor Red }
}

function Skip {
    param([string]$Id, [string]$Claim, [string]$Reason)
    $script:Results.Add([pscustomobject]@{ Id = $Id; Claim = $Claim; Status = 'NOT RUN'; Detail = $Reason })
    Write-Host ("  [ -- ] {0}  {1}" -f $Id, $Claim) -ForegroundColor DarkYellow
    Write-Host ("         -> NOT RUN: {0}" -f $Reason) -ForegroundColor DarkYellow
}

function Fatal {
    param([string]$Message)
    Write-Host ""
    Write-Host "HARD GATE FAILED — stopping." -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
    Write-Host ""
    Write-Summary
    exit 1
}

function Write-Summary {
    $pass = @($script:Results | Where-Object Status -eq 'PASS').Count
    $fail = @($script:Results | Where-Object Status -eq 'FAIL').Count
    $skip = @($script:Results | Where-Object Status -eq 'NOT RUN').Count
    Write-Host ""
    Write-Host ("SUMMARY: {0} passed, {1} failed, {2} not run" -f $pass, $fail, $skip) `
        -ForegroundColor $(if ($fail -gt 0) { 'Red' } elseif ($skip -gt 0) { 'Yellow' } else { 'Green' })
    if ($fail -gt 0) {
        Write-Host ""
        foreach ($r in $script:Results | Where-Object Status -eq 'FAIL') {
            Write-Host ("  FAILED {0}: {1}" -f $r.Id, $r.Claim) -ForegroundColor Red
            if ($r.Detail) { Write-Host ("    {0}" -f $r.Detail) -ForegroundColor Red }
        }
    }
}

# ---------------------------------------------------------------------------------------------
# Sentinels. One per field, each carrying a per-run token, so a hit cannot be residue from an
# earlier run and a value cannot be confused with a field name.
# ---------------------------------------------------------------------------------------------
$runToken = ([guid]::NewGuid().ToString('N')).Substring(0, 12).ToUpperInvariant()

function New-SentinelSet {
    param([string]$Phase)
    [ordered]@{
        caseReference           = "CASE-$Phase-$runToken"
        applicantFullName       = "Sentinel $Phase Fullname $runToken"
        nationalInsuranceNumber = "QQ$runToken`C"
        medicalNotes            = "Confidential $Phase medical note $runToken"
    }
}

$devSentinels = New-SentinelSet -Phase 'DEV'
$normSentinels = New-SentinelSet -Phase 'NORM'

# Never submitted anywhere. If the probe reports finding this, the probe matches everything and
# every "found" result it has produced is worthless.
$controlSentinel = "NEVER-SUBMITTED-$runToken"

$sorchaEnv = if ($GatewayUrl) { Initialize-SorchaEnvironment -GatewayUrl $GatewayUrl } `
             else { Initialize-SorchaEnvironment -Profile $Profile }

# The probe needs a route to the OWNING node's MongoDB, which is a different thing from the
# gateway URL. Derive it from the target when the caller has not said, so this drops into
# run-all.ps1 with the same -Profile/-GatewayUrl arguments every other step takes. An explicit
# -OwnerSshHost always wins.
if (-not $PSBoundParameters.ContainsKey('OwnerSshHost')) {
    $OwnerSshHost = if ($sorchaEnv.GatewayUrl -match 'n1\.sorcha\.dev') { 'sorcha@51.105.7.135' } else { '' }
}

Write-WtBanner "Encryption at rest — conformance (#1580 / #1579)"
Write-Host ("  gateway  : {0}" -f $sorchaEnv.GatewayUrl)
Write-Host ("  register : {0}" -f $state.registerId)
Write-Host ("  owner db : {0}" -f $(if ($OwnerSshHost) { $OwnerSshHost } else { 'local docker' }))
Write-Host ("  run token: {0}" -f $runToken)
Write-Host ""

$ownerProbe = if ($OwnerSshHost) { New-SorchaStorageProbe -Name 'owner' -SshHost $OwnerSshHost } `
              else { New-SorchaStorageProbe -Name 'owner' }

$officer = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.officer.email `
    -Password $state.officer.password -OrganizationId $state.officer.organizationId
$applicant = Connect-SorchaUser -TenantUrl $state.tenantUrl -Email $state.applicant.email `
    -Password $state.applicant.password -OrganizationId $state.applicant.organizationId

function New-Instance {
    $r = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Headers $officer.Headers -Body @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $state.organizationId
    }
    if (-not $r.id) { throw "Instance creation returned no id." }
    return $r.id
}

function Submit-Sentinels {
    param([string]$InstanceId, [hashtable]$Payload, [pscustomobject]$Session, [string]$ActionId = '1')
    $resp = Invoke-SorchaAction `
        -BlueprintUrl $state.blueprintUrl -InstanceId $InstanceId -ActionId $ActionId `
        -BlueprintId $state.blueprintId -SenderWallet $Session.Wallet -RegisterId $state.registerId `
        -Token $Session.Token -PayloadData $Payload -WaitForSeal
    if ([string]::IsNullOrWhiteSpace($resp.transactionId)) {
        throw "Action $ActionId sealed but no transaction id was resolved — cannot probe storage for it."
    }
    return $resp.transactionId
}

$officerCtx = [pscustomobject]@{ Token = $officer.Token; Wallet = $state.officer.wallet }
$applicantCtx = [pscustomobject]@{ Token = $applicant.Token; Wallet = $state.applicant.wallet }

# =============================================================================================
# PHASE 0 — the probe must prove it works before anything depends on it
# =============================================================================================
Write-Host "PHASE 0 — probe" -ForegroundColor Cyan
try {
    $reach = Test-SorchaProbeReachable -Probe $ownerProbe
    Check 'P0.1' 'the storage probe can reach the owning node MongoDB' ([bool]$reach.ok) ''
} catch {
    Fatal "The storage probe cannot reach the owner node: $($_.Exception.Message)"
}

# =============================================================================================
# PHASE 1 — DEVMODE: the probe MUST find plaintext. HARD GATE.
# =============================================================================================
Write-Host ""
Write-Host "PHASE 1 — DevMode: the probe must SEE the plaintext (hard gate)" -ForegroundColor Cyan

$devInstance = New-Instance
$devTxId = Submit-Sentinels -InstanceId $devInstance -Payload $devSentinels -Session $applicantCtx
Write-Host "  devmode action-1 tx $devTxId"

$devStored = Get-SorchaStoredTransaction -Probe $ownerProbe -RegisterId $state.registerId -TxId $devTxId

Check 'P1.1' 'the owning node holds a database for this register' $devStored.RegisterPresent `
    "no sorcha_register_$($state.registerId) on the owner — the probe is pointed at the wrong node"
Check 'P1.2' 'the sealed DevMode transaction is present in storage' $devStored.Exists `
    "register present but TxId $devTxId absent (register holds $($devStored.TxCount) transactions)"

if (-not $devStored.Exists) {
    Fatal ("The DevMode transaction could not be read from storage, so the probe has NOT " +
           "demonstrated it can see anything. Every later 'sentinel absent' result would be " +
           "unfalsifiable. Refusing to report the encrypted half.")
}

$devShape = Get-SorchaStoredEnvelopeShape -Payload $devStored.Payloads[0]
Check 'P1.3' 'the stored DevMode payload is the PLAINTEXT envelope shape' ($devShape -eq 'plaintext') `
    "expected 'plaintext', got '$devShape'"

$devMisses = @()
foreach ($field in $devSentinels.Keys) {
    $hit = Find-SorchaSentinel -Stored $devStored -Sentinel $devSentinels[$field]
    if (-not $hit.Found) { $devMisses += $field }
}
Check 'P1.4' 'EVERY submitted field VALUE is readable in the stored DevMode bytes' ($devMisses.Count -eq 0) `
    "the probe could not find: $($devMisses -join ', '). It has not shown it can see plaintext, so it cannot show that encryption hides it."

if ($devMisses.Count -gt 0) {
    Fatal ("The probe failed to find plaintext that IS there in a DevMode register. Until the same " +
           "probe demonstrably sees a value, its failure to see one proves nothing — this is the " +
           "vacuous pass the whole design exists to prevent.")
}

$controlHit = Find-SorchaSentinel -Stored $devStored -Sentinel $controlSentinel
Check 'P1.5' 'a sentinel that was NEVER submitted is absent from those same bytes' (-not $controlHit.Found) `
    "the probe reported finding '$controlSentinel', which was never sent anywhere — it matches everything, so its positive results mean nothing"

# =============================================================================================
# PHASE 2 — the promotion itself (#1579)
# =============================================================================================
Write-Host ""
Write-Host "PHASE 2 — DevMode -> Normal promotion" -ForegroundColor Cyan

$promoteOk = $false
$promoteTxId = $null
try {
    # The REAL endpoint. TradeFinance called PATCH /registers/{id} with {devMode:false}: there is no
    # MapPatch in Register.Service at all, and UpdateRegisterRequest has no DevMode field — so it
    # printed "This operation is IRREVERSIBLE" inside a try/catch and changed nothing (#1579).
    $promote = Invoke-SorchaApi -Method POST `
        -Uri "$($state.registerUrl)/registers/$($state.registerId)/disable-dev-mode" `
        -Headers $officer.Headers -Body @{}
    $promoteOk = $true
    $promoteTxId = $promote.txId
} catch {
    Check 'P2.1' 'POST /api/registers/{id}/disable-dev-mode is accepted' $false $_.Exception.Message
}
if ($promoteOk) {
    Check 'P2.1' 'POST /api/registers/{id}/disable-dev-mode is accepted' $true ''
    Check 'P2.2' 'the promotion returns a control-transaction id, not just a 200' `
        (-not [string]::IsNullOrWhiteSpace($promoteTxId)) `
        "no txId on the response — a promotion that is not a transaction cannot replicate"
}

if (-not $promoteOk) {
    Fatal "The register could not be promoted, so there is no encrypted half to measure."
}

# The 200 means SUBMITTED. The flag flips on each node only when the CryptoPolicyUpdate control
# transaction SEALS, so polling the register is the only honest confirmation.
$deadline = (Get-Date).AddSeconds($PromotionTimeoutSeconds)
$devModeNow = $true
while ((Get-Date) -lt $deadline) {
    $reg = Invoke-SorchaApi -Method GET -Uri "$($state.registerUrl)/registers/$($state.registerId)" -Headers $officer.Headers
    $devModeNow = [bool]$reg.devMode
    if (-not $devModeNow) { break }
    Start-Sleep -Seconds 3
}
Check 'P2.3' 'the register reports devMode=false once the control transaction seals' (-not $devModeNow) `
    "still devMode=true after $PromotionTimeoutSeconds s — the promotion was submitted but never sealed"

if ($devModeNow) {
    Fatal "The register never left DevMode, so the encrypted half would measure a DevMode register."
}

$promoStored = Get-SorchaStoredTransaction -Probe $ownerProbe -RegisterId $state.registerId -TxId $promoteTxId
Check 'P2.4' 'the promotion is a transaction on the ledger, not a local flag flip' $promoStored.Exists `
    "the CryptoPolicyUpdate tx $promoteTxId is not in storage — a local-only flip would desync every replica"

# =============================================================================================
# PHASE 3 — NORMAL: the SAME action, the SAME fields, one variable changed
# =============================================================================================
Write-Host ""
Write-Host "PHASE 3 — Normal (encrypted): the same probe must NOT find the values" -ForegroundColor Cyan

$normInstance = New-Instance
$normTxId = Submit-Sentinels -InstanceId $normInstance -Payload $normSentinels -Session $applicantCtx
Write-Host "  normal action-1 tx $normTxId"

$normStored = Get-SorchaStoredTransaction -Probe $ownerProbe -RegisterId $state.registerId -TxId $normTxId

# Built-in discrimination proof. Every predicate below is only meaningful if it can return the
# OTHER answer, so the same shape predicate is run against the DevMode transaction first: it must
# say 'plaintext' there and 'encrypted' here. Without this, a predicate hard-wired to 'encrypted'
# — or one reading a field that no longer exists — would report the platform as sound forever.
$devShapeRecheck = Get-SorchaStoredEnvelopeShape -Payload $devStored.Payloads[0]
Check 'P3.0' 'the envelope-shape predicate DISCRIMINATES (it says plaintext for the DevMode tx)' `
    ($devShapeRecheck -eq 'plaintext') `
    "the same predicate returned '$devShapeRecheck' for a transaction known to be plaintext, so its verdict below carries no information"

Check 'P3.1' 'the sealed Normal transaction is present in storage' $normStored.Exists `
    "TxId $normTxId absent — nothing below can be trusted"

if ($normStored.Exists) {
    $normShape = Get-SorchaStoredEnvelopeShape -Payload $normStored.Payloads[0]
    Check 'P3.2' 'the stored Normal payload is the ENCRYPTED envelope shape' ($normShape -eq 'encrypted') `
        ("expected 'encrypted' (contentEncoding + encryptedPayloads), got '$normShape'. " +
         "'plaintext' here is the FAIL-OPEN: with no resolvable recipient key ActionExecutionService " +
         "falls through to the plaintext builder and writes clear values to an encrypted register.")

    $normHits = @()
    foreach ($field in $normSentinels.Keys) {
        $hit = Find-SorchaSentinel -Stored $normStored -Sentinel $normSentinels[$field]
        if ($hit.Found) {
            $normHits += ("{0} [{1}]" -f $field, (($hit.Hits | ForEach-Object { "$($_.Where):$($_.Encoding)" }) -join '; '))
        }
    }
    Check 'P3.3' 'NO submitted field VALUE is recoverable from the stored Normal bytes' ($normHits.Count -eq 0) `
        "found in the clear: $($normHits -join ' | ')"

    # The distinguishing assertion (#1580, point 3). P3.3 already searches every encoding of every
    # VALUE; this goes further and asks whether the ciphertext is opaque at all — because a payload
    # that was merely encoded rather than encrypted still carries the payload's STRUCTURE, and it
    # would do so even for a field whose value nobody thought to use as a sentinel.
    $opacity = Test-SorchaCiphertextOpacity -Stored $normStored -FieldNames @($normSentinels.Keys)
    $opacityDetail = (@($opacity.Findings | ForEach-Object { "group $($_.Group): $($_.Problem)" })) -join ' | '
    Check 'P3.4' 'the ciphertext is opaque — it is not an encoding of the payload' $opacity.Opaque $opacityDetail

    $disclosed = Get-SorchaDisclosedFieldNames -Stored $normStored
    $expectedNames = @($normSentinels.Keys)
    $missingNames = @($expectedNames | Where-Object { $disclosed -notcontains $_ })
    Check 'P3.5' 'the field NAMES are in the clear, as the disclosure model intends' ($missingNames.Count -eq 0) `
        ("disclosedFields did not carry: $($missingNames -join ', '). Present: $($disclosed -join ', '). " +
         "If the names are absent too, this probe may simply be reading the wrong group — which " +
         "would make P3.3's absence result meaningless.")

    Check 'P3.6' 'the transaction still names its recipients, so it can be routed without decrypting' `
        ($normStored.Recipients.Count -gt 0) `
        "no RecipientsWallets — with no recipient the payload is not addressed to anyone and the fail-open above is likely"

    # P3.3 asserts an ABSENCE, using Find-SorchaSentinel over THIS transaction's bytes. That is
    # worthless unless the same function, over the same bytes, can find something that IS there.
    # P1.5 showed it is not a yes-machine; this shows it is not a no-machine either — and it does so
    # on the encrypted envelope specifically, whose structure differs from the plaintext one that
    # Phase 1 exercised. A field name is the right needle: public by design, and present in the
    # very payload whose values must be absent.
    $needle = @($normSentinels.Keys)[0]
    $selfTest = Find-SorchaSentinel -Stored $normStored -Sentinel $needle
    Check 'P3.7' "the sentinel search still finds what IS present in these bytes (field name '$needle')" `
        $selfTest.Found `
        ("the search found neither the values (P3.3) nor a field name known to be in the clear here. " +
         "It is failing to read this envelope at all, so P3.3's absence result proves nothing.")
}

# =============================================================================================
# PHASE 4 — a subsequent action on the promoted register is encrypted too
# =============================================================================================
Write-Host ""
Write-Host "PHASE 4 — the next action in the same flow" -ForegroundColor Cyan

$reviewSentinel = "REVIEW-$runToken-CONFIDENTIAL"
$a2Ok = $false
$a2TxId = $null

# Gate on the PROJECTION, not just the seal. -WaitForSeal waits for action 1's docket; the
# instance only advances when the InstanceProjector folds it, a beat later. Without this the
# officer's submit races the projector.
try {
    Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId $normInstance -ActionId '2' `
        -RegisterId $state.registerId -Headers $officer.Headers -GatewayUrl $sorchaEnv.GatewayUrl | Out-Null
} catch {
    Write-Host "  (instance did not surface action 2 within the wait: $($_.Exception.Message))" -ForegroundColor DarkYellow
}

try {
    $a2TxId = Submit-Sentinels -InstanceId $normInstance -ActionId '2' -Session $officerCtx -Payload @{
        reviewerNotes = $reviewSentinel
        decision      = 'approved'
    }
    $a2Ok = $true
} catch {
    # Report what the PLATFORM said, not the status line. "400 (Bad Request)" names nothing; the
    # body carries the VAL_* code or the authorisation reason that identifies the cause.
    $body = Get-SorchaErrorBody -ErrorRecord $_
    Check 'P4.1' 'action 2 on the promoted register seals' $false `
        ("$($_.Exception.Message) | body: $body")
}

if ($a2Ok) {
    Check 'P4.1' 'action 2 on the promoted register seals' $true ''
    $a2Stored = Get-SorchaStoredTransaction -Probe $ownerProbe -RegisterId $state.registerId -TxId $a2TxId
    $a2Shape = if ($a2Stored.Exists) { Get-SorchaStoredEnvelopeShape -Payload $a2Stored.Payloads[0] } else { 'missing' }
    Check 'P4.2' 'the action-2 payload is the encrypted envelope shape' ($a2Shape -eq 'encrypted') `
        "got '$a2Shape'"
    $a2Hit = if ($a2Stored.Exists) { Find-SorchaSentinel -Stored $a2Stored -Sentinel $reviewSentinel } else { $null }
    Check 'P4.3' "the reviewer's note is not recoverable from storage" ($a2Stored.Exists -and -not $a2Hit.Found) `
        $(if (-not $a2Stored.Exists) { 'transaction not in storage' } else { "found at: $(($a2Hit.Hits | ForEach-Object { $_.Where }) -join ', ')" })
}

# =============================================================================================
# PHASE 5 — promotion is not retrospective. Stated, not assumed.
# =============================================================================================
Write-Host ""
Write-Host "PHASE 5 — what promotion does NOT do" -ForegroundColor Cyan

$devAfter = Get-SorchaStoredTransaction -Probe $ownerProbe -RegisterId $state.registerId -TxId $devTxId
$devAfterHit = if ($devAfter.Exists) { Find-SorchaSentinel -Stored $devAfter -Sentinel $devSentinels['nationalInsuranceNumber'] } else { $null }
Check 'P5.1' 'payloads sealed BEFORE promotion remain plaintext forever' ($devAfter.Exists -and $devAfterHit.Found) `
    ("the pre-promotion value is no longer readable. That is not a pass: the ledger is immutable, so " +
     "promotion cannot retro-encrypt. If this value has changed, something rewrote sealed history.")
Write-Host "         (this is a PROPERTY of the ledger, not a defect — but it means promoting a" -ForegroundColor DarkGray
Write-Host "          register does not protect what it already holds. Operators must be told.)" -ForegroundColor DarkGray

# =============================================================================================
# PHASE 6 — the replica. Gated on the transaction actually being there.
# =============================================================================================
Write-Host ""
Write-Host "PHASE 6 — replica node" -ForegroundColor Cyan

if (-not $ReplicaSshHost) {
    Skip 'P6.1' 'a replica holding this register cannot read the values either' `
        'no -ReplicaSshHost supplied'
} else {
    $replicaProbe = New-SorchaStorageProbe -Name 'replica' -SshHost $ReplicaSshHost

    if ($ReplicaGatewayUrl) {
        try {
            $replicaEnv = Initialize-SorchaEnvironment -GatewayUrl $ReplicaGatewayUrl -SkipHealthCheck
            $platform = Get-SorchaSecrets -WalkthroughName 'platform'
            $replicaAdmin = Connect-SorchaAdmin -TenantUrl $replicaEnv.TenantUrl `
                -AdminEmail $platform.adminEmail -AdminPassword $platform.adminPassword
            Add-SorchaPublicOrgSubscription -TenantUrl $replicaEnv.TenantUrl `
                -RegisterId $state.registerId -RegisterName $state.registerName `
                -SysAdminHeaders $replicaAdmin.Headers | Out-Null
            Write-Host "  replica subscribed to the register; waiting for it to pull"
        } catch {
            Write-Host "  replica subscribe failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
        }
    }

    $replicaStored = $null
    $deadline = (Get-Date).AddSeconds($ReplicaTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $replicaStored = Get-SorchaStoredTransaction -Probe $replicaProbe -RegisterId $state.registerId -TxId $normTxId
        if ($replicaStored.Exists) { break }
        Start-Sleep -Seconds 5
    }

    if (-not $replicaStored.Exists) {
        # NOT a pass. A node that never received the transaction trivially cannot read it, and
        # scoring that as "the replica cannot decrypt" is the emptiest claim in the design.
        Skip 'P6.1' 'a replica holding this register cannot read the values either' `
            ("the replica never received transaction $normTxId " +
             "(register db present: $($replicaStored.RegisterPresent)). Absence of a value on a node " +
             "that does not hold the transaction proves nothing about encryption.")
    } else {
        Check 'P6.1' 'the replica genuinely holds this transaction (gate for the claim below)' $true ''
        $replicaShape = Get-SorchaStoredEnvelopeShape -Payload $replicaStored.Payloads[0]
        Check 'P6.2' 'the replica stored the encrypted envelope shape' ($replicaShape -eq 'encrypted') "got '$replicaShape'"
        $replicaHits = @()
        foreach ($field in $normSentinels.Keys) {
            $hit = Find-SorchaSentinel -Stored $replicaStored -Sentinel $normSentinels[$field]
            if ($hit.Found) { $replicaHits += $field }
        }
        Check 'P6.3' 'a node that legitimately replicates the data still cannot read the values' ($replicaHits.Count -eq 0) `
            "readable on the replica: $($replicaHits -join ', ')"
    }
}

Write-Summary

$failed = @($script:Results | Where-Object Status -eq 'FAIL').Count
exit $(if ($failed -gt 0) { 1 } else { 0 })
