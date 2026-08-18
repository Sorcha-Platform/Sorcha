#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CredentialLifecycle — the standard credential conformance check.
#
# Drives ONE credential through every state the two status-list specifications
# define, and after each transition asks the platform's own gate whether the
# credential is usable. The gate's verdict is the evidence — not the wallet's
# status field, and never the HTTP 202 of a submission.
#
#   P0  issue            a fresh credential, bound to a status list
#   P1  active           -> gate ACCEPTS
#   P2  suspend          -> gate REFUSES
#   P3  reinstate        -> gate ACCEPTS again        <-- reversibility
#   P4  revoke           -> gate REFUSES
#   P5  reinstate again  -> API REFUSES               <-- terminality
#   P6  W3C wire format  bitstring status list, per purpose
#   P7  IETF wire format token status list, entry values
#   P8  independence     a second credential is unaffected by the first's status
#
# WHY EACH HALF MATTERS
#
# P3 is the one a refusal test cannot give you. Suspension and revocation both
# refuse, so a regression that quietly made suspension terminal passes every
# "is it blocked?" assertion ever written. Only getting the SAME credential
# accepted again proves the reversibility is real.
#
# P5 is its mirror. Revocation is terminal in both specifications, so a platform
# that lets a revoked credential be reinstated is not merely lenient, it is
# non-conformant.
#
# P6/P7 check what we PUBLISH, decoded from the credential's own credentialStatus
# entries rather than from any convenience API. A verifier we have never met will
# read those bytes; asserting our own reader agrees with our own writer proves
# nothing, which is exactly how #1492 shipped a `bits: 2` header over a 1-bit
# array that our own checker then misread.
#
# P8 is the index-integrity check. #1491, #1492 and #1502 were all "the right
# operation applied to the wrong entry" — invisible unless a second credential is
# watching.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$GatewayUrl,
    [switch]$ShowJson,
    # Skip the wire-format phases (P6/P7) — useful where the status-list origin is
    # not reachable from the machine running the script.
    [switch]$SkipWireFormat
)

$ErrorActionPreference = 'Stop'

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) `
    "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CredentialLifecycle — credential conformance check"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "state.json not found — run setup.ps1 first."
    exit 1
}
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$initParams = @{ Profile = $Profile }
if ($GatewayUrl) { $initParams.GatewayUrl = $GatewayUrl }
$sorchaEnv = Initialize-SorchaEnvironment @initParams

$CredType = $state.credentialType

# ── result tracking ──────────────────────────────────────────────────────────
$script:Checks = @()
function Check([string]$phase, [string]$name, [bool]$ok, [string]$detail = "") {
    $script:Checks += [pscustomobject]@{ Phase = $phase; Name = $name; Ok = $ok; Detail = $detail }
    if ($ok) { Write-WtSuccess "[$phase] $name" }
    else     { Write-WtFail    "[$phase] $name$(if($detail){" — $detail"})" }
}

# ── helpers ──────────────────────────────────────────────────────────────────

function ConvertFrom-Base64Url([string]$value) {
    $s = $value.Replace('-', '+').Replace('_', '/')
    switch ($s.Length % 4) { 2 { $s += '==' } 3 { $s += '=' } }
    return [Convert]::FromBase64String($s)
}

function Expand-Gzip([byte[]]$bytes) {
    $in  = [System.IO.MemoryStream]::new($bytes)
    $gz  = [System.IO.Compression.GZipStream]::new($in, [System.IO.Compression.CompressionMode]::Decompress)
    $out = [System.IO.MemoryStream]::new()
    $gz.CopyTo($out); $gz.Dispose(); $in.Dispose()
    return $out.ToArray()
}

function Expand-Zlib([byte[]]$bytes) {
    $in  = [System.IO.MemoryStream]::new($bytes)
    $z   = [System.IO.Compression.ZLibStream]::new($in, [System.IO.Compression.CompressionMode]::Decompress)
    $out = [System.IO.MemoryStream]::new()
    $z.CopyTo($out); $z.Dispose(); $in.Dispose()
    return $out.ToArray()
}

# MSB-first within each byte — the ordering BOTH specifications require.
function Get-BitMsbFirst([byte[]]$raw, [int]$index) {
    $byteIndex = [math]::Floor($index / 8)
    if ($byteIndex -ge $raw.Length) { return $null }
    $bitIndex = 7 - ($index % 8)
    return (($raw[$byteIndex] -band (1 -shl $bitIndex)) -ne 0)
}

# Reads a `bits`-wide entry value, MSB-first across and within bytes.
function Get-EntryValue([byte[]]$raw, [int]$index, [int]$bits) {
    $start = $index * $bits
    if ((($start + $bits) / 8) -gt $raw.Length) { return $null }
    $value = 0
    for ($b = $start; $b -lt ($start + $bits); $b++) {
        $set = Get-BitMsbFirst -raw $raw -index $b
        if ($null -eq $set) { return $null }
        $value = ($value -shl 1) -bor $(if ($set) { 1 } else { 0 })
    }
    return $value
}

# The credential's OWN status references, decoded from its SD-JWT payload.
# This is the spec-correct source: it is what an external verifier reads.
# Handles credentialStatus as an object (single purpose) OR an array (one entry
# per purpose since #1491) — a reader that only handles the object form silently
# stops seeing suspension.
function Get-StatusReferences([string]$rawToken) {
    $jwt = $rawToken.TrimEnd('~').Split('~')[0]
    $parts = $jwt.Split('.')
    if ($parts.Length -lt 2) { return @() }
    $payload = [Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[1])) | ConvertFrom-Json
    if (-not $payload.credentialStatus) { return @() }

    $entries = if ($payload.credentialStatus -is [array]) { $payload.credentialStatus }
               else { @($payload.credentialStatus) }

    return @($entries | ForEach-Object {
        [pscustomobject]@{
            Purpose = $_.statusPurpose
            Uri     = $_.statusListCredential
            Index   = [int]$_.statusListIndex
        }
    })
}

function Get-Presentation([string]$credentialId, $session) {
    return Get-SorchaCredentialPresentation `
        -WalletUrl      $sorchaEnv.WalletUrl `
        -WalletAddress  $state.roles.holder.walletAddress `
        -CredentialType $CredType `
        -CredentialId   $credentialId `
        -Token          $session.Token
}

# Submits the credential to the gate. Returns $true when ACCEPTED.
function Invoke-Gate([string]$credentialId, [string]$phase, $session) {
    $inst = Invoke-SorchaApi `
        -Method POST -Uri "$($sorchaEnv.BlueprintUrl)/instances/" `
        -Body @{
            blueprintId = $state.blueprints.gate.id
            registerId  = $state.registerId
            tenantId    = $state.roles.holder.organizationId
        } `
        -Headers $session.Headers -ShowJson:$ShowJson

    $pres = Get-Presentation -credentialId $credentialId -session $session
    if ($pres.credentialId -ne $credentialId) {
        throw ("Presentation is for $($pres.credentialId) but the phase is testing $credentialId. " +
               "Selecting by type alone presents whichever credential is first in the wallet.")
    }

    try {
        Invoke-SorchaAction `
            -BlueprintUrl $sorchaEnv.BlueprintUrl `
            -InstanceId   $inst.id `
            -ActionId     "0" `
            -BlueprintId  $state.blueprints.gate.id `
            -SenderWallet $state.roles.holder.walletAddress `
            -RegisterId   $state.registerId `
            -Token        $session.Token `
            -PayloadData  @{ purpose = "conformance"; phase = $phase } `
            -CredentialPresentations @($pres) `
            -WaitForSeal | Out-Null
        return $true
    } catch {
        Write-WtInfo "    gate refused ($phase): $($_.Exception.Message)"
        return $false
    }
}

# Issues a fresh credential and returns its wallet record. Requires it to be NEW:
# "a credential of this type exists" is vacuous in a wallet that accumulates them.
function New-ConformanceCredential([string]$reference, $authoritySession, $holderSession) {
    $before = @()
    try {
        $existing = Invoke-SorchaApi -Method GET `
            -Uri "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.holder.walletAddress)/credentials/?status=All" `
            -Headers $holderSession.Headers
        $before = @(Resolve-SorchaCollection -Response $existing -PropertyName 'credentials' |
                    Where-Object { $_.type -eq $CredType } | ForEach-Object { $_.id })
    } catch { }

    $inst = Invoke-SorchaApi `
        -Method POST -Uri "$($sorchaEnv.BlueprintUrl)/instances/" `
        -Body @{
            blueprintId = $state.blueprints.issuance.id
            registerId  = $state.registerId
            tenantId    = $state.roles.holder.organizationId
        } `
        -Headers $holderSession.Headers -ShowJson:$ShowJson

    Invoke-SorchaAction `
        -BlueprintUrl $sorchaEnv.BlueprintUrl -InstanceId $inst.id -ActionId "0" `
        -BlueprintId $state.blueprints.issuance.id `
        -SenderWallet $state.roles.holder.walletAddress -RegisterId $state.registerId `
        -Token $holderSession.Token `
        -PayloadData @{
            subjectName      = "Conformance Subject"
            subjectReference = $reference
            requestedAt      = (Get-Date).ToString("yyyy-MM-dd")
        } `
        -WaitForSeal | Out-Null

    Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId $inst.id -ActionId "1" `
        -RegisterId $state.registerId -Headers $authoritySession.Headers `
        -GatewayUrl $sorchaEnv.GatewayUrl | Out-Null

    Invoke-SorchaAction `
        -BlueprintUrl $sorchaEnv.BlueprintUrl -InstanceId $inst.id -ActionId "1" `
        -BlueprintId $state.blueprints.issuance.id `
        -SenderWallet $state.roles.authority.walletAddress -RegisterId $state.registerId `
        -Token $authoritySession.Token `
        -PayloadData @{
            decision = "issue"
            grade    = "standard"
            issuedOn = (Get-Date).ToString("yyyy-MM-dd")
        } `
        -WaitForSeal | Out-Null

    # Delivery is async: sealed into the action's recipient-addressed disclosure
    # group, then decrypted and persisted by the recipient's detector.
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        try {
            $now = Invoke-SorchaApi -Method GET `
                -Uri "$($sorchaEnv.WalletUrl)/v1/wallets/$($state.roles.holder.walletAddress)/credentials/?status=All" `
                -Headers $holderSession.Headers
            $fresh = @(Resolve-SorchaCollection -Response $now -PropertyName 'credentials' |
                       Where-Object { $_.type -eq $CredType -and $before -notcontains $_.id }) |
                     Select-Object -First 1
            if ($fresh) { return $fresh }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $null
}

# Never throws. A conformance run must survive a platform failure and still report
# every remaining phase — dying on the first 500 tells you one thing when the run
# was about to tell you eight.
function Invoke-Lifecycle([string]$credentialId, [string]$operation, $authoritySession) {
    try {
        $resp = Invoke-SorchaApi `
            -Method POST `
            -Uri "$($sorchaEnv.GatewayUrl)/api/v1/credentials/$credentialId/$operation" `
            -Body @{
                issuerWallet = $state.roles.authority.walletAddress
                reason       = "conformance run — $operation"
            } `
            -Headers $authoritySession.Headers -ShowJson:$ShowJson
        return [pscustomobject]@{ Ok = $true; Response = $resp; Status = $resp.status; Error = $null; HttpStatus = 200 }
    } catch {
        $code = $null
        try { $code = $_.Exception.Response.StatusCode.value__ } catch { }
        return [pscustomobject]@{
            Ok = $false; Response = $null; Status = $null
            Error = $_.Exception.Message; HttpStatus = $code
        }
    }
}

# ═════════════════════════════════════════════════════════════════════════════
# P0 — Issue
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P0: Issue a fresh conformance credential"

$authority = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $state.roles.authority.email -Password $state.roles.authority.password `
    -OrganizationId $state.roles.authority.organizationId
$holder = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $state.roles.holder.email -Password $state.roles.holder.password `
    -OrganizationId $state.roles.holder.organizationId

$runRef = "CLC-" + [Guid]::NewGuid().ToString("N").Substring(0, 8).ToUpper()
Write-WtInfo "Run reference: $runRef"

$cred = New-ConformanceCredential -reference $runRef -authoritySession $authority -holderSession $holder
Check "P0" "a NEW credential was issued and delivered to the holder" ([bool]$cred) `
    "no new credential of type $CredType arrived within 60s"
if (-not $cred) {
    Write-WtFail "Cannot continue — every later phase asserts against this credential."
    exit 1
}
Write-WtInfo "Credential: $($cred.id)  status=$($cred.status)"

$pres0 = Get-Presentation -credentialId $cred.id -session $holder
$refs  = Get-StatusReferences -rawToken $pres0.rawPresentation

Check "P0" "the credential carries at least one status reference" ($refs.Count -ge 1) `
    "no credentialStatus in the SD-JWT — nothing downstream can be enforced or checked"

$revRef  = $refs | Where-Object { $_.Purpose -eq 'revocation' } | Select-Object -First 1
$susRef  = $refs | Where-Object { $_.Purpose -eq 'suspension' } | Select-Object -First 1

Check "P0" "a revocation status entry is declared" ([bool]$revRef)
Check "P0" "a suspension status entry is declared (separate list, per W3C)" ([bool]$susRef) `
    "without it a suspension can only be expressed by setting the revocation bit, which the spec says is irreversible"

if ($revRef -and $susRef) {
    Check "P0" "the two purposes share one entry number" ($revRef.Index -eq $susRef.Index) `
        "revocation index $($revRef.Index) vs suspension index $($susRef.Index) — one credential must have one entry number in every purpose list (#1502)"
    Check "P0" "the two purposes use DIFFERENT lists" ($revRef.Uri -ne $susRef.Uri) `
        "both point at $($revRef.Uri) — one list cannot express a reversible and an irreversible status at the same index (#1491)"
}
foreach ($r in $refs) { Write-WtInfo "  $($r.Purpose): index $($r.Index) in $($r.Uri)" }

# ═════════════════════════════════════════════════════════════════════════════
# P1 — Active
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P1: ACTIVE — the gate must accept"
Check "P1" "an active credential is ACCEPTED" (Invoke-Gate $cred.id "active" $holder) `
    "the baseline failed, so nothing later can be attributed to a status change"

# ═════════════════════════════════════════════════════════════════════════════
# P2 — Suspended
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P2: SUSPENDED — the gate must refuse"

$sus = Invoke-Lifecycle $cred.id "suspend" $authority
Check "P2" "suspend succeeds" $sus.Ok "HTTP $($sus.HttpStatus): $($sus.Error)"
Check "P2" "suspend reports Suspended, not Revoked" ($sus.Status -eq "Suspended") "got '$($sus.Status)'"
Check "P2" "suspend flipped a status-list bit" ($sus.Response.statusListUpdated -eq $true) `
    "statusListUpdated=$($sus.Response.statusListUpdated) — the wallet status changed but no verifier can see it"

Check "P2" "a suspended credential is REFUSED" (-not (Invoke-Gate $cred.id "suspended" $holder)) `
    "suspension is not being enforced (#1495 regressed)"

# ═════════════════════════════════════════════════════════════════════════════
# P3 — Reinstated  (the reversibility proof)
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P3: REINSTATED — the gate must accept again"

$rein = Invoke-Lifecycle $cred.id "reinstate" $authority
Check "P3" "reinstate succeeds" $rein.Ok "HTTP $($rein.HttpStatus): $($rein.Error)"
Check "P3" "reinstate reports Active" ($rein.Status -eq "Active") "got '$($rein.Status)'"

Check "P3" "the SAME credential is ACCEPTED after reinstatement" (Invoke-Gate $cred.id "reinstated" $holder) `
    "suspension behaved as terminal — the single assertion a refusal-only test can never make"

# ═════════════════════════════════════════════════════════════════════════════
# P4 — Revoked
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P4: REVOKED — the gate must refuse"

$rev = Invoke-Lifecycle $cred.id "revoke" $authority
Check "P4" "revoke succeeds" $rev.Ok "HTTP $($rev.HttpStatus): $($rev.Error)"
Check "P4" "revoke reports Revoked" ($rev.Status -eq "Revoked") "got '$($rev.Status)'"
Check "P4" "a revoked credential is REFUSED" (-not (Invoke-Gate $cred.id "revoked" $holder))

# ═════════════════════════════════════════════════════════════════════════════
# P5 — Terminality
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P5: TERMINAL — a revoked credential must not be reinstatable"

$reinAgain = Invoke-Lifecycle $cred.id "reinstate" $authority
Check "P5" "reinstating a REVOKED credential is refused" (-not $reinAgain.Ok) `
    "revocation is 'not reversible' in W3C and 'revoked, annulled, taken back' in IETF — a platform that lifts it is non-conformant"
# A 500 refuses too, but for the wrong reason: it means the platform fell over rather
# than declined. Only a 4xx is a decision.
Check "P5" "the refusal is a 4xx decision, not a 5xx failure" `
    ($reinAgain.HttpStatus -ge 400 -and $reinAgain.HttpStatus -lt 500) `
    "got HTTP $($reinAgain.HttpStatus) — see #1476"

# ═════════════════════════════════════════════════════════════════════════════
# P6/P7 — Wire format
# ═════════════════════════════════════════════════════════════════════════════
if ($SkipWireFormat) {
    Write-WtInfo "P6/P7 skipped (-SkipWireFormat)"
} else {
    Write-WtStep "P6: W3C Bitstring Status List — published wire format"

    foreach ($r in $refs) {
        $expectSet = ($r.Purpose -eq 'revocation')   # revoked at this point; suspension was reinstated
        try {
            $doc = Invoke-SorchaApi -Method GET -Uri $r.Uri
            Check "P6" "$($r.Purpose): list is retrievable" $true
            Check "P6" "$($r.Purpose): declares statusPurpose '$($r.Purpose)'" `
                ($doc.credentialSubject.statusPurpose -eq $r.Purpose) `
                "declares '$($doc.credentialSubject.statusPurpose)' — a verifier MUST raise STATUS_VERIFICATION_ERROR when the purpose it checks is absent"

            $encoded = $doc.credentialSubject.encodedList
            # v1 canonical form is multibase base64url ('u' prefix); the pre-v1 draft was plain base64.
            $compressed = if ($encoded.StartsWith('u')) { ConvertFrom-Base64Url $encoded.Substring(1) }
                          else { [Convert]::FromBase64String($encoded) }
            $raw = Expand-Gzip $compressed
            Check "P6" "$($r.Purpose): encodedList decodes (multibase/base64 + GZip)" ($raw.Length -gt 0)

            $bit = Get-BitMsbFirst -raw $raw -index $r.Index
            Check "P6" "$($r.Purpose): bit at index $($r.Index) is $(if($expectSet){'SET'}else{'CLEAR'})" `
                ($bit -eq $expectSet) `
                "read $bit — either the bit ordering is not MSB-first or the operation addressed a different entry"
        } catch {
            Check "P6" "$($r.Purpose): list is retrievable and decodable" $false $_.Exception.Message
        }
    }

    Write-WtStep "P7: IETF Token Status List — published wire format"

    # Same list ids, served under the IETF route as a signed statuslist+jwt.
    $ietfBase = "$($sorchaEnv.GatewayUrl)/api/v1/credentials/ietf-status-lists"
    $listId   = ($revRef.Uri.TrimEnd('/') -split '/')[-1]
    try {
        # The IETF route is AUTHENTICATED; the W3C list is public. A missing token here
        # returns 401, which looks exactly like "the projection is broken".
        $ietfHeaders = @{ Accept = "application/statuslist+jwt" }
        foreach ($kv in $authority.Headers.GetEnumerator()) { $ietfHeaders[$kv.Key] = $kv.Value }
        # .Content is a BYTE ARRAY in pwsh 7 whenever the content type is not recognised
        # as text — and application/statuslist+jwt is not. Decode explicitly.
        $ietfResp = Invoke-WebRequest -Uri "$ietfBase/$listId" -Headers $ietfHeaders
        $jwt = if ($ietfResp.Content -is [byte[]]) {
            [Text.Encoding]::UTF8.GetString($ietfResp.Content).Trim()
        } else {
            [string]$ietfResp.Content
        }
        $jwt = $jwt.Trim()
        $parts  = $jwt.Split('.')
        Check "P7" "status list token is a compact JWS" ($parts.Length -eq 3)

        $header  = [Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[0])) | ConvertFrom-Json
        $payload = [Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[1])) | ConvertFrom-Json

        Check "P7" "typ is 'statuslist+jwt'" ($header.typ -eq "statuslist+jwt") "got '$($header.typ)'"
        Check "P7" "payload carries sub + iat" (($null -ne $payload.sub) -and ($null -ne $payload.iat))
        Check "P7" "payload carries status_list.bits and .lst" `
            (($null -ne $payload.status_list.bits) -and ($null -ne $payload.status_list.lst))

        $bits = [int]$payload.status_list.bits
        Check "P7" "bits is one of 1, 2, 4, 8" (@(1,2,4,8) -contains $bits) "got $bits"

        $lst = Expand-Zlib (ConvertFrom-Base64Url $payload.status_list.lst)

        # THE #1492 CHECK: `bits` is a claim about byte layout, so the array must be
        # wide enough for the entries it claims to hold. A 2-bit header over a 1-bit
        # array makes a conformant reader take entry N from bits 2N..2N+1 and invent
        # a status for a credential nobody touched.
        $needBytes = [math]::Ceiling((($revRef.Index + 1) * $bits) / 8)
        Check "P7" "lst is wide enough for $bits-bit entries up to index $($revRef.Index)" `
            ($lst.Length -ge $needBytes) `
            "declared $bits bits/entry needs >= $needBytes bytes, got $($lst.Length) — the header disagrees with the bytes (#1492)"

        $value = Get-EntryValue -raw $lst -index $revRef.Index -bits $bits
        # 0x00 VALID / 0x01 INVALID / 0x02 SUSPENDED. The credential is revoked here.
        $readable = if ($null -eq $value) { "nothing (index out of range)" } else { "0x{0:X2}" -f $value }
        Check "P7" "entry $($revRef.Index) reads 0x01 INVALID" ($value -eq 1) `
            "read $readable — the IETF projection disagrees with the revocation the platform enforced"
    } catch {
        Check "P7" "IETF status list is retrievable and decodable" $false $_.Exception.Message
    }
}

# ═════════════════════════════════════════════════════════════════════════════
# P8 — Independence
# ═════════════════════════════════════════════════════════════════════════════
Write-WtStep "P8: INDEPENDENCE — one credential's status must not touch another's"

$ref2  = "CLC-" + [Guid]::NewGuid().ToString("N").Substring(0, 8).ToUpper()
$cred2 = $null
try {
    $cred2 = New-ConformanceCredential -reference $ref2 -authoritySession $authority -holderSession $holder
} catch {
    Write-WtInfo "    second issuance threw: $($_.Exception.Message)"
}
Check "P8" "a second credential was issued" ([bool]$cred2) `
    "without it the independence checks cannot run — one credential can never prove it did not affect another"

if ($cred2) {
    $pres2  = Get-Presentation -credentialId $cred2.id -session $holder
    $refs2  = Get-StatusReferences -rawToken $pres2.rawPresentation
    $rev2   = $refs2 | Where-Object { $_.Purpose -eq 'revocation' } | Select-Object -First 1

    Check "P8" "the second credential has its OWN entry number" `
        ($rev2 -and $revRef -and ($rev2.Index -ne $revRef.Index)) `
        "index $($rev2.Index) collides with the first credential's $($revRef.Index) — revoking one would mark both"

    # The first credential is REVOKED. The second must be unaffected.
    Check "P8" "the second credential is ACCEPTED while the first is revoked" `
        (Invoke-Gate $cred2.id "independence" $holder) `
        "revoking one credential refused another — the operation addressed the wrong entry"

    # And suspension must not leak into the revocation list.
    $sus2 = Invoke-Lifecycle $cred2.id "suspend" $authority
    Check "P8" "suspending the second reports Suspended" ($sus2.Status -eq "Suspended") `
        "HTTP $($sus2.HttpStatus): $($sus2.Error)"

    if (-not $SkipWireFormat -and $rev2) {
        try {
            $revDoc = Invoke-SorchaApi -Method GET -Uri $rev2.Uri
            $enc = $revDoc.credentialSubject.encodedList
            $rawRev = Expand-Gzip $(if ($enc.StartsWith('u')) { ConvertFrom-Base64Url $enc.Substring(1) } else { [Convert]::FromBase64String($enc) })
            $revBit = Get-BitMsbFirst -raw $rawRev -index $rev2.Index
            Check "P8" "suspending did NOT set the REVOCATION bit" ($revBit -eq $false) `
                "the suspension was written to the revocation list — revocation is irreversible, so a reinstate would then clear a bit the spec says can never clear (#1491)"
        } catch {
            Check "P8" "revocation list readable for the second credential" $false $_.Exception.Message
        }
    }

    # Leave the node tidy: this credential is still usable.
    Invoke-Lifecycle $cred2.id "reinstate" $authority | Out-Null
}

# ═════════════════════════════════════════════════════════════════════════════
# Report
# ═════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-WtBanner "CredentialLifecycle — conformance results"

$byPhase = $script:Checks | Group-Object Phase
foreach ($g in $byPhase) {
    $passed = @($g.Group | Where-Object { $_.Ok }).Count
    $total  = $g.Group.Count
    $mark   = if ($passed -eq $total) { "OK  " } else { "FAIL" }
    Write-Host ("  [{0}] {1,-4} {2}/{3}" -f $mark, $g.Name, $passed, $total)
}

$failed = @($script:Checks | Where-Object { -not $_.Ok })
Write-Host ""
if ($failed.Count -eq 0) {
    Write-WtSuccess "ALL $($script:Checks.Count) CONFORMANCE CHECKS PASSED"
    Write-WtInfo "Credential under test: $($cred.id) (left REVOKED — terminal by design)"
    exit 0
}

Write-WtFail "$($failed.Count) of $($script:Checks.Count) checks FAILED:"
foreach ($f in $failed) {
    Write-WtFail "  [$($f.Phase)] $($f.Name)"
    if ($f.Detail) { Write-WtInfo "        $($f.Detail)" }
}
exit 1
