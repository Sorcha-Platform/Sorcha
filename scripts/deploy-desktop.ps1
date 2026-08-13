# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Sorcha desktop-node deployer (Windows, PowerShell 7+).
.DESCRIPTION
    Pulls the published sorchadev/* images and brings up a single-node Sorcha
    stack sized for ~10-20 users. Pairs with
    docker/desktop/docker-compose.desktop.yml.
.EXAMPLE
    pwsh ./scripts/deploy-desktop.ps1
.EXAMPLE
    pwsh ./scripts/deploy-desktop.ps1 -Tag 2.412.1
.EXAMPLE
    pwsh ./scripts/deploy-desktop.ps1 -Down            # stop the node
    pwsh ./scripts/deploy-desktop.ps1 -Down -Purge     # stop and DELETE data
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$SkipBootstrap,
    [switch]$Down,
    [switch]$Purge
)
$ErrorActionPreference = 'Stop'

# --- locate the bundle -------------------------------------------------------
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$BundleDir  = Join-Path $ScriptDir '..' 'docker' 'desktop' | Resolve-Path | Select-Object -ExpandProperty Path
$ComposeFile = Join-Path $BundleDir 'docker-compose.desktop.yml'
$EnvFile     = Join-Path $BundleDir '.env'

function Ok   ($m) { Write-Host "✓ $m" -ForegroundColor Green }
function Warn ($m) { Write-Host "! $m" -ForegroundColor Yellow }
function Die  ($m) { Write-Host "✗ $m" -ForegroundColor Red; exit 1 }

# --- 1. resolve runtime + compose command ------------------------------------
# License-free first (Podman), then Docker/Rancher.
function Resolve-Compose {
    if ((Get-Command podman -EA SilentlyContinue) -and (podman compose version 2>$null)) {
        $script:Runtime = 'podman'; $script:Compose = @('podman','compose')
    }
    elseif ((Get-Command docker -EA SilentlyContinue) -and (docker compose version 2>$null)) {
        $script:Runtime = 'docker'; $script:Compose = @('docker','compose')
    }
    elseif (Get-Command docker-compose -EA SilentlyContinue) {
        $script:Runtime = 'docker'; $script:Compose = @('docker-compose')
    }
    else {
        Die @"
No container runtime found. Install one (no Docker Desktop licence needed), then re-run:
  Rancher Desktop: https://rancherdesktop.io   (recommended, free)
  Podman Desktop:  https://podman-desktop.io
"@
    }
    Ok "Using runtime: $Runtime ($($Compose -join ' '))"
}

function Invoke-Compose {
    & $Compose[0] @($Compose[1..($Compose.Count-1)]) -f $ComposeFile --env-file $EnvFile @args
}

# --- 2. resource preflight ---------------------------------------------------
# ┌─────────────────────────────────────────────────────────────────────────┐
# │ YOUR CONTRIBUTION — fill in the gate policy.                              │
# │                                                                           │
# │ $TotalGb and $Cpu below are already gathered for you (under WSL2/Podman   │
# │ these reflect the runtime VM's allocation, which is what containers get). │
# │                                                                           │
# │ This scope (core + citizen wallet) targets ~6 GB RAM / 4 vCPU minimum for │
# │ 10-20 users. Decide:                                                      │
# │   • What thresholds to enforce (match the bash reference, or stricter?).  │
# │   • Hard-fail below floor, or warn + prompt to continue? Remember WSL2/   │
# │     Podman often under-reports because the VM is capped well below host   │
# │     RAM — a hard fail may block a machine that's actually fine once the    │
# │     VM is resized. The bash version warns + prompts; you choose.          │
# │                                                                           │
# │ Use Ok / Warn / Die for output. Return nothing (Die exits on its own).    │
# └─────────────────────────────────────────────────────────────────────────┘
function Test-DesktopResources {
    $TotalGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
    $Cpu     = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    Write-Host "Detected: $TotalGb GB RAM, $Cpu vCPU."

    # TODO(you): implement the 6 GB / 4 vCPU gate. Warn + prompt, or hard-fail —
    # your call. See the policy box above and preflight_resources() in the .sh.
}

# --- 3. ensure .env (generate secrets on first run) --------------------------
function New-SecretB64   { [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 })) }
function New-SecretAlnum { -join ((1..32) | ForEach-Object { '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'[(Get-Random -Max 62)] }) }

function Initialize-Env {
    if (Test-Path $EnvFile) { Ok ".env present"; return }
    Warn "No .env found — generating one with fresh secrets."
    Copy-Item (Join-Path $BundleDir '.env.desktop.example') $EnvFile
    $jwt = New-SecretB64; $pgp = New-SecretAlnum; $mgp = New-SecretAlnum   # alnum: mongo pwd is in a URI
    # Per-service ServiceAuth client secrets (#1423) — one per internal service principal.
    $walletS = New-SecretAlnum; $tenantS = New-SecretAlnum; $registerS = New-SecretAlnum
    $validatorS = New-SecretAlnum; $haipS = New-SecretAlnum; $peerS = New-SecretAlnum
    $blueprintS = New-SecretAlnum
    (Get-Content $EnvFile) `
        -replace '^JWT_SIGNING_KEY=.*',         "JWT_SIGNING_KEY=$jwt" `
        -replace '^POSTGRES_PASSWORD=.*',       "POSTGRES_PASSWORD=$pgp" `
        -replace '^MONGO_PASSWORD=.*',          "MONGO_PASSWORD=$mgp" `
        -replace '^WALLET_SERVICE_SECRET=.*',   "WALLET_SERVICE_SECRET=$walletS" `
        -replace '^TENANT_SERVICE_SECRET=.*',   "TENANT_SERVICE_SECRET=$tenantS" `
        -replace '^REGISTER_SERVICE_SECRET=.*', "REGISTER_SERVICE_SECRET=$registerS" `
        -replace '^VALIDATOR_SERVICE_SECRET=.*',"VALIDATOR_SERVICE_SECRET=$validatorS" `
        -replace '^HAIP_SERVICE_SECRET=.*',     "HAIP_SERVICE_SECRET=$haipS" `
        -replace '^PEER_SERVICE_SECRET=.*',     "PEER_SERVICE_SECRET=$peerS" `
        -replace '^BLUEPRINT_SERVICE_SECRET=.*',"BLUEPRINT_SERVICE_SECRET=$blueprintS" |
        Set-Content $EnvFile
    Ok "Generated $EnvFile (secrets written)."
}

function Get-HttpPort {
    $line = Select-String -Path $EnvFile -Pattern '^SORCHA_HTTP_PORT=' | Select-Object -Last 1
    if ($line) { ($line.Line -split '=',2)[1].Trim('"') } else { '8880' }
}

# --- 4. health gate ----------------------------------------------------------
function Wait-ForHealth {
    $port = Get-HttpPort
    $url  = "http://localhost:$port/health"
    Write-Host "Waiting for the gateway to become healthy at $url ..."
    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        try { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 4 | Out-Null; Write-Host ''; Ok "Gateway healthy."; return }
        catch { Start-Sleep 5; Write-Host '.' -NoNewline }
    }
    Invoke-Compose ps
    Die "Gateway not healthy after 5 min. Check logs: $($Compose -join ' ') -f `"$ComposeFile`" logs"
}

# --- 5. bootstrap ------------------------------------------------------------
function Invoke-Bootstrap {
    $port = Get-HttpPort
    $repoBootstrap = Join-Path $ScriptDir 'bootstrap-sorcha.ps1'
    if (Test-Path $repoBootstrap) {
        Write-Host "Running repo bootstrap (bootstrap-sorcha.ps1) ..."
        try { & $repoBootstrap -NonInteractive } catch { Warn "Bootstrap exited non-zero — finish admin setup manually (below)." }
    }
    else {
        Warn "No bootstrap-sorcha.ps1 on this machine (source-less node)."
        Write-Host "Finish setup by registering the first admin via the UI:"
        Write-Host "   http://localhost:$port/app  ->  Register"
    }
}

# --- main --------------------------------------------------------------------
if (-not (Test-Path $ComposeFile)) { Die "Compose file not found: $ComposeFile" }
Resolve-Compose

if ($Down) {
    if ($Purge) { Warn "Tearing down AND deleting all data volumes."; Invoke-Compose down -v }
    else        { Invoke-Compose down }
    Ok "Node stopped."; exit 0
}

Test-DesktopResources
Initialize-Env
if ($Tag) { $env:SORCHA_IMAGE_TAG = $Tag }

Write-Host "Pulling images ..."; Invoke-Compose pull
Write-Host "Starting the node ..."; Invoke-Compose up -d
Wait-ForHealth
if (-not $SkipBootstrap) { Invoke-Bootstrap }

$port = Get-HttpPort
Write-Host ''
Ok "Sorcha desktop node is up."
Write-Host "   Web UI .......... http://localhost:$port/app"
Write-Host "   Citizen wallet .. http://localhost:$port/wallet"
Write-Host "   API health ...... http://localhost:$port/health"
Write-Host "   Stop ............ pwsh ./scripts/deploy-desktop.ps1 -Down"
