# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Node inventory loader + selectors (FR-006, FR-007). Pure-logic + file IO.

Set-StrictMode -Version Latest

$script:DemoNodeRequiredFields = @('id', 'role', 'gateway', 'installationName')
$script:DemoNodeRoles = @('issuer', 'subscriber')

<#
.SYNOPSIS
    Load and validate a demo node inventory (demo-nodes.json).
.DESCRIPTION
    Reads the JSON file, validates each node entry (required fields, role enum,
    well-formed absolute gateway URI, unique ids) and returns the node array as
    PSCustomObjects. Fails fast with a clear message on any violation (FR-006).
.PARAMETER Path
    Path to the inventory JSON file. Defaults to ./demo-nodes.json.
#>
function Get-DemoNodeInventory {
    [CmdletBinding()]
    param([string]$Path = (Join-Path $script:DemoRoot "demo-nodes.json"))

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Node inventory not found at '$Path'. Copy demo-nodes.example.json and edit it for your installations."
    }

    try {
        $doc = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        throw "Node inventory '$Path' is not valid JSON: $($_.Exception.Message)"
    }

    if (-not ($doc.PSObject.Properties.Name -contains 'nodes') -or $null -eq $doc.nodes) {
        throw "Node inventory '$Path' must contain a top-level 'nodes' array."
    }

    $nodes = @($doc.nodes)
    if ($nodes.Count -lt 1) {
        throw "Node inventory '$Path' must list at least one node."
    }

    $seenIds = @{}
    foreach ($node in $nodes) {
        foreach ($field in $script:DemoNodeRequiredFields) {
            $value = $node.PSObject.Properties[$field]
            if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value.Value)) {
                throw "Node inventory '$Path': a node is missing required field '$field'."
            }
        }
        if ($script:DemoNodeRoles -notcontains $node.role) {
            throw "Node inventory '$Path': node '$($node.id)' has invalid role '$($node.role)' (expected issuer|subscriber)."
        }
        $uri = $null
        if (-not [System.Uri]::TryCreate([string]$node.gateway, [System.UriKind]::Absolute, [ref]$uri)) {
            throw "Node inventory '$Path': node '$($node.id)' has malformed gateway URL '$($node.gateway)'."
        }
        if ($seenIds.ContainsKey($node.id)) {
            throw "Node inventory '$Path': duplicate node id '$($node.id)'."
        }
        $seenIds[$node.id] = $true
    }

    return $nodes
}

<#
.SYNOPSIS
    Select a node from the inventory by its id. Throws if not found.
#>
function Select-DemoNode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Inventory,
        [Parameter(Mandatory)][string]$Id
    )
    $node = $Inventory | Where-Object { $_.id -eq $Id } | Select-Object -First 1
    if (-not $node) {
        $available = ($Inventory | ForEach-Object { $_.id }) -join ', '
        throw "Node id '$Id' not found in inventory. Available: $available"
    }
    return $node
}

<#
.SYNOPSIS
    Return the first node with the given role, or throw if none match.
#>
function Get-DemoNodeByRole {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Inventory,
        [Parameter(Mandatory)][ValidateSet('issuer', 'subscriber')][string]$Role
    )
    $node = $Inventory | Where-Object { $_.role -eq $Role } | Select-Object -First 1
    if (-not $node) {
        throw "No node with role '$Role' in the inventory."
    }
    return $node
}
