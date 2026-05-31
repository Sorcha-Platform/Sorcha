# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Single-source agency-name injection + coherence check (FR-002, FR-005, R3).

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Inject the agency name into a blueprint template's {{issuerName}} tokens.
.DESCRIPTION
    Pure string transform over the blueprint JSON. The demo blueprint template
    carries {{issuerName}} at the participant organisation and x-review header.
    Returns the rendered JSON. Throws if any {{issuerName}} token survives.
.PARAMETER BlueprintJson
    Raw blueprint template JSON string.
.PARAMETER AgencyName
    The single-source agency name.
#>
function Set-BlueprintIssuerName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$BlueprintJson,
        [Parameter(Mandatory)][string]$AgencyName
    )
    $rendered = Expand-DemoTokens -Text $BlueprintJson -Tokens @{ issuerName = $AgencyName }
    if ($rendered -match '\{\{\s*issuerName\s*\}\}') {
        throw "Blueprint still contains an unresolved {{issuerName}} token after injection."
    }
    return $rendered
}

<#
.SYNOPSIS
    Test that the agency name is coherently applied across every tester-visible site.
.DESCRIPTION
    Pure predicate (FR-002/FR-005, SC-004). Returns an object:
      { Coherent = [bool]; Mismatches = [string[]] }
    Coherent iff the org name, register name, published-participant org name, and
    the blueprint's rendered issuerName ALL equal -AgencyName, with no residual
    {{issuerName}} token in the blueprint.
.PARAMETER AgencyName
    Expected single-source name.
.PARAMETER OrgName / RegisterName / ParticipantOrg
    The names actually applied to those artefacts.
.PARAMETER BlueprintJson
    The rendered blueprint JSON (post-injection).
#>
function Test-AgencyNameCoherence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AgencyName,
        [Parameter(Mandatory)][string]$OrgName,
        [Parameter(Mandatory)][string]$RegisterName,
        [Parameter(Mandatory)][string]$ParticipantOrg,
        [Parameter(Mandatory)][string]$BlueprintJson
    )
    $mismatches = @()
    if ($OrgName        -ne $AgencyName) { $mismatches += "org name '$OrgName'" }
    if ($RegisterName   -ne $AgencyName) { $mismatches += "register name '$RegisterName'" }
    if ($ParticipantOrg -ne $AgencyName) { $mismatches += "participant org '$ParticipantOrg'" }

    if (Get-DemoUnresolvedTokens -Text $BlueprintJson | Where-Object { $_ -match 'issuerName' }) {
        $mismatches += "blueprint has unresolved issuerName token"
    } elseif ($BlueprintJson -notmatch [regex]::Escape($AgencyName)) {
        $mismatches += "blueprint issuerName does not contain '$AgencyName'"
    }

    return [pscustomobject]@{
        Coherent   = ($mismatches.Count -eq 0)
        Mismatches = $mismatches
    }
}
