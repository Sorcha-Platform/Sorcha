# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Common helpers shared across the Assured Identity demo toolkit lib units.

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Replace {{token}} placeholders in a string from a hashtable of values.
.DESCRIPTION
    Pure string transform. Replaces every {{key}} occurrence with the matching
    value from -Tokens. Keys are matched case-insensitively. Unknown {{tokens}}
    are left intact so callers can detect un-substituted placeholders.
.PARAMETER Text
    The source text (e.g. a blueprint or actor-config JSON string).
.PARAMETER Tokens
    Hashtable of token name -> replacement value.
#>
function Expand-DemoTokens {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][hashtable]$Tokens
    )
    $result = $Text
    foreach ($key in $Tokens.Keys) {
        $pattern = "{{$key}}"
        $result = $result.Replace($pattern, [string]$Tokens[$key])
    }
    return $result
}

<#
.SYNOPSIS
    Return the list of un-substituted {{token}} placeholders remaining in a string.
.DESCRIPTION
    Used by tests and provisioning to assert a template was fully rendered.
#>
function Get-DemoUnresolvedTokens {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text
    )
    $matches = [regex]::Matches($Text, '{{\s*[A-Za-z0-9_]+\s*}}')
    return @($matches | ForEach-Object { $_.Value } | Select-Object -Unique)
}

<#
.SYNOPSIS
    Normalise a node gateway URL into the /api base the SorchaWalkthrough helpers expect.
#>
function Get-DemoApiBase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Gateway)
    return ($Gateway.TrimEnd('/')) + "/api"
}
