#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Deploy PeerRouter to Azure Container Apps
# Manually called from a local machine — experimental/development only.
#
# Usage:
#   pwsh infra/deploy-peer-router.ps1              # Build, push, update
#   pwsh infra/deploy-peer-router.ps1 -Status      # Check current status
#   pwsh infra/deploy-peer-router.ps1 -Logs         # Tail recent logs
#   pwsh infra/deploy-peer-router.ps1 -EnableRelay  # Deploy with relay mode ON

param(
    [string]$ResourceGroup = $env:AZURE_RESOURCE_GROUP,
    [string]$RegistryName = $env:CONTAINER_REGISTRY,
    [string]$AppName = 'peer-router',
    [string]$ImageTag = 'latest',
    [switch]$EnableRelay,
    [switch]$Status,
    [switch]$Logs,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($ResourceGroup)) { $ResourceGroup = 'sorcha-uksouth-rg' }
if ([string]::IsNullOrEmpty($RegistryName)) { $RegistryName = 'sorchauksouth' }

$AcrServer = "$RegistryName.azurecr.io"
$FullImageName = "$AcrServer/peer-router:$ImageTag"

# ============================================================================
# Status check
# ============================================================================
if ($Status) {
    Write-Host "`n=== PeerRouter Status ===" -ForegroundColor Cyan
    az containerapp show --name $AppName --resource-group $ResourceGroup `
        --query "{Name:name, Status:properties.runningStatus, FQDN:properties.configuration.ingress.fqdn, Replicas:properties.template.scale}" `
        --output table

    $fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup `
        --query "properties.configuration.ingress.fqdn" -o tsv

    if ($fqdn) {
        Write-Host "`nHealth check: " -NoNewline
        try {
            $health = Invoke-RestMethod -Uri "https://$fqdn/health" -TimeoutSec 10
            Write-Host "HEALTHY" -ForegroundColor Green
        }
        catch {
            Write-Host "UNHEALTHY — $($_.Exception.Message)" -ForegroundColor Red
        }

        Write-Host "Peers endpoint: " -NoNewline
        try {
            $peers = Invoke-RestMethod -Uri "https://$fqdn/peers" -TimeoutSec 10
            $count = ($peers | Measure-Object).Count
            Write-Host "$count peers registered" -ForegroundColor Green
        }
        catch {
            Write-Host "unavailable" -ForegroundColor Yellow
        }
    }
    exit 0
}

# ============================================================================
# Logs
# ============================================================================
if ($Logs) {
    Write-Host "`n=== PeerRouter Logs ===" -ForegroundColor Cyan
    az containerapp logs show --name $AppName --resource-group $ResourceGroup --tail 50 --follow $false
    exit 0
}

# ============================================================================
# Prerequisites
# ============================================================================
Write-Host "`n=== PeerRouter Deploy ===" -ForegroundColor Cyan
Write-Host "  Resource Group: $ResourceGroup"
Write-Host "  Registry:       $AcrServer"
Write-Host "  App:            $AppName"
Write-Host "  Image:          $FullImageName"
Write-Host "  Relay:          $(if ($EnableRelay) { 'ENABLED' } else { 'unchanged' })"
Write-Host ""

# Check Azure login
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Not logged in to Azure. Logging in..." -ForegroundColor Yellow
    az login
}
Write-Host "Logged in as: $($account.user.name)" -ForegroundColor Green

# ============================================================================
# Build
# ============================================================================
if (-not $SkipBuild) {
    Write-Host "`n--- Building Docker image ---" -ForegroundColor Cyan
    docker build -t $FullImageName -f src/Apps/Sorcha.PeerRouter/Dockerfile .

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build successful" -ForegroundColor Green
}

# ============================================================================
# Push to ACR
# ============================================================================
Write-Host "`n--- Pushing to Azure Container Registry ---" -ForegroundColor Cyan
az acr login --name $RegistryName

if ($LASTEXITCODE -ne 0) {
    Write-Host "ACR login failed" -ForegroundColor Red
    exit 1
}

docker push $FullImageName

if ($LASTEXITCODE -ne 0) {
    Write-Host "Push failed" -ForegroundColor Red
    exit 1
}
Write-Host "Push successful" -ForegroundColor Green

# ============================================================================
# Update Container App
# ============================================================================
Write-Host "`n--- Updating Container App ---" -ForegroundColor Cyan

$updateArgs = @(
    'containerapp', 'update',
    '--name', $AppName,
    '--resource-group', $ResourceGroup,
    '--image', $FullImageName
)

if ($EnableRelay) {
    $updateArgs += '--set-env-vars'
    $updateArgs += 'PEERROUTER__ENABLE_RELAY=true'
}

az @updateArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Container App update failed" -ForegroundColor Red
    exit 1
}
Write-Host "Container App updated" -ForegroundColor Green

# ============================================================================
# Health check
# ============================================================================
Write-Host "`n--- Waiting for deployment (30s) ---" -ForegroundColor Cyan
Start-Sleep -Seconds 30

$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup `
    --query "properties.configuration.ingress.fqdn" -o tsv

if ($fqdn) {
    Write-Host "URL: https://$fqdn" -ForegroundColor White

    $retries = 3
    for ($i = 1; $i -le $retries; $i++) {
        try {
            $health = Invoke-RestMethod -Uri "https://$fqdn/health" -TimeoutSec 10
            Write-Host "Health check: HEALTHY" -ForegroundColor Green

            $peers = Invoke-RestMethod -Uri "https://$fqdn/peers" -TimeoutSec 10
            $count = ($peers | Measure-Object).Count
            Write-Host "Peers: $count registered" -ForegroundColor Green
            break
        }
        catch {
            if ($i -lt $retries) {
                Write-Host "Health check attempt $i/$retries failed, retrying in 15s..." -ForegroundColor Yellow
                Start-Sleep -Seconds 15
            }
            else {
                Write-Host "Health check failed after $retries attempts" -ForegroundColor Red
            }
        }
    }
}

Write-Host "`n=== Deploy Complete ===" -ForegroundColor Cyan
