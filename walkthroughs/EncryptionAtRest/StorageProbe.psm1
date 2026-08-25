# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Reads what a Sorcha node ACTUALLY stored for a transaction, straight out of MongoDB.

.DESCRIPTION
    Every existing "encryption is working" check in this repository asks the API what it is
    willing to show and treats the filtered response as evidence about storage (issue #1580).
    This module does the opposite: it goes behind the API to the bytes on disk.

    Three things it has to get right, each of which silently produces a WRONG answer:

    1. `Payloads[].Data` is a BSON **Binary**, not a string. `print(t.Payloads[0].Data)` renders
       `Binary.createFromBase64(...)` and `.Data.length` is a function, so the naive read produces
       misaligned bytes that fail to decode — and a probe that fails to decode finds no sentinel,
       which is indistinguishable from "the value is encrypted". Extraction therefore goes through
       `EJSON.stringify`, which yields `{"$binary":{"base64":"...","subType":"00"}}`.

    2. Quoting. The script has to survive PowerShell -> ssh -> docker exec -> mongosh. Rather than
       escaping through four layers, the script is base64'd on this side and decoded on the far
       side, so no quote, brace or dollar in it is ever interpreted by an intermediate shell.

    3. An empty result is not a negative result. Every read distinguishes "the transaction is not
       there" from "the transaction is there and the value is absent from it". Conflating them is
       precisely how a probe pointed at the wrong register reports encryption working.
#>

Set-StrictMode -Version Latest

$script:ProbeBegin = '__SORCHA_PROBE_BEGIN__'
$script:ProbeEnd = '__SORCHA_PROBE_END__'

function New-SorchaStorageProbe {
    <#
    .SYNOPSIS
        Describes how to reach one node's MongoDB.
    .PARAMETER Name
        Friendly node name used in output ("n1", "tiny", "local").
    .PARAMETER SshHost
        Omit for a MongoDB reachable through docker on THIS machine. Otherwise the ssh target,
        e.g. "sorcha@51.105.7.135" or "tiny".
    .PARAMETER Container
        The mongod container name. Same on every Sorcha node.
    .PARAMETER MongoUser / MongoPassword / AuthDatabase
        Credentials for mongosh. Defaults match the shipped docker-compose.
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$SshHost,
        [string]$Container = 'sorcha-mongodb',
        [string]$MongoUser = 'sorcha',
        [string]$MongoPassword = 'sorcha_dev_password',
        [string]$AuthDatabase = 'admin'
    )

    [pscustomobject]@{
        Name          = $Name
        SshHost       = $SshHost
        Container     = $Container
        MongoUser     = $MongoUser
        MongoPassword = $MongoPassword
        AuthDatabase  = $AuthDatabase
    }
}

function Invoke-SorchaMongoScript {
    <#
    .SYNOPSIS
        Runs a mongosh script on the probe's node and returns whatever it printed between the
        sentinel markers.
    .DESCRIPTION
        The script is transported base64-encoded (see the module remarks) and piped into mongosh
        on stdin. mongosh echoes a "test> " REPL prompt on each line of piped input, so output is
        delimited by sentinels rather than assumed to be the whole of stdout.
    #>
    param(
        [Parameter(Mandatory)][pscustomobject]$Probe,
        [Parameter(Mandatory)][string]$Script,
        [int]$TimeoutSeconds = 60
    )

    # mongosh reads piped stdin as a REPL, evaluating ONE LINE AT A TIME — so a multi-line
    # statement (or a try/catch wrapping one) is fed to it as a series of syntax errors, and the
    # probe then sees no output and cannot tell that from "the value is not there". Collapse the
    # whole thing to a single line, which the REPL evaluates atomically.
    if ($Script -match '(?m)//') {
        throw "Mongo probe scripts must not contain // comments: they are collapsed to a single line and would comment out the rest of the script."
    }

    $wrapped = "print('$script:ProbeBegin'); try { $Script } catch (e) { print(JSON.stringify({ probeError: String(e) })); } print('$script:ProbeEnd');"
    $wrapped = ($wrapped -replace '\r?\n', ' ') -replace '\s{2,}', ' '

    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($wrapped))
    $mongosh = "docker exec -i $($Probe.Container) mongosh -u $($Probe.MongoUser) -p $($Probe.MongoPassword) --authenticationDatabase $($Probe.AuthDatabase) --quiet"
    $remote = "echo $b64 | base64 -d | $mongosh"

    if ($Probe.SshHost) {
        $raw = & ssh -o ConnectTimeout=15 -o BatchMode=yes $Probe.SshHost $remote 2>&1
    } else {
        $raw = & bash -c $remote 2>&1
    }

    $text = ($raw | Out-String)
    $lines = $text -split "`r?`n"

    $inside = $false
    $captured = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        # mongosh echoes each line of piped input behind its REPL prompt, colours it with ANSI
        # escapes, and interleaves a spinner ("|", "/", "-", "\"). None of that is output; a probe
        # that treats it as output fails to parse and then reports "value absent".
        $clean = $line -replace "$([char]27)\[[0-9;]*[A-Za-z]", ''
        $clean = $clean -replace '^[A-Za-z0-9_\-\[\]]*>\s?', ''
        if ($clean -match [regex]::Escape($script:ProbeEnd)) { $inside = $false; continue }
        if ($clean -match [regex]::Escape($script:ProbeBegin)) { $inside = $true; continue }
        if (-not $inside) { continue }
        if ($clean.Trim()) { $captured.Add($clean) }
    }

    if ($captured.Count -eq 0) {
        throw ("mongosh on '$($Probe.Name)' produced no delimited output. This is a PROBE failure, " +
               "not a negative result — treating it as 'value absent' is exactly the vacuous pass " +
               "this module exists to avoid. Raw output follows:`n$text")
    }

    # Take the JSON document out of whatever the REPL wrapped around it. The scripts in this
    # module print exactly one document, so first '{' to last '}' is unambiguous — and it is
    # immune to the spinner glyphs mongosh interleaves mid-line.
    $joined = ($captured -join '')
    $open = $joined.IndexOf('{')
    $close = $joined.LastIndexOf('}')
    if ($open -lt 0 -or $close -le $open) {
        throw ("mongosh on '$($Probe.Name)' returned no JSON document between the probe markers. " +
               "PROBE failure, not a negative result. Captured:`n$joined")
    }
    $parsed = $joined.Substring($open, $close - $open + 1) | ConvertFrom-Json
    if ($parsed.PSObject.Properties['probeError']) {
        throw "mongosh on '$($Probe.Name)' threw: $($parsed.probeError)"
    }
    return $parsed
}

function Test-SorchaProbeReachable {
    <#
    .SYNOPSIS
        Proves the probe can reach this node's MongoDB before any assertion depends on it.
    #>
    param([Parameter(Mandatory)][pscustomobject]$Probe)

    $r = Invoke-SorchaMongoScript -Probe $Probe -Script @'
print(JSON.stringify({ ok: true, registerDbs: db.adminCommand({listDatabases:1}).databases
    .map(function(d){return d.name;})
    .filter(function(n){return n.indexOf("sorcha_register_") === 0;}).length }));
'@
    return $r
}

function Get-SorchaStoredTransaction {
    <#
    .SYNOPSIS
        Reads one sealed transaction's stored payload bytes from a node's per-register database.
    .DESCRIPTION
        Returns an object that always distinguishes three states, because collapsing them is how a
        misdirected probe reports success:
          RegisterPresent = $false  -> this node holds no database for that register at all
          Exists          = $false  -> the register is here, the transaction is not
          Exists          = $true   -> decoded payloads are in .Payloads
    #>
    param(
        [Parameter(Mandatory)][pscustomobject]$Probe,
        [Parameter(Mandatory)][string]$RegisterId,
        [Parameter(Mandatory)][string]$TxId
    )

    $dbName = "sorcha_register_$RegisterId"
    $js = @"
var target = '$dbName';
var names = db.adminCommand({listDatabases:1}).databases.map(function(d){return d.name;});
if (names.indexOf(target) < 0) {
    print(JSON.stringify({ registerPresent: false, exists: false }));
} else {
    var d = db.getSiblingDB(target);
    var t = d.transactions.findOne({ TxId: '$TxId' });
    if (!t) {
        print(JSON.stringify({ registerPresent: true, exists: false, txCount: d.transactions.countDocuments({}) }));
    } else {
        print(EJSON.stringify({
            registerPresent: true,
            exists: true,
            txId: t.TxId,
            recipients: t.RecipientsWallets || [],
            data: (t.Payloads || []).map(function(p){ return p.Data; })
        }));
    }
}
"@

    $r = Invoke-SorchaMongoScript -Probe $Probe -Script $js

    $payloads = @()
    if ($r.exists) {
        foreach ($d in @($r.data)) {
            # EJSON renders BSON Binary as { "$binary": { "base64": "...", "subType": "00" } }.
            $b64 = $d.'$binary'.base64
            $bytes = [Convert]::FromBase64String($b64)
            $text = [Text.Encoding]::UTF8.GetString($bytes)
            $json = $null
            try { $json = $text | ConvertFrom-Json } catch { $json = $null }
            $payloads += [pscustomobject]@{
                Base64 = $b64
                Bytes  = $bytes
                Text   = $text
                Json   = $json
            }
        }
    }

    [pscustomobject]@{
        Node            = $Probe.Name
        RegisterId      = $RegisterId
        TxId            = $TxId
        RegisterPresent = [bool]$r.registerPresent
        Exists          = [bool]$r.exists
        TxCount         = if ($r.PSObject.Properties['txCount']) { [int]$r.txCount } else { $null }
        Recipients      = if ($r.PSObject.Properties['recipients']) { @($r.recipients) } else { @() }
        Payloads        = $payloads
    }
}

function Get-SorchaStoredEnvelopeShape {
    <#
    .SYNOPSIS
        Classifies a decoded stored payload as the encrypted or the plaintext transaction shape.
    .DESCRIPTION
        The two shapes are produced by different builders and differ STRUCTURALLY, not just in
        content (TransactionBuilderServiceExtensions):
          encrypted : { type, contentEncoding: "encrypted", ..., encryptedPayloads: [ { groupId,
                        disclosedFields, ciphertext, nonce, wrappedKeys } ] }
          plaintext : { type, ..., payloads: { "<wallet>": { ...fields in the clear... } } }
        Both are Base64Url on the ledger, so they look equally opaque in mongosh — which is the
        whole reason this classification has to read the decoded bytes rather than eyeball them.
    #>
    param([Parameter(Mandatory)][pscustomobject]$Payload)

    $json = $Payload.Json
    if (-not $json) { return 'unparseable' }

    $hasEncoding = $json.PSObject.Properties['contentEncoding'] -and $json.contentEncoding -eq 'encrypted'
    $hasGroups = $json.PSObject.Properties['encryptedPayloads']
    $hasPlain = $json.PSObject.Properties['payloads']

    if ($hasEncoding -and $hasGroups) { return 'encrypted' }
    if ($hasGroups) { return 'encrypted-without-marker' }
    if ($hasPlain) { return 'plaintext' }
    return 'other'
}

function Get-SorchaSentinelEncodings {
    <#
    .SYNOPSIS
        Every representation a sentinel could plausibly survive as, if the value was ENCODED
        rather than ENCRYPTED.
    .DESCRIPTION
        "Absent as raw UTF-8" is a weak claim on its own: base64 of the plaintext is also absent as
        raw UTF-8 while carrying the value perfectly. #1580's third point is exactly this — nothing
        has ever distinguished ciphertext from an encoding.
    #>
    param([Parameter(Mandatory)][string]$Sentinel)

    $utf8 = [Text.Encoding]::UTF8.GetBytes($Sentinel)
    $b64 = [Convert]::ToBase64String($utf8)

    [ordered]@{
        'raw'        = $Sentinel
        'base64'     = $b64
        'base64url'  = $b64.TrimEnd('=').Replace('+', '-').Replace('/', '_')
        'hex-lower'  = ([BitConverter]::ToString($utf8) -replace '-', '').ToLowerInvariant()
        'hex-upper'  = ([BitConverter]::ToString($utf8) -replace '-', '')
        'utf16le-b64' = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Sentinel))
    }
}

function Find-SorchaSentinel {
    <#
    .SYNOPSIS
        Searches everything a node stored for a transaction for a sentinel value, in every
        encoding it could have survived as.
    .DESCRIPTION
        Searches, per payload:
          * the decoded envelope text (catches the plaintext shape)
          * the raw Base64Url of Payloads[].Data (catches a double-encoded plaintext)
          * every encryptedPayloads[].ciphertext, base64-decoded (catches "encrypted" that is
            really just encoded)
        Returns every hit with WHERE it was found, so a positive result can be read rather than
        merely counted.
    #>
    param(
        [Parameter(Mandatory)][pscustomobject]$Stored,
        [Parameter(Mandatory)][string]$Sentinel
    )

    $hits = New-Object System.Collections.Generic.List[object]
    $encodings = Get-SorchaSentinelEncodings -Sentinel $Sentinel

    for ($i = 0; $i -lt $Stored.Payloads.Count; $i++) {
        $p = $Stored.Payloads[$i]

        $haystacks = [ordered]@{
            "payload[$i].decoded-envelope" = $p.Text
            "payload[$i].stored-base64"    = $p.Base64
        }

        # Reach into the ciphertext itself. This is the assertion that separates encrypted from
        # encoded: an AEAD ciphertext cannot contain its own plaintext in any encoding.
        if ($p.Json -and $p.Json.PSObject.Properties['encryptedPayloads']) {
            $g = 0
            foreach ($group in @($p.Json.encryptedPayloads)) {
                foreach ($field in 'ciphertext', 'nonce') {
                    if ($group.PSObject.Properties[$field] -and $group.$field) {
                        try {
                            $raw = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($group.$field))
                            $haystacks["payload[$i].group[$g].$field-decoded"] = $raw
                        } catch {
                            # A ciphertext that is not valid base64 is itself worth knowing about,
                            # but it is not a sentinel hit.
                        }
                        $haystacks["payload[$i].group[$g].$field-base64"] = $group.$field
                    }
                }
                $g++
            }
        }

        foreach ($where in $haystacks.Keys) {
            $hay = $haystacks[$where]
            if (-not $hay) { continue }
            foreach ($encName in $encodings.Keys) {
                $needle = $encodings[$encName]
                if ($hay.Contains($needle)) {
                    $hits.Add([pscustomobject]@{ Where = $where; Encoding = $encName })
                }
            }
        }
    }

    [pscustomobject]@{
        Sentinel = $Sentinel
        Found    = $hits.Count -gt 0
        Hits     = $hits.ToArray()
    }
}

function Get-SorchaDisclosedFieldNames {
    <#
    .SYNOPSIS
        The field NAMES an encrypted transaction leaves in the clear.
    .DESCRIPTION
        A disclosure group publishes which fields it covers (`disclosedFields`) so a node can route
        without decrypting. Names in the clear is the design; VALUES in the clear is the defect.
        Returning the names lets a caller assert both halves rather than only the one that is easy.
    #>
    param([Parameter(Mandatory)][pscustomobject]$Stored)

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($p in $Stored.Payloads) {
        if ($p.Json -and $p.Json.PSObject.Properties['encryptedPayloads']) {
            foreach ($group in @($p.Json.encryptedPayloads)) {
                if ($group.PSObject.Properties['disclosedFields']) {
                    foreach ($f in @($group.disclosedFields)) { $names.Add([string]$f) }
                }
            }
        }
    }
    return $names.ToArray()
}

function Test-SorchaCiphertextOpacity {
    <#
    .SYNOPSIS
        Inspects the ciphertext bytes themselves and reports whether they look like ciphertext or
        like an encoding of a JSON payload.
    .DESCRIPTION
        "The sentinel value is absent" is satisfied by ciphertext AND by, say, a compressed or
        re-keyed plaintext. This looks at the decoded ciphertext directly and asks two questions a
        real AEAD output must both answer no to:

          * does it parse as JSON?  A Base64 of the plaintext envelope would.
          * does it contain any of the FIELD NAMES?  The names are published in the clear in
            `disclosedFields` by design, so if they also appear inside the ciphertext then the
            ciphertext contains the structure of the payload and is not opaque.

        The second is the sharper test: an encoding preserves field names even when the caller
        never thought to use one of the values as a sentinel.
    #>
    param(
        [Parameter(Mandatory)][pscustomobject]$Stored,
        [Parameter(Mandatory)][string[]]$FieldNames
    )

    $findings = New-Object System.Collections.Generic.List[object]

    foreach ($p in $Stored.Payloads) {
        if (-not ($p.Json -and $p.Json.PSObject.Properties['encryptedPayloads'])) { continue }
        $g = 0
        foreach ($group in @($p.Json.encryptedPayloads)) {
            if (-not ($group.PSObject.Properties['ciphertext'] -and $group.ciphertext)) { $g++; continue }

            $bytes = $null
            try { $bytes = [Convert]::FromBase64String($group.ciphertext) } catch { }
            if ($null -eq $bytes) {
                $findings.Add([pscustomobject]@{ Group = $g; Problem = 'ciphertext is not valid base64' })
                $g++; continue
            }

            $text = [Text.Encoding]::UTF8.GetString($bytes)

            $parsesAsJson = $false
            try { $null = $text | ConvertFrom-Json -ErrorAction Stop; $parsesAsJson = $true } catch { }
            if ($parsesAsJson) {
                $findings.Add([pscustomobject]@{ Group = $g; Problem = 'ciphertext decodes to parseable JSON — it is an ENCODING, not a ciphertext' })
            }

            foreach ($name in $FieldNames) {
                if ($text.Contains($name)) {
                    $findings.Add([pscustomobject]@{ Group = $g; Problem = "ciphertext contains the field NAME '$name' — the payload structure survives inside it" })
                }
            }
            $g++
        }
    }

    [pscustomobject]@{
        Opaque   = $findings.Count -eq 0
        Findings = $findings.ToArray()
    }
}

Export-ModuleMember -Function `
    New-SorchaStorageProbe, `
    Invoke-SorchaMongoScript, `
    Test-SorchaProbeReachable, `
    Get-SorchaStoredTransaction, `
    Get-SorchaStoredEnvelopeShape, `
    Get-SorchaSentinelEncodings, `
    Find-SorchaSentinel, `
    Test-SorchaCiphertextOpacity, `
    Get-SorchaDisclosedFieldNames
