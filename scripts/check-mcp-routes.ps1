#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# MCP tool route-contract CI gate.
#
# Every request path an MCP tool issues must correspond to a route family actually mapped by a
# Sorcha service. Nothing verified that join before this gate existed.
#
# Why this is gated:
#
#   The public MCP server advertises ~65 tools to external AI agents. An audit found TEN of them
#   calling HTTP paths that were never mapped in any service — including the ENTIRE participant
#   discovery loop (sorcha_inbox_list, sorcha_action_details, sorcha_action_validate,
#   sorcha_workflow_status). An agent could therefore submit an action but never discover or
#   inspect one. Nothing fails at build time: the tool compiles, the client compiles, the route
#   simply 404s at runtime and the tool reports a generic "failed to retrieve" to the agent, which
#   reads as a transient platform problem rather than a permanently broken tool.
#
#   This is the classic Sorcha seam bug: both sides correct in isolation, the join unverified.
#   It mirrors the Sorcha.Cli.ContractTests precedent (CLAUDE.md pattern 18) for CLI DTOs.
#
# WHAT IS SCANNED
#
#   Tool side  — only classes carrying [McpServerToolType] under
#                src/Apps/Sorcha.McpServer/Tools/**. A tool deliberately left unregistered
#                (e.g. WalletSignTool, spec 139 T029) is absent from the served surface, so a
#                route nobody can call must not fail the gate.
#
#                URLs live on BOTH sides of a seam: some tools build the URL inline against
#                HttpClient, others call a typed method in src/Common/Sorcha.ServiceClients*/**
#                that builds it. Both are followed — the typed-client method bodies actually
#                invoked by a registered tool are scanned too, or most of the ten would be missed.
#
#   Service side — every MapGroup / MapGet / MapPost / MapPut / MapDelete / MapPatch / MapMethods
#                route literal under src/Services/**. Group prefixes are composed with the
#                relative fragments mapped under them, including across files where an endpoint
#                extension method (MapXxxEndpoints) is invoked on a group at its call site.
#
# ROUTE FAMILIES, NOT EXACT MATCHES
#
# SCOPED PER OWNING SERVICE
#
#   A tool's path is checked against the routes mapped by THE SERVICE IT ACTUALLY CALLS, not
#   against the union of all of them. Otherwise a Blueprint-bound `api/inbox` would be satisfied by
#   a same-named route in Tenant and the gate would go green on a tool that still 404s.
#
#   Ownership is DERIVED, never hardcoded. Both sides already name the service:
#     service side — the project directory (src/Services/Sorcha.<X>.Service)
#     typed client — `SorchaServiceAddresses.TryResolve(configuration, SorchaService.<X>)` in the
#                    client's own constructor, or in its AddHttpClient<> registration
#     inline tool  — the same TryResolve call assigning the endpoint field the URL interpolates
#   A route whose owner cannot be established falls back to the union and is reported, so an
#   unattributable call is visible rather than silently strict or silently lax.
#
#   Both sides are reduced to a route FAMILY: the query string is dropped and every parameter
#   segment ({id}, {id:guid}, {Uri.EscapeDataString(x)}) collapses to '*'. So
#     api/registers/{registerId}/transactions   ->  api/registers/*/transactions
#     api/blueprints/{id}/diff?from=2           ->  api/blueprints/*/diff
#
#   Matching is segment-wise against the service families: a service '*' accepts any tool segment
#   (a route parameter really does accept any value), a service literal must match the tool
#   literal, and a catch-all '**' absorbs the remainder. Comparing raw parameter NAMES would
#   false-positive constantly ({id} vs {registerId}) and a gate that cries wolf gets disabled.
#
# Usage:
#   pwsh scripts/check-mcp-routes.ps1              # gate
#   pwsh scripts/check-mcp-routes.ps1 -ShowRoutes  # also dump both extracted sides (diagnostics)
#
# Exit codes:
#   0 - every registered MCP tool route family is mapped by a service (or allowlisted)
#   1 - a tool calls an unmapped route family, or a stale allowlist entry

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch]$ShowRoutes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.mcp-routes-allowlist'

$toolsRoot = Join-Path $repo 'src/Apps/Sorcha.McpServer/Tools'
$servicesRoot = Join-Path $repo 'src/Services'
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

# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------

function Get-SourceFiles {
    param([string[]]$Roots)

    $files = @()
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $files += Get-ChildItem -Path $root -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
    }
    # A path may sit under two roots (Sorcha.ServiceClients.Http is not nested, but be safe).
    return $files | Sort-Object -Property FullName -Unique
}

# Collapse interpolation holes and route parameters to a wildcard segment.
#   {**catchAll}                        -> **
#   {id} {id:guid} {Uri.Escape...(x)}   -> *
function Resolve-Holes {
    param([string]$Text)

    $t = $Text
    $t = [regex]::Replace($t, '\{\*\*[^{}]*\}', '**')
    # Innermost-first, so nested holes such as {Foo(Bar{x})} still collapse.
    for ($i = 0; $i -lt 8; $i++) {
        $next = [regex]::Replace($t, '\{[^{}]*\}', '*')
        if ($next -eq $t) { break }
        $t = $next
    }
    return $t
}

# Reduce a raw path to its route family: no query, no scheme/host, parameters as wildcards,
# lower-cased, no leading or trailing slash, no empty segments.
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

# Every double-quoted literal on a line, with the surrounding quotes stripped.
# Interpolated ($"") and verbatim (@"") literals are picked up by the same pattern.
function Get-StringLiterals {
    param([string]$Line)

    $out = @()
    foreach ($m in [regex]::Matches($Line, '"(?:[^"\\]|\\.)*"')) {
        $out += $m.Value.Substring(1, $m.Value.Length - 2)
    }
    return $out
}

# Every `api/...` request path embedded in a string literal on this line. Interpolation holes are
# collapsed first so `$"{endpoint}/api/actions/{Uri.EscapeDataString(id)}/validate"` is found.
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

# The owning service of a file under src/Services, named as the SorchaService enum names it
# (Sorcha.Blueprint.Service -> Blueprint, Sorcha.ApiGateway -> ApiGateway).
function Get-ServiceOwnerFromPath {
    param([string]$FullPath)

    $p = $FullPath.Replace('\', '/')
    if ($p -match '/src/Services/Sorcha\.([A-Za-z0-9]+)\.Service(/|$)') { return $Matches[1] }
    if ($p -match '/src/Services/Sorcha\.ApiGateway(/|$)') { return 'ApiGateway' }
    return $null
}

function Remove-LineComments {
    param([string]$Text)
    # Conservative: only strips a // comment that starts a line (after whitespace) or follows
    # whitespace, and never inside a string literal on that line is not attempted — Map* route
    # literals never appear after a // on the same line in this tree.
    return ($Text -split "`n" | ForEach-Object {
        if ($_ -match '^\s*//') { '' } else { $_ }
    }) -join "`n"
}

# ---------------------------------------------------------------------------
# SERVICE SIDE: extract every mapped route family
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

# --- Pass A1: group variables resolvable without ambient context (absolute MapGroup paths) ----
# fileFullName -> (varName -> [paths])
$groupVars = @{}

function Add-GroupVarPath {
    param([string]$File, [string]$VarName, [string[]]$Paths)

    if (-not $groupVars.ContainsKey($File)) { $groupVars[$File] = @{} }
    if (-not $groupVars[$File].ContainsKey($VarName)) { $groupVars[$File][$VarName] = @() }
    foreach ($p in $Paths) {
        if ($groupVars[$File][$VarName] -notcontains $p) {
            $groupVars[$File][$VarName] += $p
        }
    }
}

# Locate the statement a match sits in, so a fluent chain broken over several lines is followed.
function Get-StatementAfter {
    param([string]$Text, [int]$StartIndex)

    $end = $Text.IndexOf(';', $StartIndex)
    if ($end -lt 0) { $end = [Math]::Min($Text.Length, $StartIndex + 2000) }
    return $Text.Substring($StartIndex, $end - $StartIndex)
}

function Get-StatementBefore {
    param([string]$Text, [int]$EndIndex)

    $start = $Text.LastIndexOfAny(@(';', '{', '}'), [Math]::Max(0, $EndIndex - 1))
    if ($start -lt 0) { $start = 0 } else { $start = $start + 1 }
    return $Text.Substring($start, $EndIndex - $start)
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

# --- Pass A2: prefix under which each MapXxxEndpoints extension method is invoked -------------
# extensionMethodName -> [prefixes]
$extPrefixes = @{}

function Add-ExtPrefix {
    param([string]$Name, [string]$Prefix)

    if (-not $extPrefixes.ContainsKey($Name)) { $extPrefixes[$Name] = @() }
    if ($extPrefixes[$Name] -notcontains $Prefix) { $extPrefixes[$Name] += $Prefix }
}

foreach ($file in $serviceFiles) {
    $text = $serviceText[$file.FullName]

    # a) chained: app.MapGroup("/api/x").WithTags(..).MapFooEndpoints();
    $chained = @{}
    foreach ($m in [regex]::Matches($text, $groupPattern)) {
        $stmt = Get-StatementAfter -Text $text -StartIndex $m.Index
        foreach ($c in [regex]::Matches($stmt, $extCallPattern)) {
            Add-ExtPrefix -Name $c.Groups[1].Value -Prefix $m.Groups[1].Value
            $chained[$c.Groups[1].Value] = $true
        }
    }

    # b) receiver-qualified: app.MapFooEndpoints();  group.MapFooEndpoints();
    foreach ($m in [regex]::Matches($text, '\b([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(Map[A-Za-z]*Endpoints?)\s*\(')) {
        $recv = $m.Groups[1].Value
        $name = $m.Groups[2].Value
        if ($chained.ContainsKey($name)) { continue }

        if ($groupVars.ContainsKey($file.FullName) -and $groupVars[$file.FullName].ContainsKey($recv)) {
            foreach ($p in $groupVars[$file.FullName][$recv]) { Add-ExtPrefix -Name $name -Prefix $p }
        }
        else {
            # Invoked on the app / top-level builder: the extension's own literals are absolute.
            Add-ExtPrefix -Name $name -Prefix ''
        }
    }
}

# --- Pass B: resolve every Map<Verb> literal to one or more absolute routes -------------------
$serviceRoutes = [System.Collections.Generic.HashSet[string]]::new()   # union, for fallback only
$serviceRoutesByOwner = @{}                                           # owner -> HashSet[string]
$serviceRouteSamples = @{}

function Add-ServiceRoute {
    param([string]$Family, [string]$Owner, [string]$RelFile)

    [void]$serviceRoutes.Add($Family)
    if ($Owner) {
        if (-not $serviceRoutesByOwner.ContainsKey($Owner)) {
            $serviceRoutesByOwner[$Owner] = [System.Collections.Generic.HashSet[string]]::new()
        }
        [void]$serviceRoutesByOwner[$Owner].Add($Family)
    }
    if (-not $serviceRouteSamples.ContainsKey($Family)) { $serviceRouteSamples[$Family] = $RelFile }
}

foreach ($file in $serviceFiles) {
    $text = $serviceText[$file.FullName]

    # Ambient prefixes for relative fragments in this file: the prefixes under which the endpoint
    # extension methods DEFINED here are invoked (possibly from another file).
    $ambient = @()
    foreach ($d in [regex]::Matches($text, $extDefPattern)) {
        $name = $d.Groups[1].Value
        if ($extPrefixes.ContainsKey($name)) {
            foreach ($p in $extPrefixes[$name]) { if ($ambient -notcontains $p) { $ambient += $p } }
        }
    }
    if ($ambient.Count -eq 0) { $ambient = @('') }

    # Group variables again, now able to resolve a relative MapGroup inside an extension method.
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

    # NOTE: a MapGroup path is a PREFIX, not a route. `MapGroup("/api/workflows")` with only
    # `/{id}/disclosures` hanging off it does not serve GET /api/workflows. Recording group paths
    # as routes would mask exactly that class of miss, so only Map<Verb> literals become routes.
    foreach ($m in [regex]::Matches($text, $verbPattern)) {
        $fragment = $m.Groups[1].Value

        $candidates = @()
        if (Test-AbsoluteRoute $fragment) {
            $candidates += $fragment
        }
        else {
            # Receiver immediately before the ".MapGet(" — a group variable, or the extension param.
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

        foreach ($c in $candidates) {
            $fam = ConvertTo-RouteFamily $c
            if ($fam.Length -eq 0) { continue }
            Add-ServiceRoute -Family $fam `
                -Owner (Get-ServiceOwnerFromPath $file.FullName) `
                -RelFile ([IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/'))
        }
    }
}

# ---------------------------------------------------------------------------
# TOOL SIDE: registered tools, plus the typed-client methods they reach
# ---------------------------------------------------------------------------

# A class is on the served surface only if the attribute is applied, not merely MENTIONED. The
# deliberately-unregistered WalletSignTool documents `<c>[McpServerToolType]</c>` in its XML doc
# to explain its own absence, so comment lines must be excluded or the gate would fail on a route
# nobody can call.
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

# clientTypeName -> implementation file(s). Keyed by BOTH the interface a client implements and
# the client's own concrete class name: a tool field typed as the concrete class (a legitimate,
# already-present idiom — AddHttpClient<T> registers the concrete type) would otherwise be invisible
# to the scan, and an unscanned tool is a SILENTLY unchecked tool, not a reported one.
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
        # Only name the concrete type when it actually implements a client interface — that is what
        # makes it a service client rather than any other class living under the client roots.
        if ($names.Count -gt 0 -and $className -match '^[A-Za-z_][A-Za-z0-9_]*Client$') {
            $names += $className
        }
        foreach ($name in $names) {
            if (-not $clientImpls.ContainsKey($name)) { $clientImpls[$name] = @() }
            if ($clientImpls[$name] -notcontains $f.FullName) { $clientImpls[$name] += $f.FullName }
        }
    }
}

# Which Sorcha service does each typed client address? Derived, not hardcoded: a client either
# resolves its own base address in its constructor
#   SorchaServiceAddresses.TryResolve(configuration, SorchaService.Blueprint)
# or has it set at its AddHttpClient<> registration (HaipServiceClient does the latter).
# interfaceName -> SorchaService name
$clientOwner = @{}

function Get-SoleService {
    param([string]$Text)

    $names = @()
    foreach ($m in [regex]::Matches($Text, 'SorchaService\.([A-Za-z0-9]+)')) {
        if ($names -notcontains $m.Groups[1].Value) { $names += $m.Groups[1].Value }
    }
    if ($names.Count -eq 1) { return $names[0] }
    return $null
}

foreach ($iface in $clientImpls.Keys) {
    $owners = @()
    foreach ($implFile in $clientImpls[$iface]) {
        $implName = [IO.Path]::GetFileNameWithoutExtension($implFile)

        # (1) the client's own file
        $o = Get-SoleService $clientText[$implFile]

        # (2) otherwise its AddHttpClient<> registration, wherever that lives
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

# Extract the body of a named method (block or expression bodied) from a class file.
function Get-MethodBodies {
    param([string]$Text, [string]$MethodName)

    $bodies = @()
    foreach ($m in [regex]::Matches($Text, "(?<![A-Za-z0-9_])$([regex]::Escape($MethodName))\s*\(")) {
        # Skip the interface declaration / call sites: require a preceding accessibility modifier.
        $head = $Text.Substring([Math]::Max(0, $m.Index - 200), [Math]::Min(200, $m.Index))
        if ($head -notmatch '(public|private|internal|protected)[^;{}]*$') { continue }

        # Walk to the end of the parameter list.
        $i = $m.Index + $m.Length - 1
        $depth = 0
        while ($i -lt $Text.Length) {
            if ($Text[$i] -eq '(') { $depth++ }
            elseif ($Text[$i] -eq ')') { $depth--; if ($depth -eq 0) { break } }
            $i++
        }
        if ($i -ge $Text.Length) { continue }
        $j = $i + 1

        # Skip whitespace and any trailing constraints before the body.
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

$toolRoutes = @()   # { Family, Raw, File, Line, Tool, Via }

foreach ($file in $toolFiles) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lines = Get-Content -LiteralPath $file.FullName
    $text = ($lines -join "`n")

    $toolName = [IO.Path]::GetFileNameWithoutExtension($file.FullName)

    # Endpoint fields the tool interpolates into inline URLs, and the service each addresses:
    #   _peerServiceEndpoint = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Peer)
    $endpointOwner = @{}
    foreach ($m in [regex]::Matches($text, '(_[A-Za-z0-9_]+)\s*=\s*SorchaServiceAddresses\.TryResolve\s*\([^;]*?SorchaService\.([A-Za-z0-9]+)')) {
        $endpointOwner[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    $soleEndpointOwner = $null
    $distinctOwners = @($endpointOwner.Values | Sort-Object -Unique)
    if ($distinctOwners.Count -eq 1) { $soleEndpointOwner = $distinctOwners[0] }

    # (a) URLs built inline in the tool itself.
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line.TrimStart().StartsWith('//')) { continue }
        if ($line -match '^\s*\[Description\(') { continue }   # prose, not a request path
        foreach ($p in (Get-ApiPathsInLine $line)) {
            $fam = ConvertTo-RouteFamily $p
            if ($fam.Length -eq 0) { continue }

            # Attribute to the endpoint field this very line interpolates; fall back to the tool's
            # only endpoint field when the URL is assembled away from the field reference.
            $owner = $null
            $onLine = @()
            foreach ($fm in [regex]::Matches($line, '_[A-Za-z0-9_]+')) {
                if ($endpointOwner.ContainsKey($fm.Value) -and $onLine -notcontains $endpointOwner[$fm.Value]) {
                    $onLine += $endpointOwner[$fm.Value]
                }
            }
            if ($onLine.Count -eq 1) { $owner = $onLine[0] }
            elseif ($onLine.Count -eq 0) { $owner = $soleEndpointOwner }

            $toolRoutes += [pscustomobject]@{
                Family = $fam; Raw = $p; File = $rel; Line = ($i + 1); Tool = $toolName
                Via    = 'inline'; Owner = $owner
            }
        }
    }

    # (b) URLs built by the typed clients this tool calls.
    #
    # A typed client reaches a tool three ways and ALL of them must be seen. A reference the scan
    # cannot see silently drops that tool from the gate — which reads as a PASS, not as a report:
    #   1. a plain readonly field ...................  private readonly IFooClient _foo;
    #   2. the same field WITH an initialiser .......  private readonly IFooClient _foo = foo;
    #   3. a primary-constructor parameter, used
    #      directly .................................  sealed class T(IFooClient foo)
    # Idioms 2 and 3 are already present elsewhere in this tree, so this is a live gap, not a
    # hypothetical one: converting one tool to the primary-constructor form dropped the call-site
    # count by one and left the gate green with a bogus route in place.
    #
    # The declared type may be the INTERFACE or the CONCRETE client class; $clientImpls keys both.
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

    foreach ($m in [regex]::Matches($text, '(?<![A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z0-9_]+)\s*\(')) {
        $fieldName = $m.Groups[1].Value
        $method = $m.Groups[2].Value
        if (-not $fields.ContainsKey($fieldName)) { continue }
        $iface = $fields[$fieldName]
        if (-not $clientImpls.ContainsKey($iface)) { continue }

        foreach ($implFile in $clientImpls[$iface]) {
            $implText = $clientText[$implFile]
            $implRel = [IO.Path]::GetRelativePath($repo, $implFile).Replace('\', '/')
            foreach ($body in (Get-MethodBodies -Text $implText -MethodName $method)) {
                foreach ($bl in ($body.Text -split "`n")) {
                    if ($bl.TrimStart().StartsWith('//')) { continue }
                    foreach ($p in (Get-ApiPathsInLine $bl)) {
                        $fam = ConvertTo-RouteFamily $p
                        if ($fam.Length -eq 0) { continue }
                        $toolRoutes += [pscustomobject]@{
                            Family = $fam
                            Raw    = $p
                            File   = $implRel
                            Line   = (Get-LineNumber -Text $implText -Index $body.Start)
                            Tool   = $toolName
                            Via    = "$iface.$method"
                            Owner  = $(if ($clientOwner.ContainsKey($iface)) { $clientOwner[$iface] } else { $null })
                        }
                    }
                }
            }
        }
    }
}

# One overload / one repeated call site should report once.
$seen = @{}
$deduped = @()
foreach ($t in $toolRoutes) {
    $key = "$($t.Tool)|$($t.File)|$($t.Line)|$($t.Family)|$($t.Via)"
    if ($seen.ContainsKey($key)) { continue }
    $seen[$key] = $true
    $deduped += $t
}
$toolRoutes = $deduped

# ---------------------------------------------------------------------------
# NON-VACUITY: broken extraction must FAIL, not read as a clean gate.
# ---------------------------------------------------------------------------
#
# There has always been a floor on the tool-class side ($toolFiles.Count -eq 0 above). There was
# none here, so any breakage in route extraction — a regex that stops matching an idiom, a client
# root that moves, a refactor of the typed clients — produced "0 violations" and exit 0. A gate
# that cannot see anything is not a gate that found nothing wrong.
$toolsWithRoutes = @($toolRoutes | Select-Object -ExpandProperty Tool -Unique).Count
$minToolsWithRoutes = [int][Math]::Floor($toolFiles.Count / 2)

if ($toolRoutes.Count -eq 0) {
    Write-Host "FAIL: no request paths extracted from any [McpServerToolType] class — extraction is broken." -ForegroundColor Red
    exit 1
}

if ($toolsWithRoutes -lt $minToolsWithRoutes) {
    Write-Host ("FAIL: only {0} of {1} registered tool class(es) yielded a request path (floor {2})." -f `
            $toolsWithRoutes, $toolFiles.Count, $minToolsWithRoutes) -ForegroundColor Red
    Write-Host "Nearly every MCP tool calls a backend, so this means extraction stopped seeing an idiom" -ForegroundColor Red
    Write-Host "rather than that the tools stopped calling anything. Fix the resolver; do not lower the floor." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# MATCH
# ---------------------------------------------------------------------------

$serviceSegments = @{}
foreach ($s in $serviceRoutes) { $serviceSegments[$s] = ($s -split '/') }

function Test-RouteMapped {
    param([string]$Family, [string]$Owner)

    # Scope to the service the tool actually calls. Without this, a Blueprint-bound api/inbox would
    # be satisfied by a same-named Tenant route and the gate would go green on a broken tool.
    $candidates = $null
    if ($Owner -and $serviceRoutesByOwner.ContainsKey($Owner)) {
        $candidates = $serviceRoutesByOwner[$Owner]
    }
    elseif ($Owner) {
        return $false      # owner known, but that service maps no routes at all
    }
    else {
        $candidates = $serviceRoutes   # unattributable: fall back to the union, and report it
    }

    if ($candidates.Contains($Family)) { return $true }

    $tool = $Family -split '/'
    foreach ($svc in $candidates) {
        $s = $serviceSegments[$svc]
        $ok = $true
        $i = 0
        for (; $i -lt $s.Count; $i++) {
            if ($s[$i] -eq '**') { return $true }
            if ($i -ge $tool.Count) { $ok = $false; break }
            if ($s[$i] -eq '*') { continue }
            if ($s[$i] -ne $tool[$i]) { $ok = $false; break }
        }
        if ($ok -and $i -eq $tool.Count) { return $true }
    }
    return $false
}

# ---------------------------------------------------------------------------
# ALLOWLIST + REPORT
# ---------------------------------------------------------------------------

$allowed = @{}
if (Test-Path -LiteralPath $allowlistPath) {
    foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        if ($trimmed.StartsWith('#')) { continue }
        $allowed[(ConvertTo-RouteFamily $trimmed)] = $true
    }
}
else {
    Write-Host "WARN: allowlist not found at $allowlistPath — treating as empty." -ForegroundColor Yellow
}

if ($ShowRoutes) {
    Write-Host ""
    Write-Host "Service route families ($($serviceRoutes.Count)):" -ForegroundColor Cyan
    foreach ($s in ($serviceRoutes | Sort-Object)) { Write-Host "  $s" }
    Write-Host ""
    Write-Host "Tool route families ($(($toolRoutes | Select-Object -ExpandProperty Family -Unique).Count) distinct, $($toolRoutes.Count) call sites):" -ForegroundColor Cyan
    foreach ($t in ($toolRoutes | Sort-Object Family, Tool)) {
        $svc = if ($t.Owner) { $t.Owner } else { 'UNATTRIBUTED' }
        Write-Host ("  {0,-52} -> {1,-11} {2} ({3}) {4}:{5}" -f $t.Family, $svc, $t.Tool, $t.Via, $t.File, $t.Line)
    }
    Write-Host ""
    Write-Host "Owning service per typed client:" -ForegroundColor Cyan
    foreach ($k in ($clientOwner.Keys | Sort-Object)) { Write-Host ("  {0,-36} -> {1}" -f $k, $clientOwner[$k]) }
    Write-Host ""
}

$violations = @()
$hitAllowed = @{}

foreach ($t in $toolRoutes) {
    if (Test-RouteMapped -Family $t.Family -Owner $t.Owner) { continue }
    if ($allowed.ContainsKey($t.Family)) { $hitAllowed[$t.Family] = $true; continue }
    $violations += $t
}

$unattributed = @($toolRoutes | Where-Object { -not $_.Owner })

$stale = @()
foreach ($entry in $allowed.Keys) {
    if (-not $hitAllowed.ContainsKey($entry)) { $stale += $entry }
}

$failed = $false

if ($violations.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: MCP tool calls a route family the service it addresses does not map." -ForegroundColor Red
    Write-Host ""
    foreach ($group in ($violations | Group-Object Family | Sort-Object Name)) {
        Write-Host ("  {0}" -f $group.Name) -ForegroundColor Yellow
        foreach ($v in ($group.Group | Sort-Object File, Line)) {
            $svc = if ($v.Owner) { $v.Owner } else { 'UNATTRIBUTED' }
            Write-Host ("      {0}:{1}  {2}  [{3}] -> {4} Service" -f $v.File, $v.Line, $v.Tool, $v.Via, $svc)
        }
    }
    Write-Host ""
    Write-Host "Each path above 404s at runtime AGAINST THE SERVICE NAMED ON ITS LINE — a same-named"
    Write-Host "route in a different service does not help. The tool compiles, the client compiles, and the"
    Write-Host "agent is told the platform failed — so a permanently broken tool reads as a"
    Write-Host "transient outage. Map the route in the service, or repoint the tool at the route"
    Write-Host "that already exists. See .mcp-routes-allowlist (a ratchet — it may only shrink)"
    Write-Host "and src/Apps/Sorcha.McpServer/README.md > Adding New Tools."
    Write-Host ""
    Write-Host "Re-run with -ShowRoutes to dump both extracted sides."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — these route families are now mapped (or no longer called):" -ForegroundColor Red
    foreach ($s in ($stale | Sort-Object)) { Write-Host "  - $s" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "Remove these lines from .mcp-routes-allowlist in the same PR. The allowlist may only shrink."
}

if ($unattributed.Count -gt 0) {
    Write-Host ""
    Write-Host "NOTE: $($unattributed.Count) tool call site(s) could not be attributed to an owning service and" -ForegroundColor Yellow
    Write-Host "      were checked against the union of all services (weaker, but never falsely strict):" -ForegroundColor Yellow
    foreach ($u in ($unattributed | Sort-Object Tool, Family)) {
        Write-Host ("        {0,-44} {1} [{2}]" -f $u.Family, $u.Tool, $u.Via)
    }
    Write-Host ""
}

if ($failed) { exit 1 }

Write-Host ("OK: mcp-routes gate passed. {0} registered tool class(es), {1} tool call site(s) across {2} route famil(ies), checked per owning service against {3} mapped service route famil(ies) in {4} service(s). {5} allowlisted, {6} unattributed." -f `
        $toolFiles.Count,
        $toolRoutes.Count,
    ($toolRoutes | Select-Object -ExpandProperty Family -Unique).Count,
        $serviceRoutes.Count,
        $serviceRoutesByOwner.Count,
        $allowed.Count,
        $unattributed.Count) -ForegroundColor Green

if ($allowed.Count -eq 0) {
    Write-Host "  Allowlist is empty — every MCP tool route family is mapped by a service." -ForegroundColor Green
}
exit 0
