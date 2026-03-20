# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Reset n1.sorcha.dev - tears down all containers and volumes, pulls fresh images,
# restarts everything, and optionally re-bootstraps.
#
# This is the equivalent of "Docker Desktop down -v" + fresh start.
#
# Usage:
#   .\n1-reset.ps1                    # Reset and re-bootstrap
#   .\n1-reset.ps1 -SkipBootstrap     # Reset without bootstrap
#   .\n1-reset.ps1 -UpdateCompose     # Also upload latest compose files before reset
#   .\n1-reset.ps1 -StartVm           # Start the VM first (if auto-shutdown stopped it)

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "sorcha-n1",

    [Parameter(Mandatory = $false)]
    [string]$AdminUsername = "sorcha",

    [Parameter(Mandatory = $false)]
    [switch]$SkipBootstrap,

    [Parameter(Mandatory = $false)]
    [switch]$UpdateCompose,

    [Parameter(Mandatory = $false)]
    [switch]$StartVm,

    [Parameter(Mandatory = $false)]
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Sorcha n1.sorcha.dev Reset                  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# Get VM IP
# ============================================================================
Write-Host "Finding n1 VM..." -ForegroundColor Yellow

# Start VM if requested (after auto-shutdown)
if ($StartVm) {
    Write-Host "  Starting VM..." -ForegroundColor Gray
    az vm start --resource-group $ResourceGroup --name "sorcha-n1-vm" --output none
    Write-Host "  ✓ VM started" -ForegroundColor Green
    Start-Sleep -Seconds 10
}

$vmIp = az vm list-ip-addresses `
    --resource-group $ResourceGroup `
    --name "sorcha-n1-vm" `
    --query "[0].virtualMachine.network.publicIpAddresses[0].ipAddress" `
    --output tsv 2>$null

if ([string]::IsNullOrEmpty($vmIp)) {
    Write-Error "Cannot find VM. Is it deployed? Run n1-deploy.ps1 first."
    exit 1
}

# Check VM is running
$vmState = az vm get-instance-view `
    --resource-group $ResourceGroup `
    --name "sorcha-n1-vm" `
    --query "instanceView.statuses[1].displayStatus" `
    --output tsv 2>$null

if ($vmState -ne "VM running") {
    Write-Error "VM is not running (status: $vmState). Use -StartVm to start it."
    exit 1
}

Write-Host "  ✓ VM: $vmIp ($vmState)" -ForegroundColor Green
Write-Host ""

# ============================================================================
# Confirm
# ============================================================================
if (-not $Yes) {
    Write-Host "This will:" -ForegroundColor Yellow
    Write-Host "  1. Stop all Sorcha containers on n1" -ForegroundColor White
    Write-Host "  2. Delete ALL data volumes (PostgreSQL, MongoDB, Redis)" -ForegroundColor White
    Write-Host "  3. Pull latest images from DockerHub" -ForegroundColor White
    Write-Host "  4. Start fresh containers" -ForegroundColor White
    if (-not $SkipBootstrap) {
        Write-Host "  5. Re-bootstrap (create org, admin user)" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "  ⚠  ALL DATA ON n1 WILL BE PERMANENTLY DELETED!" -ForegroundColor Red
    Write-Host ""

    $response = Read-Host "Continue? (yes/no)"
    if ($response -ne "yes" -and $response -ne "y") {
        Write-Host "Cancelled." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
}

# ============================================================================
# Upload latest compose files (optional)
# ============================================================================
if ($UpdateCompose) {
    Write-Host "Uploading latest compose files..." -ForegroundColor Yellow

    $filesToUpload = @(
        @{Local = "$repoRoot\docker-compose.yml"; Remote = "/opt/sorcha/docker-compose.yml" },
        @{Local = "$repoRoot\docker-compose.n1.yml"; Remote = "/opt/sorcha/docker-compose.n1.yml" },
        @{Local = "$repoRoot\docker\postgres-init.sql"; Remote = "/opt/sorcha/docker/postgres-init.sql" },
        @{Local = "$repoRoot\docker\certs\aspnetapp.pfx"; Remote = "/opt/sorcha/docker/certs/aspnetapp.pfx" },
        @{Local = "$repoRoot\scripts\n1-setup-remote.sh"; Remote = "/opt/sorcha/n1-setup-remote.sh" }
    )

    ssh -o StrictHostKeyChecking=no "${AdminUsername}@${vmIp}" "mkdir -p /opt/sorcha/docker/certs"

    foreach ($file in $filesToUpload) {
        if (Test-Path $file.Local) {
            scp -o StrictHostKeyChecking=no $file.Local "${AdminUsername}@${vmIp}:$($file.Remote)"
        }
    }

    ssh -o StrictHostKeyChecking=no "${AdminUsername}@${vmIp}" "chmod +x /opt/sorcha/n1-setup-remote.sh"
    Write-Host "  ✓ Files uploaded" -ForegroundColor Green
    Write-Host ""
}

# ============================================================================
# Run reset on VM
# ============================================================================
Write-Host "Running reset on n1..." -ForegroundColor Yellow
Write-Host ""

$resetArgs = "--reset"
if ($SkipBootstrap) {
    $resetArgs += " --skip-bootstrap"
}

ssh -o StrictHostKeyChecking=no -t "${AdminUsername}@${vmIp}" "cd /opt/sorcha && ./n1-setup-remote.sh $resetArgs"

Write-Host ""
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   n1.sorcha.dev Reset Complete!                ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  API Gateway:  http://${vmIp}" -ForegroundColor White
Write-Host "  Dashboard:    http://${vmIp}:18888" -ForegroundColor White
Write-Host ""

if (-not $SkipBootstrap) {
    Write-Host "  Next: Bootstrap the platform:" -ForegroundColor Cyan
    Write-Host "  sorcha bootstrap --profile n1" -ForegroundColor White
    Write-Host ""
}
