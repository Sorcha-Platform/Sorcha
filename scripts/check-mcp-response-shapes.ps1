#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# MCP tool RESPONSE-SHAPE CI gate.
#
# The sibling route gate (scripts/check-mcp-routes.ps1) proves an MCP tool's URL is mapped by a
# service. Nothing proved the tool reads what that endpoint actually SENDS.
#
# Why this is gated:
#
#   sorcha_tenant_list deserialized the Tenant Service list body into
#   { Items, Page, PageSize, TotalPages }. The endpoint returns
#   OrganizationListResponse { Organizations, TotalCount }. Every field silently defaulted, so on a
#   live node the tool reported "Retrieved 0 tenant(s)" next to "totalCount": 27. The route gate was
#   green throughout — the URL was correct; only the shape was wrong.
#
#   THE SAME TOOL WAS ALSO WRONG ONE LEVEL DOWN. After the envelope was fixed, its element type
#   still asked for `organizationId` (the server serializes `id`) plus UserCount / BlueprintCount /
#   LastActivityAt, none of which exist on OrganizationResponse at all. A gate that checked only the
#   outermost type would have passed that and shipped a tool returning blank tenant IDs.
#
#   THEREFORE THIS GATE WALKS NESTED ELEMENT TYPES, not just the top-level envelope. That is the
#   single most important thing it does. In an earlier phase, nine of the ten tools whose URLs were
#   repointed were ALSO wrong about response shape once the URL was right, so this class of defect
#   is the norm here, not the exception.
#
#   Nothing fails at build time: the tool compiles, System.Text.Json happily binds nothing, and the
#   agent is handed a well-formed, empty, confident answer. This mirrors the Sorcha.Cli.ContractTests
#   precedent (CLAUDE.md pattern 18) for CLI DTOs.
#
# WHAT IT CHECKS  (two questions, because answering only the first shipped #1613)
#
#   1. DOES THE PROPERTY EXIST?  A name the server never sends binds nothing, and the caller gets a
#      well-formed, empty, confident answer.
#
#   2. CAN THE PROPERTY BE READ?  A name can agree perfectly and still be unreadable.
#      RegisterSummaryInfo.Status was named right and typed `string`; GET /api/registers/ sends `1`.
#      System.Text.Json cannot put a Number into a String, so the WHOLE list threw, the client's
#      catch-all returned [], and every consumer reported "0 registers" against a node holding five.
#      A name-only gate is green throughout. See ENUM WIRE FORM below for how the wire form of an
#      enum is derived from source (property attribute, enum attribute, or the owning service's
#      JSON options) rather than assumed — there is no platform-wide convention.
#
# WHAT IS SCANNED
#
#   Tool side    — only classes carrying [McpServerToolType] under
#                  src/Apps/Sorcha.McpServer/Tools/**. A tool deliberately left unregistered
#                  (WalletSignTool, spec 139 T029) is absent from the served surface, so its DTOs
#                  must not fail the gate.
#
#                  Within such a file, every private/internal class/record declared INSIDE the tool
#                  is a candidate response DTO. The ROOTS are those named in a
#                  JsonSerializer.Deserialize<T> / ReadFromJsonAsync<T> / Deserialize<T> call in the
#                  same file — those are the types a service body is actually bound into. Every
#                  other local DTO is reached by WALKING properties from a root, so it is checked
#                  against the corresponding nested server type rather than against the envelope.
#
#   Client side  — DTOs in src/Common/Sorcha.ServiceClients.Http/** that a client method binds a
#                  response into. These are shared by the MCP server, the UI, the CLI and the
#                  services, and were previously scanned by NOTHING — which is how #1613 shipped.
#                  Pairing here is direct rather than heuristic: a client method holds the URL
#                  literal and its ReadFromJsonAsync<T> a few lines apart, so each read is paired
#                  with the nearest preceding api path. A pairing is only acted on when most of the
#                  DTO's properties land on the candidate (see the ratio test) — scoring on raw
#                  overlap alone matched RevokeTransactionResult to the Register model on the single
#                  name 'Status' and reported a correct DTO as broken.
#
#   Server side  — every Map<Verb> route literal under src/Services/**, composed with its MapGroup
#                  prefixes exactly as the route gate composes them, paired with the type(s) that
#                  route produces:
#                    .Produces<T>() on the mapping statement, and
#                    the handler method's typed result — Task<Ok<T>>, Results<Ok<T>, NotFound>, …
#
#                  Type declarations are read from src/Services/** AND src/Common/** (a service DTO
#                  frequently lives in a shared contracts project).
#
# HOW A TOOL DTO IS PAIRED WITH A SERVER TYPE
#
#   A tool may call several routes, and a route may produce several types (a success shape and a
#   problem shape). Rather than guess, every type produced by any route the tool calls — scoped to
#   the owning service the same way the route gate scopes it — is a candidate, and the candidate
#   matching the MOST of the DTO's properties wins. Ties break on name, so the choice is stable.
#
#   This is deliberately lenient in ONE direction only: it can pick a generous partner and thereby
#   MISS a mismatch. It cannot invent one. A gate that cries wolf gets disabled, and the chosen
#   server type is printed with every violation so the pairing is reviewable rather than magic.
#
# WHAT IT CANNOT SEE  (this is a real limitation, not a bug — the same one Sorcha.Cli.ContractTests
# documents in its NotAWireContract list)
#
#   A server shape that is a private record, an anonymous Results.Ok(new { ... }), a raw
#   JsonElement/JsonDocument, or a generic type parameter is not statically reachable by name. Those
#   DTOs are reported once, at DTO level, as "server shape not statically reachable" and belong in
#   the allowlist with that reason. They are NOT silently skipped: an unchecked DTO must be visible.
#
# Usage:
#   pwsh scripts/check-mcp-response-shapes.ps1               # gate
#   pwsh scripts/check-mcp-response-shapes.ps1 -ShowShapes   # also dump every resolved pairing
#
# Exit codes:
#   0 - every discovered tool response DTO property exists on the server type its route produces
#       (or is allowlisted)
#   1 - a DTO property the server never sends, an unresolvable server shape, a stale allowlist
#       entry, or an extraction floor breach

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch]$ShowShapes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.mcp-response-shapes-allowlist'

$toolsRoot = Join-Path $repo 'src/Apps/Sorcha.McpServer/Tools'
$servicesRoot = Join-Path $repo 'src/Services'
$commonRoot = Join-Path $repo 'src/Common'
$clientRoots = @(
    (Join-Path $repo 'src/Common/Sorcha.ServiceClients.Http'),
    (Join-Path $repo 'src/Common/Sorcha.ServiceClients')
)

foreach ($required in @($toolsRoot, $servicesRoot)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Write-Error "Required source tree not found: $required"
        exit 1
    }
}

# Non-vacuity floors. A broken parser must FAIL, never read as a clean gate — this repo has been
# bitten by exactly that (tests that mocked a hash provider to a constant passed for months because
# every comparison was equal by construction). Do not lower these to make a build pass.
$MinDtoCount = 20      # per the task brief
$MinResolvedPairs = 10 # DTOs that actually got paired with a server type
$MinClientResolvedPairs = 5 # service-client DTOs paired with a server type (the #1613 surface)

# ---------------------------------------------------------------------------
# Shared helpers (conventions mirrored from scripts/check-mcp-routes.ps1)
# ---------------------------------------------------------------------------

function Get-SourceFiles {
    param([string[]]$Roots)

    $files = @()
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $files += Get-ChildItem -Path $root -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    }
    return $files | Sort-Object -Property FullName -Unique
}

function Resolve-Holes {
    param([string]$Text)

    $t = $Text
    $t = [regex]::Replace($t, '\{\*\*[^{}]*\}', '**')
    for ($i = 0; $i -lt 8; $i++) {
        $next = [regex]::Replace($t, '\{[^{}]*\}', '*')
        if ($next -eq $t) { break }
        $t = $next
    }
    return $t
}

function ConvertTo-RouteFamily {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }

    $p = Resolve-Holes $Path
    $p = ($p -split '[?#]')[0]
    $p = $p.Trim()

    $segments = @()
    foreach ($seg in ($p -split '/')) {
        if ([string]::IsNullOrWhiteSpace($seg)) { continue }
        $segments += $seg.ToLowerInvariant()
    }
    return ($segments -join '/')
}

function Get-StringLiterals {
    param([string]$Line)

    $out = @()
    foreach ($m in [regex]::Matches($Line, '"(?:[^"\\]|\\.)*"')) {
        $out += $m.Value.Substring(1, $m.Value.Length - 2)
    }
    return $out
}

function Get-ApiPathsInLine {
    param([string]$Line)

    $out = @()
    foreach ($literal in (Get-StringLiterals $Line)) {
        $resolved = Resolve-Holes $literal
        foreach ($m in [regex]::Matches($resolved, '(?<![A-Za-z0-9_.\-])api/[A-Za-z0-9_\-./*]*')) {
            $out += $m.Value
        }
    }
    return $out
}

function Get-ServiceOwnerFromPath {
    param([string]$FullPath)

    $p = $FullPath.Replace('\', '/')
    if ($p -match '/src/Services/Sorcha\.([A-Za-z0-9]+)\.Service(/|$)') { return $Matches[1] }
    if ($p -match '/src/Services/Sorcha\.ApiGateway(/|$)') { return 'ApiGateway' }
    return $null
}

function Remove-LineComments {
    param([string]$Text)
    # Whole-line comments only (/// XML docs included). Line COUNT is preserved so reported line
    # numbers stay honest; character offsets shift, so every consumer must use the cleaned text.
    return ($Text -split "`n" | ForEach-Object {
            if ($_ -match '^\s*//') { '' } else { $_ }
        }) -join "`n"
}

function Get-StatementAfter {
    param([string]$Text, [int]$StartIndex)

    $end = $Text.IndexOf(';', $StartIndex)
    if ($end -lt 0) { $end = [Math]::Min($Text.Length, $StartIndex + 2000) }
    return $Text.Substring($StartIndex, $end - $StartIndex)
}

# The FULL mapping statement, lambda included. Get-StatementAfter stops at the first ';', which for
# an inline handler lands inside the lambda body — so every `return Results.Ok(...)` in this tree
# would be invisible. Walk the balanced Map(...) call first, then on to the terminating ';'.
function Get-MapStatement {
    param([string]$Text, [int]$StartIndex)

    $open = $Text.IndexOf('(', $StartIndex)
    if ($open -lt 0) { return Get-StatementAfter -Text $Text -StartIndex $StartIndex }
    $close = Find-Balanced -Text $Text -Start $open -Open '(' -Close ')'
    if ($close -lt 0) { return Get-StatementAfter -Text $Text -StartIndex $StartIndex }

    $end = $Text.IndexOf(';', $close)
    if ($end -lt 0) { $end = [Math]::Min($Text.Length - 1, $close + 2000) }
    return $Text.Substring($StartIndex, $end - $StartIndex + 1)
}

function Get-StatementBefore {
    param([string]$Text, [int]$EndIndex)

    $start = $Text.LastIndexOfAny(@(';', '{', '}'), [Math]::Max(0, $EndIndex - 1))
    if ($start -lt 0) { $start = 0 } else { $start = $start + 1 }
    return $Text.Substring($start, $EndIndex - $start)
}

function Join-RoutePath {
    param([string]$Prefix, [string]$Fragment)

    $left = $Prefix.TrimEnd('/')
    $right = $Fragment.Trim()
    if ($right.Length -gt 0 -and -not $right.StartsWith('/')) { $right = '/' + $right }
    $joined = $left + $right
    if ($joined.Length -eq 0) { $joined = '/' }
    return $joined
}

function Test-AbsoluteRoute {
    param([string]$Fragment)
    $f = $Fragment.TrimStart('/')
    return ($f -eq 'api' -or $f.StartsWith('api/'))
}

function Get-MethodBodies {
    param([string]$Text, [string]$MethodName)

    $bodies = @()
    foreach ($m in [regex]::Matches($Text, "(?<![A-Za-z0-9_])$([regex]::Escape($MethodName))\s*\(")) {
        $head = $Text.Substring([Math]::Max(0, $m.Index - 200), [Math]::Min(200, $m.Index))
        if ($head -notmatch '(public|private|internal|protected)[^;{}]*$') { continue }

        $i = $m.Index + $m.Length - 1
        $depth = 0
        while ($i -lt $Text.Length) {
            if ($Text[$i] -eq '(') { $depth++ }
            elseif ($Text[$i] -eq ')') { $depth--; if ($depth -eq 0) { break } }
            $i++
        }
        if ($i -ge $Text.Length) { continue }
        $j = $i + 1

        while ($j -lt $Text.Length -and [char]::IsWhiteSpace($Text[$j])) { $j++ }
        if ($j -ge $Text.Length) { continue }

        if ($Text[$j] -eq '{') {
            $depth = 0
            $k = $j
            while ($k -lt $Text.Length) {
                if ($Text[$k] -eq '{') { $depth++ }
                elseif ($Text[$k] -eq '}') { $depth--; if ($depth -eq 0) { break } }
                $k++
            }
            $bodies += [pscustomobject]@{ Start = $j; Text = $Text.Substring($j, [Math]::Min($k - $j + 1, $Text.Length - $j)) }
        }
        elseif ($Text.Substring($j, [Math]::Min(2, $Text.Length - $j)) -eq '=>') {
            $end = $Text.IndexOf(';', $j)
            if ($end -lt 0) { $end = $Text.Length - 1 }
            $bodies += [pscustomobject]@{ Start = $j; Text = $Text.Substring($j, $end - $j + 1) }
        }
    }
    return $bodies
}

function Get-LineNumber {
    param([string]$Text, [int]$Index)
    if ($Index -le 0) { return 1 }
    return ($Text.Substring(0, [Math]::Min($Index, $Text.Length)).Split("`n").Length)
}

# ---------------------------------------------------------------------------
# A small C# type reader
# ---------------------------------------------------------------------------
#
# Roslyn is not available to a shell gate, so this reads declarations textually. It is deliberately
# conservative: anything it cannot parse becomes an UNRESOLVED report (visible, allowlistable), never
# a silent pass.

function Find-Balanced {
    param([string]$Text, [int]$Start, [char]$Open, [char]$Close)

    $depth = 0
    for ($i = $Start; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        if ($c -eq $Open) { $depth++ }
        elseif ($c -eq $Close) { $depth--; if ($depth -eq 0) { return $i } }
    }
    return -1
}

function Split-TopLevel {
    param([string]$Text)

    $parts = @()
    $depth = 0
    $current = ''
    foreach ($c in $Text.ToCharArray()) {
        if ($c -eq '<' -or $c -eq '(' -or $c -eq '[' -or $c -eq '{') { $depth++ }
        elseif ($c -eq '>' -or $c -eq ')' -or $c -eq ']' -or $c -eq '}') { $depth-- }

        if ($c -eq ',' -and $depth -eq 0) {
            $parts += $current
            $current = ''
        }
        else { $current += $c }
    }
    if ($current.Trim().Length -gt 0) { $parts += $current }
    return , $parts
}

# The wire name of a property: [JsonPropertyName("x")] wins, otherwise the C# name (compared
# case-insensitively downstream, matching PropertyNameCaseInsensitive = true on the tool side and
# the camelCase policy on the server side).
function Get-WireName {
    param([string]$Attrs, [string]$Name)

    $m = [regex]::Match($Attrs, 'JsonPropertyName\s*\(\s*"([^"]+)"\s*\)')
    if ($m.Success) { return $m.Groups[1].Value }
    return $Name
}

# [JsonIgnore] with no argument means Always. [JsonIgnore(Condition = ...WhenWritingNull)] does NOT
# remove the property from the wire — it omits it only when null — so it must still be treated as
# part of the server's shape.
function Test-UnconditionalJsonIgnore {
    param([string]$Attrs)

    if ($Attrs -match '\[\s*JsonIgnore\s*\]') { return $true }
    if ($Attrs -match 'JsonIgnoreCondition\s*\.\s*Always') { return $true }
    return $false
}

# Matches both `T Name { get; … }` and the expression-bodied `T Name => expr;`. The latter matters:
# System.Text.Json DOES serialize a computed get-only property (ActionSubmissionResponse.TransactionHash
# is one), so omitting it would shrink the server's wire shape and flag a tool DTO that reads it as a
# violation that isn't one. Unlike the parser's other limits this class of miss is not silence — it is
# a loud wrong answer, and a gate that cries wolf gets switched off.
# A method is excluded by construction: `Name(` never matches `Name\s*=>`.
$propertyPattern = '(?<attrs>(?:\[[^\[\]]*\]\s*)*)\b(?<vis>public|internal)\s+(?:(?:required|virtual|override|new|abstract|static|readonly)\s+)*(?<type>[A-Za-z_][A-Za-z0-9_\.]*(?:<(?:[^<>]|<(?:[^<>]|<[^<>]*>)*>)*>)?\??(?:\[\])?\??)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{\s*(?:get|set|init)\b|=>)'

function Get-PropertiesFromBody {
    param([string]$Body)

    $props = @()
    foreach ($m in [regex]::Matches($Body, $propertyPattern)) {
        $attrs = $m.Groups['attrs'].Value
        # A property the server never serializes is not part of the wire shape. ONLY an
        # unconditional ignore counts: [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        # still sends the property whenever it has a value, and treating it as absent produced two
        # false violations (Action.Target, Participant.WalletAddress) on the first run of this gate.
        if (Test-UnconditionalJsonIgnore $attrs) { continue }
        $props += [pscustomobject]@{
            Name     = $m.Groups['name'].Value
            WireName = (Get-WireName -Attrs $attrs -Name $m.Groups['name'].Value)
            Type     = $m.Groups['type'].Value
            Attrs    = $attrs
        }
    }
    return $props
}

function Get-PropertiesFromPositional {
    param([string]$Params)

    $props = @()
    foreach ($raw in (Split-TopLevel $Params)) {
        $p = $raw.Trim()
        if ($p.Length -eq 0) { continue }

        $attrs = ''
        while ($p.StartsWith('[')) {
            $close = Find-Balanced -Text $p -Start 0 -Open '[' -Close ']'
            if ($close -lt 0) { break }
            $attrs += $p.Substring(0, $close + 1)
            $p = $p.Substring($close + 1).Trim()
        }
        if (Test-UnconditionalJsonIgnore $attrs) { continue }

        # Drop any default value, then take the trailing identifier as the parameter name.
        $p = ($p -split '=')[0].Trim()
        $m = [regex]::Match($p, '^(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$')
        if (-not $m.Success) { continue }

        $props += [pscustomobject]@{
            Name     = $m.Groups['name'].Value
            WireName = (Get-WireName -Attrs $attrs -Name $m.Groups['name'].Value)
            Type     = $m.Groups['type'].Value.Trim()
            Attrs    = $attrs
        }
    }
    return $props
}

$typeDeclPattern = '(?m)^[ \t]*(?<mods>(?:(?:public|private|protected|internal|file|sealed|abstract|static|partial|new|readonly)[ \t]+)*)(?<kind>record[ \t]+class|record[ \t]+struct|record|class|struct)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)'

function Get-TypeDeclarations {
    param([string]$Text, [string]$File)

    $decls = @()
    foreach ($m in [regex]::Matches($Text, $typeDeclPattern)) {
        $name = $m.Groups['name'].Value
        $mods = $m.Groups['mods'].Value
        $i = $m.Index + $m.Length

        $positional = ''
        $bases = ''
        $body = ''

        # generic parameter list — kept, not skipped: PagedResult<T>'s Items is typed T, and
        # without substituting T the element type of a paged endpoint is unreachable.
        $typeParams = @()
        while ($i -lt $Text.Length -and [char]::IsWhiteSpace($Text[$i])) { $i++ }
        if ($i -lt $Text.Length -and $Text[$i] -eq '<') {
            $close = Find-Balanced -Text $Text -Start $i -Open '<' -Close '>'
            if ($close -lt 0) { continue }
            foreach ($tp in (Split-TopLevel $Text.Substring($i + 1, $close - $i - 1))) {
                $tpm = [regex]::Match($tp.Trim(), '([A-Za-z_][A-Za-z0-9_]*)\s*$')
                if ($tpm.Success) { $typeParams += $tpm.Groups[1].Value }
            }
            $i = $close + 1
        }

        # positional record parameters
        while ($i -lt $Text.Length -and [char]::IsWhiteSpace($Text[$i])) { $i++ }
        if ($i -lt $Text.Length -and $Text[$i] -eq '(') {
            $close = Find-Balanced -Text $Text -Start $i -Open '(' -Close ')'
            if ($close -lt 0) { continue }
            $positional = $Text.Substring($i + 1, $close - $i - 1)
            $i = $close + 1
        }

        # base list
        while ($i -lt $Text.Length -and [char]::IsWhiteSpace($Text[$i])) { $i++ }
        if ($i -lt $Text.Length -and $Text[$i] -eq ':') {
            $j = $i + 1
            $depth = 0
            while ($j -lt $Text.Length) {
                $c = $Text[$j]
                if ($c -eq '<' -or $c -eq '(') { $depth++ }
                elseif ($c -eq '>' -or $c -eq ')') { $depth-- }
                elseif ($depth -le 0 -and ($c -eq '{' -or $c -eq ';')) { break }
                $j++
            }
            $bases = $Text.Substring($i + 1, $j - $i - 1)
            $i = $j
        }

        # body
        while ($i -lt $Text.Length -and [char]::IsWhiteSpace($Text[$i])) { $i++ }
        if ($i -lt $Text.Length -and $Text[$i] -eq '{') {
            $close = Find-Balanced -Text $Text -Start $i -Open '{' -Close '}'
            if ($close -ge 0) { $body = $Text.Substring($i + 1, $close - $i - 1) }
        }

        $props = @()
        if ($positional.Length -gt 0) { $props += @(Get-PropertiesFromPositional $positional) }
        if ($body.Length -gt 0) { $props += @(Get-PropertiesFromBody $body) }

        $baseNames = @()
        foreach ($b in (Split-TopLevel $bases)) {
            $bm = [regex]::Match($b.Trim(), '^([A-Za-z_][A-Za-z0-9_\.]*)')
            if ($bm.Success) { $baseNames += ($bm.Groups[1].Value -split '\.')[-1] }
        }

        $decls += [pscustomobject]@{
            Name       = $name
            Kind       = $m.Groups['kind'].Value
            IsInternal = ($mods -match '\b(private|internal)\b')
            Bases      = $baseNames
            TypeParams = $typeParams
            Props      = $props
            File       = $File
            Index      = $m.Index
        }
    }
    return $decls
}

# Collection wrappers carry no shape of their own — the shape is the element. Anything else
# generic (PagedResult<BlueprintSummary>) DOES carry shape and must be kept intact, or the element
# type of every paged endpoint becomes unreachable and the gate degenerates into an allowlist.
$collectionTypes = @(
    'List', 'IList', 'IReadOnlyList', 'ICollection', 'IReadOnlyCollection', 'IEnumerable',
    'IAsyncEnumerable', 'Collection', 'HashSet', 'ISet', 'Array', 'Nullable', 'Task', 'ValueTask',
    'ActionResult', 'Dictionary', 'IDictionary', 'IReadOnlyDictionary', 'SortedDictionary'
)

function Split-TypeRef {
    param([string]$TypeRef)

    $t = $TypeRef.Trim().TrimEnd('?').Trim()
    $open = $t.IndexOf('<')
    if ($open -lt 0) { return [pscustomobject]@{ Name = (($t -split '\.')[-1]); Args = @() } }

    $close = Find-Balanced -Text $t -Start $open -Open '<' -Close '>'
    if ($close -lt 0) { return [pscustomobject]@{ Name = (($t.Substring(0, $open) -split '\.')[-1]); Args = @() } }

    $name = ($t.Substring(0, $open) -split '\.')[-1]
    $argList = @()
    foreach ($a in (Split-TopLevel $t.Substring($open + 1, $close - $open - 1))) {
        if ($a.Trim().Length -gt 0) { $argList += $a.Trim() }
    }
    return [pscustomobject]@{ Name = $name; Args = $argList }
}

# Peel a declared type down to the type REFERENCE that carries the shape, generics preserved:
#   List<TenantDto>                       -> TenantDto
#   IReadOnlyList<Foo>?                   -> Foo
#   Dictionary<string, Bar>               -> Bar                              (value type is shaped)
#   Baz[]                                 -> Baz
#   Task<PagedResult<BlueprintSummary>>   -> PagedResult<BlueprintSummary>    (kept, not peeled)
function Resolve-ShapeTypeRef {
    param([string]$TypeText)

    $t = $TypeText.Trim()
    for ($guard = 0; $guard -lt 8; $guard++) {
        $t = $t.Trim().TrimEnd('?').Trim()
        if ($t.EndsWith('[]')) { $t = $t.Substring(0, $t.Length - 2).Trim(); continue }

        $parsed = Split-TypeRef $t
        if ($parsed.Args.Count -eq 0) { break }
        if ($collectionTypes -notcontains $parsed.Name) { break }
        $t = $parsed.Args[$parsed.Args.Count - 1]
    }

    # Drop the namespace on the outer name so Sorcha.Blueprint.Service.Models.Instance and Instance
    # are one key, not two.
    $t = $t.Trim().TrimEnd('?').Trim()
    $open = $t.IndexOf('<')
    if ($open -lt 0) { return ($t -split '\.')[-1] }
    return (($t.Substring(0, $open) -split '\.')[-1]) + $t.Substring($open)
}

# Types whose shape is opaque to a textual reader. A tool DTO bound to one of these is reported as
# unresolvable rather than silently accepted.
$opaqueTypes = @(
    'object', 'dynamic', 'JsonElement', 'JsonNode', 'JsonDocument', 'JsonObject', 'JsonArray',
    'string', 'int', 'long', 'bool', 'decimal', 'double', 'float', 'Guid', 'DateTime',
    'DateTimeOffset', 'TimeSpan', 'byte', 'Uri', 'IResult', 'Stream'
)

# ---------------------------------------------------------------------------
# SERVER SIDE: type registry
# ---------------------------------------------------------------------------

$serverTypeFiles = Get-SourceFiles -Roots @($servicesRoot, $commonRoot)
$serverTypes = @{}   # simple name -> [declaration]
$aliasScan = @{}     # file -> cleaned text (reused by the alias and method-return passes)

foreach ($f in $serverTypeFiles) {
    $text = Remove-LineComments (Get-Content -LiteralPath $f.FullName -Raw)
    $aliasScan[$f.FullName] = $text
    foreach ($d in @(Get-TypeDeclarations -Text $text -File $f.FullName)) {
        if (-not $serverTypes.ContainsKey($d.Name)) { $serverTypes[$d.Name] = @() }
        $serverTypes[$d.Name] += $d
    }
}

# `using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;` is used throughout this tree, so a
# produced type is frequently an ALIAS that no declaration anywhere is named after. Without this map
# every blueprint endpoint would look unresolvable and land in the allowlist.
$typeAliases = @{}
foreach ($f in $serverTypeFiles) {
    foreach ($m in [regex]::Matches($aliasScan[$f.FullName], '(?m)^\s*(?:global\s+)?using\s+(?<a>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<t>[A-Za-z_][A-Za-z0-9_\.]*)\s*;')) {
        $a = $m.Groups['a'].Value
        $t = ($m.Groups['t'].Value -split '\.')[-1]
        if ($a -eq $t) { continue }
        if (-not $typeAliases.ContainsKey($a)) { $typeAliases[$a] = $t }
    }
}

function Resolve-TypeAlias {
    param([string]$Name)

    $n = $Name
    for ($i = 0; $i -lt 4; $i++) {
        if ($serverTypes.ContainsKey($n)) { return $n }
        if (-not $typeAliases.ContainsKey($n)) { return $n }
        $n = $typeAliases[$n]
    }
    return $n
}

# Effective wire properties of a server type name: its own, plus every base type's, unioned across
# every declaration of that name.
#
# A name declared in more than one project is UNIONED rather than disambiguated. That is lenient by
# design — CLAUDE.md pattern 18 records that several apparent CLI "mismatches" were in fact name
# collisions where the client was already correct, and a gate that reports those gets switched off.
$effectivePropsCache = @{}

function Get-ServerWireProperties {
    param([string]$TypeRef, [System.Collections.Generic.HashSet[string]]$Visited = $null)

    if ($null -eq $Visited) { $Visited = [System.Collections.Generic.HashSet[string]]::new() }
    if (-not $Visited.Add($TypeRef)) { return @() }
    if ($effectivePropsCache.ContainsKey($TypeRef)) { return $effectivePropsCache[$TypeRef] }

    $parsed = Split-TypeRef $TypeRef
    $declName = Resolve-TypeAlias $parsed.Name
    if (-not $serverTypes.ContainsKey($declName)) { return @() }

    $props = @()
    foreach ($d in $serverTypes[$declName]) {
        # Substitute the declaration's type parameters with this reference's arguments, so
        # PagedResult<BlueprintSummary>.Items resolves to IReadOnlyList<BlueprintSummary>.
        $subs = @{}
        for ($k = 0; $k -lt $d.TypeParams.Count -and $k -lt $parsed.Args.Count; $k++) {
            $subs[$d.TypeParams[$k]] = $parsed.Args[$k]
        }

        foreach ($p in $d.Props) {
            $ptype = $p.Type
            foreach ($tp in $subs.Keys) {
                $ptype = [regex]::Replace($ptype, "(?<![A-Za-z0-9_])$([regex]::Escape($tp))(?![A-Za-z0-9_])", $subs[$tp])
            }
            $props += [pscustomobject]@{ Name = $p.Name; WireName = $p.WireName; Type = $ptype; Attrs = $p.Attrs }
        }

        foreach ($b in $d.Bases) {
            if ($b -eq $declName) { continue }
            $props += @(Get-ServerWireProperties -TypeRef $b -Visited $Visited)
        }
    }

    $seen = @{}
    $unique = @()
    foreach ($p in $props) {
        $key = $p.WireName.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $unique += $p
    }

    $effectivePropsCache[$TypeRef] = $unique
    return $unique
}

function Test-ServerTypeKnown {
    param([string]$TypeRef)

    if ([string]::IsNullOrWhiteSpace($TypeRef)) { return $false }
    $parsed = Split-TypeRef $TypeRef
    if ($opaqueTypes -contains $parsed.Name) { return $false }
    if (-not $serverTypes.ContainsKey((Resolve-TypeAlias $parsed.Name))) { return $false }
    return (@(Get-ServerWireProperties -TypeRef $TypeRef).Count -gt 0)
}

# ---------------------------------------------------------------------------
# ENUM WIRE FORM — the #1613 class
#
# A property NAME check cannot see this one. RegisterSummaryInfo.Status was named exactly right and
# typed `string`; GET /api/registers/ sends `1`. System.Text.Json cannot put a Number into a String,
# so the WHOLE list threw, RegisterServiceClient's catch-all returned [], and both consumers reported
# "0 registers" against a node holding five. Nothing failed, nothing logged, and the name gate was
# green — the property existed on the server type, it just could never be read.
#
# Whether an enum reaches the wire as a number or a name is decided in three places, and this block
# derives all three FROM SOURCE rather than assuming a platform-wide convention (there isn't one):
#
#   1. the property's own [JsonConverter(...)]          — wins over everything
#   2. the enum type's [JsonConverter(...)]             — e.g. RegisterPurpose has one
#   3. the owning service calling SorchaJson.Configure  — ONLY Tenant and Wallet do
#
# AddServiceDefaults configures no JSON at all, so Register / Blueprint / Validator / Peer / HAIP
# serialise under the ASP.NET web defaults. The result is that ONE object can carry both forms:
# a Register sends "purpose":"System" (attribute) next to "status":1 and "syncState":2 (no attribute).
# That inconsistency is the generator of this defect class, which is why the check is per-property.
# ---------------------------------------------------------------------------

$enumIsStringSerialised = @{}
$enumNames = [System.Collections.Generic.HashSet[string]]::new()

foreach ($f in (Get-SourceFiles -Roots @($commonRoot, $servicesRoot))) {
    # Comment-stripped so an attribute separated from its enum by an XML doc block is still adjacent.
    $clean = Remove-LineComments (Get-Content -LiteralPath $f.FullName -Raw)
    foreach ($m in [regex]::Matches($clean, '(?<attrs>(?:\[[^\]\r\n]*\]\s*)*)(?:public|internal)\s+enum\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)')) {
        $name = $m.Groups['name'].Value
        [void]$enumNames.Add($name)
        if ($m.Groups['attrs'].Value -match 'JsonConverter') { $enumIsStringSerialised[$name] = $true }
    }
}

# Which services apply the shared kebab-case-string wire format to their own responses.
$serviceAppliesSorchaJson = @{}
foreach ($f in (Get-SourceFiles -Roots @($servicesRoot))) {
    if ($f.Name -ne 'Program.cs') { continue }
    if ((Get-Content -LiteralPath $f.FullName -Raw) -notmatch 'SorchaJson\.Configure') { continue }
    $owner = Get-ServiceOwnerFromPath $f.FullName
    if ($owner) { $serviceAppliesSorchaJson[$owner] = $true }
}

# True only when the server writes this property as a bare INTEGER, so a client `string` cannot
# read it. Every uncertainty resolves to $false: this gate misses rather than cries wolf.
function Test-WireEnumIsNumeric {
    param([string]$ServerPropType, [string]$ServerPropAttrs, [string[]]$Owners)

    $bare = (Split-TypeRef (Resolve-ShapeTypeRef $ServerPropType)).Name
    if (-not $enumNames.Contains($bare)) { return $false }
    if ($ServerPropAttrs -and $ServerPropAttrs -match 'JsonConverter') { return $false }
    if ($enumIsStringSerialised.ContainsKey($bare)) { return $false }

    # Unknown owner, or ANY candidate owner that stringifies, withholds the verdict.
    if (-not $Owners -or @($Owners).Count -eq 0) { return $false }
    foreach ($o in $Owners) {
        if (-not $o) { return $false }
        if ($serviceAppliesSorchaJson.ContainsKey($o)) { return $false }
    }
    return $true
}

# A client property that can never hold what the server writes there.
function Test-ClientTypeCannotBind {
    param([string]$ClientPropType, [string]$ServerPropType, [string]$ServerPropAttrs, [string[]]$Owners)

    $c = $ClientPropType.Trim().TrimEnd('?')
    if ($c -ne 'string' -and $c -ne 'String') { return $false }
    return (Test-WireEnumIsNumeric -ServerPropType $ServerPropType -ServerPropAttrs $ServerPropAttrs -Owners $Owners)
}

# ---------------------------------------------------------------------------
# SERVER SIDE: route family -> produced type names   (composition copied from check-mcp-routes.ps1)
# ---------------------------------------------------------------------------

$verbPattern = '\.\s*Map(?:Get|Post|Put|Delete|Patch|Methods)\s*\(\s*[$@]{0,2}"((?:[^"\\]|\\.)*)"'
$groupPattern = '\.\s*MapGroup\s*\(\s*[$@]{0,2}"((?:[^"\\]|\\.)*)"\s*\)'
$extCallPattern = '\.\s*(Map[A-Za-z]*Endpoints?)\s*\('
$extDefPattern = '\b(Map[A-Za-z]*Endpoints?)\s*\(\s*this\s+'

$serviceFiles = Get-SourceFiles -Roots @($servicesRoot)
$serviceText = @{}
foreach ($f in $serviceFiles) {
    $serviceText[$f.FullName] = Remove-LineComments (Get-Content -LiteralPath $f.FullName -Raw)
}

$groupVars = @{}

function Add-GroupVarPath {
    param([string]$File, [string]$VarName, [string[]]$Paths)

    if (-not $groupVars.ContainsKey($File)) { $groupVars[$File] = @{} }
    if (-not $groupVars[$File].ContainsKey($VarName)) { $groupVars[$File][$VarName] = @() }
    foreach ($p in $Paths) {
        if ($groupVars[$File][$VarName] -notcontains $p) { $groupVars[$File][$VarName] += $p }
    }
}

foreach ($file in $serviceFiles) {
    $text = $serviceText[$file.FullName]
    foreach ($m in [regex]::Matches($text, $groupPattern)) {
        $path = $m.Groups[1].Value
        if (-not (Test-AbsoluteRoute $path)) { continue }

        $before = Get-StatementBefore -Text $text -EndIndex $m.Index
        if ($before -match '(?:var|RouteGroupBuilder|IEndpointRouteBuilder)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*[A-Za-z0-9_.\s]*$') {
            Add-GroupVarPath -File $file.FullName -VarName $Matches[1] -Paths @($path)
        }
    }
}

$extPrefixes = @{}

function Add-ExtPrefix {
    param([string]$Name, [string]$Prefix)

    if (-not $extPrefixes.ContainsKey($Name)) { $extPrefixes[$Name] = @() }
    if ($extPrefixes[$Name] -notcontains $Prefix) { $extPrefixes[$Name] += $Prefix }
}

foreach ($file in $serviceFiles) {
    $text = $serviceText[$file.FullName]

    $chained = @{}
    foreach ($m in [regex]::Matches($text, $groupPattern)) {
        $stmt = Get-StatementAfter -Text $text -StartIndex $m.Index
        foreach ($c in [regex]::Matches($stmt, $extCallPattern)) {
            Add-ExtPrefix -Name $c.Groups[1].Value -Prefix $m.Groups[1].Value
            $chained[$c.Groups[1].Value] = $true
        }
    }

    foreach ($m in [regex]::Matches($text, '\b([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(Map[A-Za-z]*Endpoints?)\s*\(')) {
        $recv = $m.Groups[1].Value
        $name = $m.Groups[2].Value
        if ($chained.ContainsKey($name)) { continue }

        if ($groupVars.ContainsKey($file.FullName) -and $groupVars[$file.FullName].ContainsKey($recv)) {
            foreach ($p in $groupVars[$file.FullName][$recv]) { Add-ExtPrefix -Name $name -Prefix $p }
        }
        else { Add-ExtPrefix -Name $name -Prefix '' }
    }
}

# A method-return registry, so an inline lambda that just forwards a service call can still be
# resolved: `return Results.Ok(await service.GetAllAsync(...))` names no type at all, and the great
# majority of this tree's endpoints are written that way. Without this, most tool DTOs would be
# "server shape not statically reachable" and the gate would be an allowlist with a script attached.
#
# Keyed by method NAME. Ambiguity is resolved by preferring a declaration in the same file, then the
# same service, then the union — the same preference order the route gate uses for ownership.
$methodReturns = @{}   # name -> [ @{ File; Owner; Ref } ]

function Add-MethodReturn {
    param([string]$Name, [string]$File, [string]$Owner, [string]$Ref)

    if (-not $methodReturns.ContainsKey($Name)) { $methodReturns[$Name] = @() }
    foreach ($e in $methodReturns[$Name]) { if ($e.File -eq $File -and $e.Ref -eq $Ref) { return } }
    $methodReturns[$Name] += [pscustomobject]@{ File = $File; Owner = $Owner; Ref = $Ref }
}

foreach ($f in $serverTypeFiles) {
    $mrText = $aliasScan[$f.FullName]
    $mrOwner = Get-ServiceOwnerFromPath $f.FullName
    foreach ($m in [regex]::Matches($mrText, '(?:Task|ValueTask)<\s*(?<t>[^<>]*(?:<(?:[^<>]|<[^<>]*>)*>)?)\s*>\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)\s*\(')) {
        $ref = Resolve-ShapeTypeRef $m.Groups['t'].Value
        if ([string]::IsNullOrWhiteSpace($ref)) { continue }
        Add-MethodReturn -Name $m.Groups['n'].Value -File $f.FullName -Owner $mrOwner -Ref $ref
    }
}

function Get-MethodReturnRefs {
    param([string]$Method, [string]$File, [string]$Owner)

    if (-not $methodReturns.ContainsKey($Method)) { return @() }
    $entries = @($methodReturns[$Method])

    $sameFile = @($entries | Where-Object { $_.File -eq $File })
    if ($sameFile.Count -gt 0) { return @($sameFile | Select-Object -ExpandProperty Ref -Unique) }

    if ($Owner) {
        $sameOwner = @($entries | Where-Object { $_.Owner -eq $Owner })
        if ($sameOwner.Count -gt 0) { return @($sameOwner | Select-Object -ExpandProperty Ref -Unique) }
    }
    return @($entries | Select-Object -ExpandProperty Ref -Unique)
}

# The type(s) a mapping statement produces. FOUR sources, because no single one covers this tree:
#   1. .Produces<T>() on the chain
#   2. a handler method group's typed result — Task<Ok<T>>, Results<Ok<T>, NotFound>
#   3. an inline lambda's Results.Ok(new T { ... })
#   4. an inline lambda's Results.Ok(x) / Results.Ok(await svc.FooAsync(..)) resolved through the
#      method-return registry
# Every one of them is present in src/Services and each alone leaves most endpoints unresolved.
function Get-ProducedTypeNames {
    param([string]$Statement, [string]$FileText, [string]$File, [string]$Owner)

    $names = @()
    function Add-Name {
        param([string]$Ref)
        if ([string]::IsNullOrWhiteSpace($Ref)) { return }
        $r = $Ref.Trim()
        if ($r -match '^(var|new|null|true|false)$') { return }
        if ($script:__producedNames -notcontains $r) { $script:__producedNames += $r }
    }
    $script:__producedNames = @()

    # (1) .Produces<T>()
    foreach ($m in [regex]::Matches($Statement, '\.\s*Produces(?:FromProblem)?<\s*(?<t>[^<>()]*(?:<(?:[^<>]|<[^<>]*>)*>)?)\s*>')) {
        Add-Name (Resolve-ShapeTypeRef $m.Groups['t'].Value)
    }

    # (2) handler method group: group.MapGet("/", ListOrganizations)
    $hm = [regex]::Match($Statement, '^\s*\.\s*Map(?:Get|Post|Put|Delete|Patch|Methods)\s*\(\s*[$@]{0,2}"(?:[^"\\]|\\.)*"\s*,\s*(?:\[[^\]]*\]\s*)*(?<h>[A-Za-z_][A-Za-z0-9_]*)\s*[),]')
    if ($hm.Success) {
        $handler = $hm.Groups['h'].Value
        foreach ($dm in [regex]::Matches($FileText, "(?<ret>[A-Za-z_][A-Za-z0-9_<>,\.\s\[\]\?]*?)\s+$([regex]::Escape($handler))\s*\(")) {
            $head = $FileText.Substring([Math]::Max(0, $dm.Index - 120), [Math]::Min(120, $dm.Index))
            if ($head -notmatch '(public|private|internal|protected|static|async)[^;{}]*$') { continue }
            foreach ($rm in [regex]::Matches($dm.Groups['ret'].Value, '\b(?:Ok|Created|CreatedAtRoute|Accepted|AcceptedAtRoute|JsonHttpResult)<\s*(?<t>[^<>]*(?:<(?:[^<>]|<[^<>]*>)*>)?)\s*>')) {
                Add-Name (Resolve-ShapeTypeRef $rm.Groups['t'].Value)
            }
        }
    }

    # (3)+(4) inline lambda results
    foreach ($rm in [regex]::Matches($Statement, '(?:Typed)?Results\s*\.\s*(?:Ok|Created|CreatedAtRoute|Accepted|AcceptedAtRoute|Json)\s*\(')) {
        $open = $Statement.IndexOf('(', $rm.Index + $rm.Length - 1)
        if ($open -lt 0) { continue }
        $close = Find-Balanced -Text $Statement -Start $open -Open '(' -Close ')'
        if ($close -lt 0) { continue }

        foreach ($arg in (Split-TopLevel $Statement.Substring($open + 1, $close - $open - 1))) {
            $a = $arg.Trim()
            if ($a.Length -eq 0) { continue }

            # new T { ... } / new T(...)
            $nm = [regex]::Match($a, '^new\s+(?<t>[A-Za-z_][A-Za-z0-9_\.]*(?:<(?:[^<>]|<[^<>]*>)*>)?)\s*[\{\(]')
            if ($nm.Success) { Add-Name (Resolve-ShapeTypeRef $nm.Groups['t'].Value); continue }

            # await svc.FooAsync(...) / svc.FooAsync(...)
            $cm = [regex]::Match($a, '^(?:await\s+)?[A-Za-z_][A-Za-z0-9_\.\(\)]*\.\s*(?<m>[A-Za-z_][A-Za-z0-9_]*)\s*\(')
            if ($cm.Success) {
                foreach ($r in (Get-MethodReturnRefs -Method $cm.Groups['m'].Value -File $File -Owner $Owner)) { Add-Name $r }
                continue
            }

            # a local: trace `var x = await svc.FooAsync(..)` / `var x = new T {` in the same statement
            $im = [regex]::Match($a, '^(?<id>[A-Za-z_][A-Za-z0-9_]*)$')
            if (-not $im.Success) { continue }
            $id = [regex]::Escape($im.Groups['id'].Value)

            $vm = [regex]::Match($Statement, "(?:var|[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]]*)\s+$id\s*=\s*new\s+(?<t>[A-Za-z_][A-Za-z0-9_\.]*(?:<(?:[^<>]|<[^<>]*>)*>)?)\s*[\{\(]")
            if ($vm.Success) { Add-Name (Resolve-ShapeTypeRef $vm.Groups['t'].Value); continue }

            $vm2 = [regex]::Match($Statement, "(?:var|[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]]*)\s+$id\s*=\s*(?:await\s+)?[A-Za-z_][A-Za-z0-9_\.\(\)]*\.\s*(?<m>[A-Za-z_][A-Za-z0-9_]*)\s*\(")
            if ($vm2.Success) {
                foreach ($r in (Get-MethodReturnRefs -Method $vm2.Groups['m'].Value -File $File -Owner $Owner)) { Add-Name $r }
            }
        }
    }

    $names = $script:__producedNames
    return $names
}

# owner -> family -> [type names];  plus a union bucket for unattributable tool call sites
$producedByOwner = @{}
$producedUnion = @{}

# owner|family (and '|family' for the union) -> $true when at least one Map<Verb> on that family
# produces a shape this reader cannot name: an anonymous Results.Ok(new { ... }), a raw object, an
# IResult handler. Verbs are NOT distinguished on the tool side (no gate here knows which verb a
# tool issues), so a family carrying both a typed producer and an anonymous one is genuinely
# ambiguous — and a DTO that matches NOTHING on the typed sibling is far more likely to be reading
# the anonymous one correctly than to be broken. Saying so is honest; inventing a partner is not.
$familyHasOpaqueProducer = @{}

function Set-OpaqueProducer {
    param([string]$Owner, [string]$Family)
    $familyHasOpaqueProducer["$Owner|$Family"] = $true
    $familyHasOpaqueProducer["|$Family"] = $true
}

function Test-FamilyHasOpaqueProducer {
    param([string]$Family, [string]$Owner)

    foreach ($k in $familyHasOpaqueProducer.Keys) {
        $parts = $k -split '\|', 2

        # Scope exactly as Get-RouteCandidates scopes: a tool route with a KNOWN owner consults only
        # that service's opaque producers — never the union key, whose owner segment is ''.
        #
        # Getting this wrong made the scoping a no-op: the union key matched every owner, so an
        # anonymous producer on a same-named family in ANY other service marked a tool's route
        # opaque. That widens the withhold-a-verdict branch below, and a genuinely broken DTO can
        # then present as "server shape not statically reachable" and be allowlisted under kind (b)
        # in good faith — exactly the ratchet rot the allowlist header warns about.
        if ($Owner -and $parts[0] -eq '') { continue }
        if ($Owner -and $parts[0] -ne $Owner) { continue }
        if (Test-FamilyMatch -ServiceFamily $parts[1] -ToolFamily $Family) { return $true }
    }
    return $false
}

function Add-Produced {
    param([string]$Owner, [string]$Family, [string[]]$Types)

    foreach ($t in $Types) {
        if (-not $producedUnion.ContainsKey($Family)) { $producedUnion[$Family] = @() }
        if ($producedUnion[$Family] -notcontains $t) { $producedUnion[$Family] += $t }

        if (-not $Owner) { continue }
        if (-not $producedByOwner.ContainsKey($Owner)) { $producedByOwner[$Owner] = @{} }
        if (-not $producedByOwner[$Owner].ContainsKey($Family)) { $producedByOwner[$Owner][$Family] = @() }
        if ($producedByOwner[$Owner][$Family] -notcontains $t) { $producedByOwner[$Owner][$Family] += $t }
    }
}

foreach ($file in $serviceFiles) {
    $text = $serviceText[$file.FullName]
    $owner = Get-ServiceOwnerFromPath $file.FullName

    $ambient = @()
    foreach ($d in [regex]::Matches($text, $extDefPattern)) {
        $name = $d.Groups[1].Value
        if ($extPrefixes.ContainsKey($name)) {
            foreach ($p in $extPrefixes[$name]) { if ($ambient -notcontains $p) { $ambient += $p } }
        }
    }
    if ($ambient.Count -eq 0) { $ambient = @('') }

    foreach ($m in [regex]::Matches($text, $groupPattern)) {
        $path = $m.Groups[1].Value
        $before = Get-StatementBefore -Text $text -EndIndex $m.Index
        if ($before -notmatch '(?:var|RouteGroupBuilder|IEndpointRouteBuilder)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z0-9_.\s]*)$') { continue }
        $varName = $Matches[1]
        $recvChain = $Matches[2].Trim().TrimEnd('.')
        $recv = ($recvChain -split '\.')[-1]

        if (Test-AbsoluteRoute $path) {
            Add-GroupVarPath -File $file.FullName -VarName $varName -Paths @($path)
        }
        elseif ($groupVars.ContainsKey($file.FullName) -and $groupVars[$file.FullName].ContainsKey($recv)) {
            $paths = @()
            foreach ($p in $groupVars[$file.FullName][$recv]) { $paths += (Join-RoutePath -Prefix $p -Fragment $path) }
            Add-GroupVarPath -File $file.FullName -VarName $varName -Paths $paths
        }
        else {
            $paths = @()
            foreach ($p in $ambient) { $paths += (Join-RoutePath -Prefix $p -Fragment $path) }
            Add-GroupVarPath -File $file.FullName -VarName $varName -Paths $paths
        }
    }

    foreach ($m in [regex]::Matches($text, $verbPattern)) {
        $fragment = $m.Groups[1].Value

        $candidates = @()
        if (Test-AbsoluteRoute $fragment) { $candidates += $fragment }
        else {
            $head = $text.Substring([Math]::Max(0, $m.Index - 64), [Math]::Min(64, $m.Index))
            $recv = $null
            if ($head -match '([A-Za-z_][A-Za-z0-9_]*)\s*$') { $recv = $Matches[1] }

            if ($recv -and $groupVars.ContainsKey($file.FullName) -and $groupVars[$file.FullName].ContainsKey($recv)) {
                foreach ($p in $groupVars[$file.FullName][$recv]) { $candidates += (Join-RoutePath -Prefix $p -Fragment $fragment) }
            }
            else {
                foreach ($p in $ambient) { $candidates += (Join-RoutePath -Prefix $p -Fragment $fragment) }
            }
        }

        $stmt = Get-MapStatement -Text $text -StartIndex $m.Index
        $types = @(Get-ProducedTypeNames -Statement $stmt -FileText $text -File $file.FullName -Owner $owner)
        $knownTypes = @($types | Where-Object { Test-ServerTypeKnown $_ })

        foreach ($c in $candidates) {
            $fam = ConvertTo-RouteFamily $c
            if ($fam.Length -eq 0) { continue }
            if ($knownTypes.Count -eq 0) { Set-OpaqueProducer -Owner $owner -Family $fam; continue }
            Add-Produced -Owner $owner -Family $fam -Types $knownTypes
        }
    }
}

# ---------------------------------------------------------------------------
# TOOL SIDE
# ---------------------------------------------------------------------------

$toolFiles = Get-SourceFiles -Roots @($toolsRoot) | Where-Object {
    $registered = $false
    foreach ($l in (Get-Content -LiteralPath $_.FullName)) {
        $t = $l.TrimStart()
        if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
        if ($t -match '^\[McpServerToolType(\(|\])') { $registered = $true; break }
    }
    $registered
}

if ($toolFiles.Count -eq 0) {
    Write-Host "FAIL: no [McpServerToolType] classes found under $toolsRoot — extraction is broken." -ForegroundColor Red
    exit 1
}

$clientFiles = Get-SourceFiles -Roots $clientRoots
$clientText = @{}
foreach ($f in $clientFiles) { $clientText[$f.FullName] = Get-Content -LiteralPath $f.FullName -Raw }

$clientImpls = @{}
foreach ($f in $clientFiles) {
    foreach ($m in [regex]::Matches($clientText[$f.FullName], 'class\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([^\{]+)\{')) {
        $className = $m.Groups[1].Value
        $names = @()
        foreach ($iface in ($m.Groups[2].Value -split ',')) {
            $name = $iface.Trim()
            if ($name -notmatch '^I[A-Za-z0-9_]*Client$') { continue }
            $names += $name
        }
        if ($names.Count -gt 0 -and $className -match '^[A-Za-z_][A-Za-z0-9_]*Client$') { $names += $className }
        foreach ($name in $names) {
            if (-not $clientImpls.ContainsKey($name)) { $clientImpls[$name] = @() }
            if ($clientImpls[$name] -notcontains $f.FullName) { $clientImpls[$name] += $f.FullName }
        }
    }
}

function Get-SoleService {
    param([string]$Text)

    $names = @()
    foreach ($m in [regex]::Matches($Text, 'SorchaService\.([A-Za-z0-9]+)')) {
        if ($names -notcontains $m.Groups[1].Value) { $names += $m.Groups[1].Value }
    }
    if ($names.Count -eq 1) { return $names[0] }
    return $null
}

$clientOwner = @{}
foreach ($iface in $clientImpls.Keys) {
    $owners = @()
    foreach ($implFile in $clientImpls[$iface]) {
        $implName = [IO.Path]::GetFileNameWithoutExtension($implFile)
        $o = Get-SoleService $clientText[$implFile]
        if (-not $o) {
            foreach ($regFile in $clientFiles) {
                $regText = $clientText[$regFile.FullName]
                foreach ($m in [regex]::Matches($regText, "AddHttpClient<[^>]*\b$([regex]::Escape($implName))\b[^>]*>")) {
                    $window = $regText.Substring($m.Index, [Math]::Min(1200, $regText.Length - $m.Index))
                    $o = Get-SoleService $window
                    if ($o) { break }
                }
                if ($o) { break }
            }
        }
        if ($o -and $owners -notcontains $o) { $owners += $o }
    }
    if ($owners.Count -eq 1) { $clientOwner[$iface] = $owners[0] }
}

# Per tool: its local DTOs, the DTOs a service body is deserialized into, and the routes it calls.
$tools = @()

foreach ($file in $toolFiles) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $rawLines = Get-Content -LiteralPath $file.FullName
    $raw = ($rawLines -join "`n")
    $text = Remove-LineComments $raw
    $toolName = [IO.Path]::GetFileNameWithoutExtension($file.FullName)

    # --- local DTOs -----------------------------------------------------------------------------
    $localDtos = @{}
    foreach ($d in @(Get-TypeDeclarations -Text $text -File $file.FullName)) {
        if (-not $d.IsInternal) { continue }          # the tool's own public result records are output, not wire
        if ($d.Name -eq $toolName) { continue }
        if ($d.Props.Count -eq 0) { continue }
        $localDtos[$d.Name] = $d
    }

    # --- roots: the types a response body is actually bound into --------------------------------
    $roots = @()
    foreach ($m in [regex]::Matches($text, '(?:Deserialize|DeserializeAsync|ReadFromJsonAsync|GetFromJsonAsync)\s*<\s*(?<t>[A-Za-z_][A-Za-z0-9_]*(?:<[^<>]*>)?)\??\s*>')) {
        $n = (Split-TypeRef (Resolve-ShapeTypeRef $m.Groups['t'].Value)).Name
        if ($localDtos.ContainsKey($n) -and $roots -notcontains $n) { $roots += $n }
    }

    # --- routes this tool calls (inline, and via the typed clients it uses) ----------------------
    $endpointOwner = @{}
    foreach ($m in [regex]::Matches($text, '(_[A-Za-z0-9_]+)\s*=\s*SorchaServiceAddresses\.TryResolve\s*\([^;]*?SorchaService\.([A-Za-z0-9]+)')) {
        $endpointOwner[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    $soleEndpointOwner = $null
    $distinctOwners = @($endpointOwner.Values | Sort-Object -Unique)
    if ($distinctOwners.Count -eq 1) { $soleEndpointOwner = $distinctOwners[0] }

    $routes = @()   # { Family, Owner }
    function Add-ToolRoute {
        param([ref]$List, [string]$Family, [string]$Owner)
        if ($Family.Length -eq 0) { return }
        foreach ($r in $List.Value) { if ($r.Family -eq $Family -and $r.Owner -eq $Owner) { return } }
        $List.Value += [pscustomobject]@{ Family = $Family; Owner = $Owner }
    }

    foreach ($line in ($text -split "`n")) {
        if ($line.TrimStart().StartsWith('//')) { continue }
        if ($line -match '^\s*\[Description\(') { continue }
        foreach ($p in (Get-ApiPathsInLine $line)) {
            $onLine = @()
            foreach ($fm in [regex]::Matches($line, '_[A-Za-z0-9_]+')) {
                if ($endpointOwner.ContainsKey($fm.Value) -and $onLine -notcontains $endpointOwner[$fm.Value]) {
                    $onLine += $endpointOwner[$fm.Value]
                }
            }
            $owner = $null
            if ($onLine.Count -eq 1) { $owner = $onLine[0] }
            elseif ($onLine.Count -eq 0) { $owner = $soleEndpointOwner }
            Add-ToolRoute -List ([ref]$routes) -Family (ConvertTo-RouteFamily $p) -Owner $owner
        }
    }

    $fields = @{}
    foreach ($m in [regex]::Matches($text, '(?:readonly\s+)?([A-Za-z_][A-Za-z0-9_]*Client)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]*)?;')) {
        $fields[$m.Groups[2].Value] = $m.Groups[1].Value
    }
    foreach ($ctor in [regex]::Matches($text, 'class\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)')) {
        foreach ($param in ($ctor.Groups[1].Value -split ',')) {
            $pm = [regex]::Match($param.Trim(), '^([A-Za-z_][A-Za-z0-9_]*Client)\s+([A-Za-z_][A-Za-z0-9_]*)$')
            if ($pm.Success) { $fields[$pm.Groups[2].Value] = $pm.Groups[1].Value }
        }
    }

    foreach ($m in [regex]::Matches($text, '(?<![A-Za-z0-9_.])(?:this\s*\.\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z0-9_]+)\s*\(')) {
        $fieldName = $m.Groups[1].Value
        $method = $m.Groups[2].Value
        if (-not $fields.ContainsKey($fieldName)) { continue }
        $iface = $fields[$fieldName]
        if (-not $clientImpls.ContainsKey($iface)) { continue }
        $owner = $(if ($clientOwner.ContainsKey($iface)) { $clientOwner[$iface] } else { $null })

        foreach ($implFile in $clientImpls[$iface]) {
            $implText = $clientText[$implFile]
            foreach ($body in (Get-MethodBodies -Text $implText -MethodName $method)) {
                foreach ($bl in ($body.Text -split "`n")) {
                    if ($bl.TrimStart().StartsWith('//')) { continue }
                    foreach ($p in (Get-ApiPathsInLine $bl)) {
                        Add-ToolRoute -List ([ref]$routes) -Family (ConvertTo-RouteFamily $p) -Owner $owner
                    }
                }
            }
        }
    }

    $tools += [pscustomobject]@{
        Name   = $toolName
        File   = $rel
        Text   = $text
        Dtos   = $localDtos
        Roots  = $roots
        Routes = $routes
    }
}

# ---------------------------------------------------------------------------
# NON-VACUITY FLOOR (part 1): DTO discovery
# ---------------------------------------------------------------------------

$allDtos = @()
foreach ($t in $tools) { foreach ($k in $t.Dtos.Keys) { $allDtos += "$($t.Name).$k" } }
$dtoCount = $allDtos.Count

if ($dtoCount -lt $MinDtoCount) {
    Write-Host ("FAIL: only {0} tool response DTO(s) discovered under {1} (floor {2})." -f $dtoCount, $toolsRoot, $MinDtoCount) -ForegroundColor Red
    Write-Host "Nearly every MCP tool binds a service body into a private/internal DTO, so this means the" -ForegroundColor Red
    Write-Host "type reader stopped seeing an idiom rather than that the DTOs went away. Fix the reader; do" -ForegroundColor Red
    Write-Host "not lower the floor." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# PAIR AND WALK
# ---------------------------------------------------------------------------

$violations = @()     # { Key, Kind, Tool, Dto, Property, ServerType, Detail }
$pairings = @()       # { Tool, Dto, ServerType, Depth, Matched, Total }
$resolvedPairs = 0

function Add-Violation {
    param([string]$Key, [string]$Kind, [string]$Tool, [string]$Dto, [string]$Property, [string]$ServerType, [string]$Detail)

    $script:violations += [pscustomobject]@{
        Key = $Key; Kind = $Kind; Tool = $Tool; Dto = $Dto
        Property = $Property; ServerType = $ServerType; Detail = $Detail
    }
}

# Candidate server types for a tool's ROOT DTO: everything produced by any route family the tool
# calls, scoped to the owning service when it is known (the same scoping the route gate applies —
# otherwise a Blueprint-bound shape could be "satisfied" by a same-named Tenant type).
function Get-RouteCandidates {
    param([string]$Family, [string]$Owner)

    $names = @()
    $buckets = @()
    if ($Owner -and $producedByOwner.ContainsKey($Owner)) { $buckets += , $producedByOwner[$Owner] }
    elseif (-not $Owner) { $buckets += , $producedUnion }

    foreach ($bucket in $buckets) {
        foreach ($fam in $bucket.Keys) {
            if (-not (Test-FamilyMatch -ServiceFamily $fam -ToolFamily $Family)) { continue }
            foreach ($t in $bucket[$fam]) { if ($names -notcontains $t) { $names += $t } }
        }
    }
    return $names
}

function Get-RootCandidates {
    param($Tool)

    $names = @()
    foreach ($r in $Tool.Routes) {
        foreach ($t in (Get-RouteCandidates -Family $r.Family -Owner $r.Owner)) {
            if ($names -notcontains $t) { $names += $t }
        }
    }
    return $names
}

# Segment-wise family match, identical in spirit to the route gate: a service '*' accepts any tool
# segment, '**' absorbs the remainder. Comparing raw parameter names ({id} vs {registerId}) would
# false-positive constantly.
function Test-FamilyMatch {
    param([string]$ServiceFamily, [string]$ToolFamily)

    if ($ServiceFamily -eq $ToolFamily) { return $true }
    $s = $ServiceFamily -split '/'
    $t = $ToolFamily -split '/'
    $i = 0
    for (; $i -lt $s.Count; $i++) {
        if ($s[$i] -eq '**') { return $true }
        if ($i -ge $t.Count) { return $false }
        if ($s[$i] -eq '*') { continue }
        if ($s[$i] -ne $t[$i]) { return $false }
    }
    return ($i -eq $t.Count)
}

function Measure-Overlap {
    param($Dto, [string]$ServerTypeRef)

    $serverProps = @(Get-ServerWireProperties -TypeRef $ServerTypeRef)
    if ($serverProps.Count -eq 0) { return -1 }

    $wire = @{}
    foreach ($sp in $serverProps) { $wire[$sp.WireName.ToLowerInvariant()] = $true }

    $matched = 0
    foreach ($p in $Dto.Props) {
        if ($wire.ContainsKey($p.WireName.ToLowerInvariant())) { $matched++ }
    }
    return $matched
}

# Best candidate for a DTO, preferring one no sibling DTO in the same tool has already claimed.
# A tool with several roots (ValidatorStatusTool: a pipeline shape AND a count shape) would otherwise
# hand the same server type to both and report the loser's every property as missing.
function Select-BestServerType {
    param($Dto, [string[]]$Candidates, [hashtable]$Claimed)

    $best = $null
    $bestScore = -1
    $bestClaimed = $true

    foreach ($c in ($Candidates | Sort-Object -Unique)) {
        if (-not (Test-ServerTypeKnown $c)) { continue }
        $score = Measure-Overlap -Dto $Dto -ServerTypeRef $c
        $isClaimed = ($null -ne $Claimed -and $Claimed.ContainsKey($c))

        # unclaimed beats claimed; within the same claim state, the higher score wins
        if ($bestClaimed -and -not $isClaimed) { $bestScore = $score; $best = $c; $bestClaimed = $false; continue }
        if ($bestClaimed -ne $isClaimed) { continue }
        if ($score -gt $bestScore) { $bestScore = $score; $best = $c; $bestClaimed = $isClaimed }
    }

    return [pscustomobject]@{ Ref = $best; Score = $bestScore; WasClaimed = $bestClaimed }
}

foreach ($tool in $tools) {
    $rootCandidates = @(Get-RootCandidates -Tool $tool)

    # A DTO nobody deserializes into is still checked — but as a root of its own, because leaving it
    # unchecked is indistinguishable from a pass. Roots named by a Deserialize<T> go first.
    $rootNames = @($tool.Roots)
    foreach ($k in ($tool.Dtos.Keys | Sort-Object)) { if ($rootNames -notcontains $k) { $rootNames += $k } }

    # (dtoName -> serverTypeName) decided during the walk, so a nested DTO is not re-checked as a root.
    $assigned = @{}

    # Does any route this tool calls have a producer whose shape cannot be named?
    $toolHasOpaqueRoute = $false
    foreach ($r in $tool.Routes) {
        if (Test-FamilyHasOpaqueProducer -Family $r.Family -Owner $r.Owner) { $toolHasOpaqueRoute = $true; break }
    }

    # Order: DTOs a Deserialize<T> actually binds come FIRST — they are the envelopes, and walking
    # one assigns its nested element types before those could be mistaken for envelopes of their own.
    # Within each group, strongest match first, so a tool's best-understood DTO claims its server
    # type before a weaker sibling can take it.
    function Sort-ByMatchStrength {
        param([string[]]$Names)

        $ordered = @()
        foreach ($n in $Names) {
            $pick = Select-BestServerType -Dto $tool.Dtos[$n] -Candidates $rootCandidates -Claimed $null
            $ordered += [pscustomobject]@{ Name = $n; Score = $pick.Score }
        }
        return @($ordered | Sort-Object -Property @{Expression = 'Score'; Descending = $true }, 'Name' |
                Select-Object -ExpandProperty Name)
    }

    $deserialized = @($rootNames | Where-Object { $tool.Roots -contains $_ })
    $orphans = @($rootNames | Where-Object { $tool.Roots -notcontains $_ })
    $rootNames = @(Sort-ByMatchStrength -Names $deserialized) + @(Sort-ByMatchStrength -Names $orphans)

    $claimed = @{}

    # Pass 1: roots reached from a Deserialize<T>, then any orphan DTO.
    # Owners of every route this tool calls. Used only to decide whether the owning service
    # stringifies enums; a mixed or unknown set withholds the verdict.
    $toolOwners = @()
    foreach ($r in $tool.Routes) { if ($toolOwners -notcontains $r.Owner) { $toolOwners += $r.Owner } }

    foreach ($rootName in $rootNames) {
        if ($assigned.ContainsKey($rootName)) { continue }
        $dto = $tool.Dtos[$rootName]

        $pick = Select-BestServerType -Dto $dto -Candidates $rootCandidates -Claimed $claimed
        $server = $pick.Ref

        # No candidate at all, or the only ones left match nothing AND this tool calls a route whose
        # producer is invisible to a textual reader. Either way the honest answer is "cannot tell" —
        # NOT a list of properties measured against a partner picked by elimination.
        if (-not $server -or (($pick.Score -le 0 -or $pick.WasClaimed) -and $toolHasOpaqueRoute)) {
            $why = if (-not $server) {
                'no statically reachable server type is produced by any route this tool calls'
            }
            elseif ($pick.WasClaimed) {
                "a route this tool calls produces an anonymous/opaque shape, and every named candidate is already claimed by a sibling DTO (best was $($pick.Ref))"
            }
            else {
                "a route this tool calls produces an anonymous/opaque shape, and no named candidate matches (best was $($pick.Ref))"
            }
            Add-Violation -Key "$($tool.Name).$rootName" -Kind 'unresolved' -Tool $tool.Name -Dto $rootName `
                -Property '' -ServerType '' -Detail $why
            $assigned[$rootName] = $null
            continue
        }
        $assigned[$rootName] = $server
        $claimed[$server] = $true

        # Breadth-first walk from (dto, serverType). THE NESTED WALK IS THE POINT OF THIS GATE:
        # sorcha_tenant_list was wrong at BOTH levels, and the element-level defect survived the
        # envelope fix because a human, not a check, was the only thing looking one level down.
        $queue = [System.Collections.Generic.Queue[object]]::new()
        $queue.Enqueue([pscustomobject]@{ Dto = $rootName; Server = $server; Depth = 0 })
        $seenPair = @{}

        while ($queue.Count -gt 0) {
            $node = $queue.Dequeue()
            $pairKey = "$($node.Dto)|$($node.Server)"
            if ($seenPair.ContainsKey($pairKey)) { continue }
            $seenPair[$pairKey] = $true
            if ($node.Depth -gt 6) { continue }

            $d = $tool.Dtos[$node.Dto]
            $serverProps = @(Get-ServerWireProperties -TypeRef $node.Server)
            $byWire = @{}
            foreach ($sp in $serverProps) { $byWire[$sp.WireName.ToLowerInvariant()] = $sp }

            $matched = 0
            foreach ($p in $d.Props) {
                $key = $p.WireName.ToLowerInvariant()
                if (-not $byWire.ContainsKey($key)) {
                    Add-Violation -Key "$($tool.Name).$($node.Dto).$($p.Name)" -Kind 'missing' `
                        -Tool $tool.Name -Dto $node.Dto -Property $p.Name -ServerType $node.Server `
                        -Detail "server type $($node.Server) has no '$($p.WireName)'"
                    continue
                }
                $matched++

                # The property EXISTS. Can it be read? (see ENUM WIRE FORM above)
                $sp = $byWire[$key]
                if (Test-ClientTypeCannotBind -ClientPropType $p.Type -ServerPropType $sp.Type `
                        -ServerPropAttrs $sp.Attrs -Owners $toolOwners) {
                    Add-Violation -Key "$($tool.Name).$($node.Dto).$($p.Name)" -Kind 'type' `
                        -Tool $tool.Name -Dto $node.Dto -Property $p.Name -ServerType $node.Server `
                        -Detail ("typed 'string', but $($node.Server).$($sp.WireName) is the enum '$($sp.Type)' " +
                                 "and this service writes it as an INTEGER — deserialization throws and the caller sees an empty result")
                    continue
                }

                # Descend into a nested tool DTO against the matching server property's type.
                $nestedDto = (Split-TypeRef (Resolve-ShapeTypeRef $p.Type)).Name
                if (-not $tool.Dtos.ContainsKey($nestedDto)) { continue }

                $nestedServer = Resolve-ShapeTypeRef $byWire[$key].Type
                if (-not (Test-ServerTypeKnown $nestedServer)) {
                    if (-not $assigned.ContainsKey($nestedDto)) {
                        Add-Violation -Key "$($tool.Name).$nestedDto" -Kind 'unresolved' `
                            -Tool $tool.Name -Dto $nestedDto -Property '' -ServerType '' `
                            -Detail "'$($node.Server).$($p.WireName)' is typed '$($byWire[$key].Type)', whose shape is not statically reachable"
                        $assigned[$nestedDto] = $null
                    }
                    continue
                }

                $assigned[$nestedDto] = $nestedServer
                $queue.Enqueue([pscustomobject]@{ Dto = $nestedDto; Server = $nestedServer; Depth = $node.Depth + 1 })
            }

            $script:resolvedPairs++
            $pairings += [pscustomobject]@{
                Tool = $tool.Name; Dto = $node.Dto; ServerType = $node.Server
                Depth = $node.Depth; Matched = $matched; Total = $d.Props.Count
            }
        }
    }
}

# ---------------------------------------------------------------------------
# SERVICE-CLIENT DTOs  (the surface #1613 actually fell through)
#
# Everything above checks DTOs declared INSIDE a tool file. RegisterSummaryInfo is not one: it lives
# in Sorcha.ServiceClients.Http and is shared by the MCP server, the UI, the CLI and the services
# themselves. It was therefore never in scope, and a name-only gate would not have caught it anyway.
#
# Pairing here is DIRECT, not heuristic: a client method contains the URL literal and the
# ReadFromJsonAsync<T> that binds that URL's body, a few lines apart. Each read is paired with the
# nearest PRECEDING api path in the same file, which is the request it belongs to. That is a far
# stronger join than the tool side's best-overlap guess, so a violation here is worth trusting.
#
# Unresolved client DTOs are COUNTED in the summary rather than allowlisted one by one: this surface
# is new and mostly untyped on the server (94 endpoints still declare .Produces<object>), so an entry
# per unchecked DTO would bury the ratchet. The count is stated so the unchecked surface stays
# visible and can be driven down by typing endpoints.
# ---------------------------------------------------------------------------

$clientHttpRoot = Join-Path $repo 'src/Common/Sorcha.ServiceClients.Http'
$clientDtoDecls = @{}
foreach ($f in $clientFiles) {
    foreach ($d in (Get-TypeDeclarations -Text $clientText[$f.FullName] -File $f.FullName)) {
        if (-not $clientDtoDecls.ContainsKey($d.Name)) { $clientDtoDecls[$d.Name] = $d }
    }
}

# Folder under ServiceClients.Http -> owning service, when the folder names one.
$serviceDirNames = @{}
foreach ($dir in (Get-ChildItem -Path $servicesRoot -Directory -ErrorAction SilentlyContinue)) {
    if ($dir.Name -match '^Sorcha\.([A-Za-z0-9]+)\.Service$') { $serviceDirNames[$Matches[1]] = $true }
}

$readPattern = '(?:ReadFromJsonAsync|GetFromJsonAsync|Deserialize|DeserializeAsync)\s*<\s*(?<t>(?:[^<>]|<(?:[^<>]|<[^<>]*>)*>)+?)\s*>\s*\('
$clientDtoCount = 0
$clientResolvedPairs = 0
$clientUnresolved = 0
$clientPairings = @()

foreach ($f in $clientFiles) {
    if (-not $f.FullName.Replace('\', '/').StartsWith($clientHttpRoot.Replace('\', '/'))) { continue }

    $raw = $clientText[$f.FullName]
    $text = Remove-LineComments $raw

    $rel = $f.FullName.Replace('\', '/').Substring($clientHttpRoot.Replace('\', '/').Length).TrimStart('/')
    $folder = ($rel -split '/')[0]
    $owner = if ($serviceDirNames.ContainsKey($folder)) { $folder } else { $null }

    # Every api path in the file, by position. Matched directly rather than by parsing string
    # literals first: in these clients an "api/..." run only ever occurs inside a request URL.
    $pathAt = @()
    foreach ($pm in [regex]::Matches($text, '(?<![A-Za-z0-9_.-])api/[A-Za-z0-9_./*{}-]*')) {
        $pathAt += [pscustomobject]@{ Index = $pm.Index; Path = (Resolve-Holes $pm.Value) }
    }
    if ($pathAt.Count -eq 0) { continue }

    $seenHere = @{}
    foreach ($rm in [regex]::Matches($text, $readPattern)) {
        $dtoName = (Split-TypeRef (Resolve-ShapeTypeRef $rm.Groups['t'].Value)).Name
        if (-not $clientDtoDecls.ContainsKey($dtoName)) { continue }

        # The request this read belongs to: nearest preceding api path in the same file.
        $path = $null
        foreach ($pa in $pathAt) { if ($pa.Index -lt $rm.Index) { $path = $pa.Path } else { break } }
        if (-not $path) { continue }

        $pairKey = "$dtoName|$path"
        if ($seenHere.ContainsKey($pairKey)) { continue }
        $seenHere[$pairKey] = $true
        $clientDtoCount++

        $dto = $clientDtoDecls[$dtoName]
        $family = ConvertTo-RouteFamily $path
        $candidates = Get-RouteCandidates -Family $family -Owner $owner
        $pick = Select-BestServerType -Dto $dto -Candidates $candidates -Claimed $null

        # A CONVINCING join, or none. Scoring by raw overlap accepted RevokeTransactionResult
        # (RevocationTxId / OriginalTxId / Status) against the Register model on the strength of
        # 'Status' alone, and then reported that correct DTO as broken. Requiring most of the
        # client's own properties to land means a pairing is a shape match, not a name coincidence.
        $ratio = if ($dto.Props.Count -gt 0) { $pick.Score / $dto.Props.Count } else { 0 }
        if (-not $pick.Ref -or $pick.Score -lt 2 -or $ratio -lt 0.6) { $clientUnresolved++; continue }

        $server = $pick.Ref
        $serverProps = @(Get-ServerWireProperties -TypeRef $server)
        $byWire = @{}
        foreach ($sp in $serverProps) { $byWire[$sp.WireName.ToLowerInvariant()] = $sp }

        $owners = if ($owner) { @($owner) } else { @() }
        $label = [IO.Path]::GetFileNameWithoutExtension($f.FullName)

        foreach ($cp in $dto.Props) {
            $k = $cp.WireName.ToLowerInvariant()
            if (-not $byWire.ContainsKey($k)) { continue }   # name gaps: reported by the CLI/tool gates
            $sp = $byWire[$k]
            if (Test-ClientTypeCannotBind -ClientPropType $cp.Type -ServerPropType $sp.Type `
                    -ServerPropAttrs $sp.Attrs -Owners $owners) {
                Add-Violation -Key "$label.$dtoName.$($cp.Name)" -Kind 'type' `
                    -Tool $label -Dto $dtoName -Property $cp.Name -ServerType $server `
                    -Detail ("typed 'string', but $server.$($sp.WireName) is the enum '$($sp.Type)' and the " +
                             "$owner Service writes it as an INTEGER — deserialization throws and the caller sees an empty result")
            }
        }

        $clientResolvedPairs++
        $clientPairings += [pscustomobject]@{
            Tool = $label; Dto = $dtoName; ServerType = $server
            Depth = 0; Matched = $pick.Score; Total = $dto.Props.Count
        }
    }
}

$pairings += $clientPairings

if ($ShowShapes) {
    Write-Host ""
    Write-Host "Resolved DTO -> server type pairings ($($pairings.Count)):" -ForegroundColor Cyan
    foreach ($p in ($pairings | Sort-Object Tool, Depth, Dto)) {
        Write-Host ("  {0,-34} {1,-32} -> {2,-38} depth {3}  {4}/{5} props matched" -f `
                $p.Tool, $p.Dto, $p.ServerType, $p.Depth, $p.Matched, $p.Total)
    }
    Write-Host ""
    Write-Host "Routes per tool:" -ForegroundColor Cyan
    foreach ($t in ($tools | Sort-Object Name)) {
        foreach ($r in $t.Routes) {
            $svc = if ($r.Owner) { $r.Owner } else { 'UNATTRIBUTED' }
            $cands = @(Get-RouteCandidates -Family $r.Family -Owner $r.Owner)
            $shown = if ($cands.Count -gt 0) { ($cands | Sort-Object) -join ', ' } else { '(no produced type resolved)' }
            Write-Host ("  {0,-34} {1,-52} {2,-11} {3}" -f $t.Name, $r.Family, $svc, $shown)
        }
    }
    Write-Host ""
}

# ---------------------------------------------------------------------------
# NON-VACUITY FLOOR (part 2): pairing actually happened
# ---------------------------------------------------------------------------

if ($clientResolvedPairs -lt $MinClientResolvedPairs) {
    Write-Host ("FAIL: only {0} service-client DTO(s) were paired with a server type (floor {1}), out of {2} discovered." -f `
            $clientResolvedPairs, $MinClientResolvedPairs, $clientDtoCount) -ForegroundColor Red
    Write-Host "The client pass is the one that covers Sorcha.ServiceClients.Http, where #1613 lived. If it" -ForegroundColor Red
    Write-Host "resolves nothing it reports nothing, which reads as a pass. Fix the pairing; do not lower the floor." -ForegroundColor Red
    exit 1
}

if ($resolvedPairs -lt $MinResolvedPairs) {
    Write-Host ("FAIL: only {0} tool DTO(s) were paired with a server type (floor {1}), out of {2} discovered." -f `
            $resolvedPairs, $MinResolvedPairs, $dtoCount) -ForegroundColor Red
    Write-Host "Route composition or .Produces<T>/typed-result extraction has stopped working. A gate that" -ForegroundColor Red
    Write-Host "resolves nothing reports nothing, which reads as a pass. Fix the resolver; do not lower the floor." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# ALLOWLIST + REPORT
# ---------------------------------------------------------------------------

$allowed = @{}
if (Test-Path -LiteralPath $allowlistPath) {
    foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
        $trimmed = ($line -split '#')[0].Trim()
        if ($trimmed.Length -eq 0) { continue }
        $allowed[$trimmed] = $true
    }
}
else {
    Write-Host "WARN: allowlist not found at $allowlistPath — treating as empty." -ForegroundColor Yellow
}

$hitAllowed = @{}
$unallowed = @()
foreach ($v in $violations) {
    if ($allowed.ContainsKey($v.Key)) { $hitAllowed[$v.Key] = $true; continue }
    $unallowed += $v
}

$stale = @()
foreach ($entry in $allowed.Keys) {
    if (-not $hitAllowed.ContainsKey($entry)) { $stale += $entry }
}

$failed = $false

if ($unallowed.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: MCP tool response DTO does not agree with the shape its endpoint sends." -ForegroundColor Red
    Write-Host ""
    foreach ($group in ($unallowed | Group-Object Tool | Sort-Object Name)) {
        Write-Host ("  {0}" -f $group.Name) -ForegroundColor Yellow
        foreach ($v in ($group.Group | Sort-Object Dto, Property)) {
            if ($v.Kind -eq 'missing') {
                Write-Host ("      {0}.{1}  ->  {2}" -f $v.Dto, $v.Property, $v.Detail)
            }
            else {
                Write-Host ("      {0}  ->  {1}" -f $v.Dto, $v.Detail)
            }
            Write-Host ("        allowlist key: {0}" -f $v.Key) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
    Write-Host "A property the server never sends does not fail — System.Text.Json binds nothing and the"
    Write-Host "agent receives a well-formed, empty, confident answer. sorcha_tenant_list reported"
    Write-Host "'Retrieved 0 tenant(s)' next to 'totalCount: 27' this way, with the route gate green."
    Write-Host "Fix the DTO to match the server type named on its line. See .mcp-response-shapes-allowlist"
    Write-Host "(a ratchet — it may only shrink) and src/Apps/Sorcha.McpServer/README.md > Adding New Tools."
    Write-Host ""
    Write-Host "Re-run with -ShowShapes to dump every resolved pairing and the routes behind it."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — these DTO properties now match the server (or no longer exist):" -ForegroundColor Red
    foreach ($s in ($stale | Sort-Object)) { Write-Host "  - $s" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Remove these lines from .mcp-response-shapes-allowlist in the same PR. The allowlist may only shrink."
}

if ($failed) { exit 1 }

# The unchecked count is stated, not left to subtraction. This line is what CI surfaces and what a
# reader takes as the coverage claim, and a kind (b) allowlist entry means a DTO is UNCHECKED — not
# that it is correct. Saying "36 allowlisted" alone reads like 36 known-and-handled problems.
$uncheckedCount = @($violations | Where-Object { $_.Kind -eq 'unresolved' }).Count

Write-Host ("OK: mcp-response-shapes gate passed. {0} registered tool class(es), {1} response DTO(s) discovered, {2} DTO/server-type pairing(s) resolved (max nesting depth {3}), {4} allowlisted, {5} DTO(s) UNCHECKED (server shape not statically reachable)." -f `
        $toolFiles.Count,
        $dtoCount,
        $resolvedPairs,
    (@($pairings | Select-Object -ExpandProperty Depth) + 0 | Measure-Object -Maximum).Maximum,
        $allowed.Count,
        $uncheckedCount) -ForegroundColor Green

# Stated, not inferred. The unchecked client count is the honest size of the remaining blind spot:
# it is dominated by endpoints still declaring .Produces<object>, which name no type for a client DTO
# to be compared against. Typing an endpoint moves a DTO from this number into the checked one.
Write-Host ("  Service-client DTOs: {0} discovered, {1} checked against the type their endpoint produces, {2} UNCHECKED (endpoint declares no named response type)." -f `
        $clientDtoCount, $clientResolvedPairs, $clientUnresolved) -ForegroundColor Green

if ($allowed.Count -eq 0) {
    Write-Host "  Allowlist is empty — every MCP tool response DTO agrees with the shape its endpoint sends." -ForegroundColor Green
}
exit 0
