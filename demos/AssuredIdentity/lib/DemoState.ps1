# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Per-run demo state record IO + stale detection (data-model Entity 4).

Set-StrictMode -Version Latest

$script:DemoStateSchemaVersion = 1

<#
.SYNOPSIS
    Read the demo state record, or $null if absent.
#>
function Read-DemoState {
    [CmdletBinding()]
    param([string]$Path = "./state.json")
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        throw "Demo state '$Path' is not valid JSON: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Write the demo state record (pretty JSON), stamping schemaVersion + updatedAt.
.PARAMETER State
    A hashtable or PSCustomObject of state fields.
#>
function Write-DemoState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$State,
        [string]$Path = "./state.json"
    )
    $obj = [ordered]@{}
    if ($State -is [hashtable]) {
        foreach ($k in $State.Keys) { $obj[$k] = $State[$k] }
    } else {
        foreach ($p in $State.PSObject.Properties) { $obj[$p.Name] = $p.Value }
    }
    $obj['schemaVersion'] = $script:DemoStateSchemaVersion
    $obj['updatedAt'] = (Get-Date).ToUniversalTime().ToString('o')
    if (-not $obj.Contains('provisionedAt')) {
        $obj['provisionedAt'] = $obj['updatedAt']
    }
    ($obj | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding UTF8
    return $Path
}

<#
.SYNOPSIS
    Shallow-merge update fields onto an existing state object, returning a hashtable.
#>
function Merge-DemoState {
    [CmdletBinding()]
    param(
        [object]$Existing,
        [Parameter(Mandatory)][hashtable]$Updates
    )
    $merged = @{}
    if ($Existing) {
        foreach ($p in $Existing.PSObject.Properties) { $merged[$p.Name] = $p.Value }
    }
    foreach ($k in $Updates.Keys) { $merged[$k] = $Updates[$k] }
    return $merged
}

<#
.SYNOPSIS
    Stale-state test (data-model "Validation"): the state names a register that
    cannot be read on the node -> the record is stale and must be reconciled.
.PARAMETER State
    The state object (or $null).
.PARAMETER RegisterReadable
    Whether state.registerId reads back on the node.
#>
function Test-DemoStateStale {
    [CmdletBinding()]
    param(
        [object]$State,
        [bool]$RegisterReadable
    )
    if ($null -eq $State) { return $false }
    $hasRegister = ($State.PSObject.Properties.Name -contains 'registerId') -and `
                   -not [string]::IsNullOrWhiteSpace([string]$State.registerId)
    return ($hasRegister -and -not $RegisterReadable)
}
