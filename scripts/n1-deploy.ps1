# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# One-command deployment script for Sorcha n1.sorcha.dev debug node
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - SSH key pair (~/.ssh/id_rsa.pub or specify -SshKeyPath)
#   - Docker images pushed to DockerHub (sorchadev/*)
#
# Usage:
#   .\n1-deploy.ps1                          # Full deploy (infra + setup)
#   .\n1-deploy.ps1 -SkipInfra               # Skip Bicep, just upload and start
#   .\n1-deploy.ps1 -SshCidr "1.2.3.4/32"    # Restrict SSH to your IP

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "sorcha-n1",

    [Parameter(Mandatory = $false)]
    [string]$Location = "uksouth",

    [Parameter(Mandatory = $false)]
    [string]$VmSize = "Standard_B2as_v2",

    [Parameter(Mandatory = $false)]
    [string]$SshKeyPath = "$env:USERPROFILE\.ssh\id_rsa.pub",

    [Parameter(Mandatory = $false)]
    [string]$SshCidr,

    [Parameter(Mandatory = $false)]
    [switch]$SkipInfra,

    [Parameter(Mandatory = $false)]
    [switch]$SkipSetup,

    [Parameter(Mandatory = $false)]
    [string]$AdminUsername = "sorcha"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host ""
Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Sorcha n1.sorcha.dev Deployment             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# Prerequisites
# ============================================================================
Write-Host "[1/6] Checking prerequisites..." -ForegroundColor Yellow

# Azure CLI
try {
    $azAccount = az account show --output json 2>$null | ConvertFrom-Json
    Write-Host "  ✓ Azure CLI: $($azAccount.user.name) ($($azAccount.name))" -ForegroundColor Green
}
catch {
    Write-Error "Not logged in to Azure. Run: az login"
    exit 1
}

# SSH key
if (-not (Test-Path $SshKeyPath)) {
    Write-Error "SSH public key not found at: $SshKeyPath"
    Write-Host "  Generate one with: ssh-keygen -t ed25519" -ForegroundColor Yellow
    exit 1
}
$sshPubKey = Get-Content $SshKeyPath -Raw
Write-Host "  ✓ SSH key: $SshKeyPath" -ForegroundColor Green

# Determine SSH CIDR (auto-detect if not provided)
if ([string]::IsNullOrEmpty($SshCidr)) {
    Write-Host "  Detecting your public IP..." -ForegroundColor Gray
    try {
        $myIp = (Invoke-RestMethod -Uri "https://api.ipify.org" -TimeoutSec 5).Trim()
        $SshCidr = "$myIp/32"
        Write-Host "  ✓ SSH restricted to: $SshCidr" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not detect public IP. Using 0.0.0.0/0 (open SSH - change this!)"
        $SshCidr = "0.0.0.0/0"
    }
}
else {
    Write-Host "  ✓ SSH restricted to: $SshCidr" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# Deploy Infrastructure (Bicep)
# ============================================================================
if (-not $SkipInfra) {
    Write-Host "[2/6] Deploying Azure infrastructure..." -ForegroundColor Yellow
    Write-Host "  Resource Group: $ResourceGroup" -ForegroundColor Gray
    Write-Host "  Location: $Location" -ForegroundColor Gray
    Write-Host "  VM Size: $VmSize" -ForegroundColor Gray
    Write-Host ""

    $bicepFile = Join-Path $repoRoot "infra\n1-vm.bicep"

    $deployOutput = az deployment sub create `
        --location $Location `
        --template-file $bicepFile `
        --parameters resourceGroupName=$ResourceGroup `
        --parameters location=$Location `
        --parameters vmSize=$VmSize `
        --parameters adminUsername=$AdminUsername `
        --parameters sshPublicKey="$sshPubKey" `
        --parameters allowedSshCidr=$SshCidr `
        --output json 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Bicep deployment failed: $deployOutput"
        exit 1
    }

    $deployment = $deployOutput | ConvertFrom-Json
    $vmIp = $deployment.properties.outputs.vmPublicIp.value
    $vmFqdn = $deployment.properties.outputs.vmFqdn.value

    Write-Host "  ✓ VM deployed: $vmIp ($vmFqdn)" -ForegroundColor Green
}
else {
    Write-Host "[2/6] Skipping infrastructure (--SkipInfra)..." -ForegroundColor Gray

    # Get existing VM IP
    $vmIp = az vm list-ip-addresses `
        --resource-group $ResourceGroup `
        --name "sorcha-n1-vm" `
        --query "[0].virtualMachine.network.publicIpAddresses[0].ipAddress" `
        --output tsv 2>$null

    if ([string]::IsNullOrEmpty($vmIp)) {
        Write-Error "Cannot find VM in resource group '$ResourceGroup'. Run without -SkipInfra first."
        exit 1
    }

    Write-Host "  ✓ Existing VM: $vmIp" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# Wait for cloud-init
# ============================================================================
Write-Host "[3/6] Waiting for VM to be ready (cloud-init)..." -ForegroundColor Yellow

$maxAttempts = 30
$attempt = 0
$sshReady = $false

while ($attempt -lt $maxAttempts -and -not $sshReady) {
    $attempt++
    try {
        $result = ssh -o StrictHostKeyChecking=no -o ConnectTimeout=5 -o BatchMode=yes `
            "${AdminUsername}@${vmIp}" "test -f /opt/sorcha/.cloud-init-complete && echo ready || echo waiting" 2>$null

        if ($result -eq "ready") {
            $sshReady = $true
            Write-Host "  ✓ VM is ready (cloud-init complete)" -ForegroundColor Green
        }
        else {
            Write-Host "  Cloud-init still running... ($attempt/$maxAttempts)" -ForegroundColor Gray
            Start-Sleep -Seconds 10
        }
    }
    catch {
        Write-Host "  SSH not ready yet... ($attempt/$maxAttempts)" -ForegroundColor Gray
        Start-Sleep -Seconds 10
    }
}

if (-not $sshReady) {
    Write-Warning "Cloud-init may still be running. Continuing anyway..."
}

Write-Host ""

# ============================================================================
# Upload compose files and scripts
# ============================================================================
Write-Host "[4/6] Uploading compose files and scripts..." -ForegroundColor Yellow

$filesToUpload = @(
    @{Local = "$repoRoot\docker-compose.yml"; Remote = "/opt/sorcha/docker-compose.yml" },
    @{Local = "$repoRoot\docker-compose.n1.yml"; Remote = "/opt/sorcha/docker-compose.n1.yml" },
    @{Local = "$repoRoot\docker\postgres-init.sql"; Remote = "/opt/sorcha/docker/postgres-init.sql" },
    @{Local = "$repoRoot\docker\certs\aspnetapp.pfx"; Remote = "/opt/sorcha/docker/certs/aspnetapp.pfx" },
    @{Local = "$repoRoot\scripts\n1-setup-remote.sh"; Remote = "/opt/sorcha/n1-setup-remote.sh" }
)

# Create subdirectories on remote
ssh -o StrictHostKeyChecking=no "${AdminUsername}@${vmIp}" "mkdir -p /opt/sorcha/docker/certs"

foreach ($file in $filesToUpload) {
    if (Test-Path $file.Local) {
        Write-Host "  Uploading $($file.Local | Split-Path -Leaf)..." -ForegroundColor Gray
        scp -o StrictHostKeyChecking=no $file.Local "${AdminUsername}@${vmIp}:$($file.Remote)"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to upload $($file.Local)"
            exit 1
        }
    }
    else {
        Write-Warning "File not found, skipping: $($file.Local)"
    }
}

# Make setup script executable
ssh -o StrictHostKeyChecking=no "${AdminUsername}@${vmIp}" "chmod +x /opt/sorcha/n1-setup-remote.sh"

# Create .env file on remote with the public IP
$envContent = @"
# n1.sorcha.dev environment
N1_PUBLIC_IP=$vmIp
INSTALLATION_NAME=n1.sorcha.dev
"@
ssh -o StrictHostKeyChecking=no "${AdminUsername}@${vmIp}" "cat > /opt/sorcha/.env << 'ENVEOF'
$envContent
ENVEOF"

Write-Host "  ✓ All files uploaded" -ForegroundColor Green
Write-Host ""

# ============================================================================
# Run setup on VM
# ============================================================================
if (-not $SkipSetup) {
    Write-Host "[5/6] Running setup on VM..." -ForegroundColor Yellow
    Write-Host ""

    ssh -o StrictHostKeyChecking=no -t "${AdminUsername}@${vmIp}" "cd /opt/sorcha && ./n1-setup-remote.sh"

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Setup script returned non-zero. Check VM logs."
    }
}
else {
    Write-Host "[5/6] Skipping setup (--SkipSetup)..." -ForegroundColor Gray
}

Write-Host ""

# ============================================================================
# Summary
# ============================================================================
Write-Host "[6/6] Deployment complete!" -ForegroundColor Yellow
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   n1.sorcha.dev Deployed Successfully!                   ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  VM IP:             $vmIp" -ForegroundColor White
Write-Host "  SSH:               ssh ${AdminUsername}@${vmIp}" -ForegroundColor White
Write-Host "  API Gateway:       http://${vmIp}" -ForegroundColor White
Write-Host "  Aspire Dashboard:  http://${vmIp}:18888" -ForegroundColor White
Write-Host "  Scalar API Docs:   http://${vmIp}/scalar/" -ForegroundColor White
Write-Host ""
Write-Host "  DNS Setup:" -ForegroundColor Cyan
Write-Host "  Create an A record: n1.sorcha.dev -> $vmIp" -ForegroundColor White
Write-Host ""
Write-Host "  CLI Setup:" -ForegroundColor Cyan
Write-Host "  sorcha config set activeProfile n1" -ForegroundColor White
Write-Host "  sorcha auth login --profile n1" -ForegroundColor White
Write-Host ""
Write-Host "  Reset all data:" -ForegroundColor Cyan
Write-Host "  .\scripts\n1-reset.ps1" -ForegroundColor White
Write-Host ""
Write-Host "  Auto-shutdown:" -ForegroundColor Cyan
Write-Host "  VM auto-shuts down at 11 PM GMT to save costs" -ForegroundColor White
Write-Host "  Start it with: az vm start -g $ResourceGroup -n sorcha-n1-vm" -ForegroundColor White
Write-Host ""
