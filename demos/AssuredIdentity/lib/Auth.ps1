# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Secret loading + per-node admin login (FR-009, Constitution II). Never echoes secrets.

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Load KEY=VALUE secrets from deploy/keys.env into a hashtable.
.DESCRIPTION
    Parses simple KEY=VALUE lines (ignores blanks and # comments). Secrets stay
    in memory only; nothing is written or printed. Each installation keeps its own
    JWT signing key here — they are NEVER shared (FR-009). The demo toolkit only
    needs the per-node sysadmin PASSWORD to log in; signing keys are deployed
    server-side and not consumed by the toolkit.
.PARAMETER Path
    Path to the secrets file. Defaults to deploy/keys.env.
#>
function Import-DemoSecrets {
    [CmdletBinding()]
    param([string]$Path = "deploy/keys.env")
    $secrets = @{}
    if (-not (Test-Path -LiteralPath $Path)) {
        return $secrets
    }
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trimmed.Substring(0, $idx).Trim()
        $val = $trimmed.Substring($idx + 1).Trim().Trim('"')
        $secrets[$key] = $val
    }
    return $secrets
}

<#
.SYNOPSIS
    Resolve the sysadmin password for a node from secrets, with a dev default.
.DESCRIPTION
    Looks for a node-specific key (e.g. TINY_ADMIN_PASSWORD) then a shared
    ADMIN_PASSWORD, else falls back to the well-known dev seed password. Never logs it.
#>
function Get-DemoAdminPassword {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Node,
        [hashtable]$Secrets = @{}
    )
    $nodeKey = ("{0}_ADMIN_PASSWORD" -f $Node.id).ToUpperInvariant()
    if ($Secrets.ContainsKey($nodeKey)) { return $Secrets[$nodeKey] }
    if ($Secrets.ContainsKey('ADMIN_PASSWORD')) { return $Secrets['ADMIN_PASSWORD'] }
    return 'Dev_Pass_2025!'
}

<#
.SYNOPSIS
    Log in as the seed sysadmin on a node and return the SorchaWalkthrough admin session.
.DESCRIPTION
    Thin wrapper over Connect-SorchaAdmin using the node's /api base and the
    resolved admin email/password. Requires SorchaWalkthrough to be imported.
#>
function Connect-DemoNodeAdmin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Node,
        [hashtable]$Secrets = @{}
    )
    $api = Get-DemoApiBase -Gateway $Node.gateway
    $email = if ($Node.PSObject.Properties.Name -contains 'adminEmail' -and $Node.adminEmail) { $Node.adminEmail } else { 'admin@sorcha.local' }
    $pw = Get-DemoAdminPassword -Node $Node -Secrets $Secrets
    return Connect-SorchaAdmin -TenantUrl $api -AdminEmail $email -AdminPassword $pw
}
