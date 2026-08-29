#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# McpServerBasics — Get token and launch MCP server.
# Authenticates with Tenant Service, then starts the MCP server Docker container.

param(
    # NO 'n1' — this walkthrough runs the MCP server as a LOCAL container and checks local
    # Docker before anything else ('Docker not running' → then `docker compose` against the
    # local stack). It cannot target a remote node as-is. Making it n1-capable means skipping
    # the container steps for remote profiles, which is a real change, not a ValidateSet edit.
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipStartup
)

$ErrorActionPreference = "Stop"

# Import shared module
$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "MCP Server — Get Token and Run"

# Load secrets
$secrets = Get-SorchaSecrets -WalkthroughName "mcp-server"

# Initialize environment
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipStartup

# Start services if needed
if (-not $SkipStartup) {
    Write-WtStep "Starting Sorcha Services"
    docker-compose up -d
    Write-WtInfo "Waiting 15s for services to initialize..."
    Start-Sleep -Seconds 15
}

# Authenticate
Write-WtStep "Authenticating"
$admin = Connect-SorchaAdmin `
    -TenantUrl $env.TenantUrl `
    -OrgName "MCP Server Demo" `
    -OrgSubdomain "mcp-server" `
    -AdminEmail $secrets.adminEmail `
    -AdminName $secrets.adminName `
    -AdminPassword $secrets.adminPassword

$jwt = Decode-SorchaJwt -Token $admin.Token
Write-WtInfo "Authenticated as: $($jwt.sub)"
if ($jwt.role) { Write-WtInfo "Roles: $($jwt.role -join ', ')" }

# Launch MCP Server
Write-WtStep "Launching MCP Server"
Write-WtInfo "Starting MCP server with JWT authentication..."
Write-WtInfo "Press Ctrl+C to stop"
Write-Host ""

try {
    $envToken = $admin.Token
    docker-compose run --rm mcp-server --jwt-token $envToken
} catch {
    Write-WtFail "MCP server failed: $($_.Exception.Message)"
    exit 1
} finally {
    Remove-Item Env:SORCHA_JWT_TOKEN -ErrorAction SilentlyContinue
}

Write-WtSuccess "MCP Server session ended"
