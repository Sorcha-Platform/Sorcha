# SPDX-License-Identifier: MIT
# Copyright (c) 2025 Sorcha Contributors

<#
.SYNOPSIS
    Build and push Sorcha Docker images to DockerHub

.DESCRIPTION
    This script builds all Sorcha service Docker images, tags them for DockerHub,
    and pushes them to your DockerHub repository.

.PARAMETER DockerHubUser
    Your DockerHub username or organization name (required)

.PARAMETER Tag
    Version tag for the images (default: latest)

.PARAMETER Services
    Specific services to push (optional, defaults to all services)
    Valid values: blueprint, wallet, register, tenant, peer, validator, gateway

.PARAMETER SkipBuild
    Skip building images and only tag/push existing local images

.PARAMETER DryRun
    Show what would be done without actually pushing

.EXAMPLE
    .\push-to-dockerhub.ps1 -DockerHubUser "myusername"
    # Builds and pushes all services with 'latest' tag

.EXAMPLE
    .\push-to-dockerhub.ps1 -DockerHubUser "myorg" -Tag "v1.0.0"
    # Pushes all services with version tag 'v1.0.0'

.EXAMPLE
    .\push-to-dockerhub.ps1 -DockerHubUser "myusername" -Services blueprint,wallet -SkipBuild
    # Only pushes blueprint and wallet services without rebuilding

.EXAMPLE
    .\push-to-dockerhub.ps1 -DockerHubUser "myusername" -DryRun
    # Shows what would be pushed without actually doing it
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Your DockerHub username or organization")]
    [string]$DockerHubUser,

    [Parameter(Mandatory=$false)]
    [string]$Tag = "latest",

    [Parameter(Mandatory=$false)]
    [ValidateSet("blueprint", "wallet", "register", "tenant", "peer", "validator", "gateway", "ui", "all")]
    [string[]]$Services = @("all"),

    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory=$false)]
    [switch]$DryRun
)

# Service definitions (key = CLI name, value = image name on DockerHub)
$serviceMap = @{
    "blueprint" = "blueprint-service"
    "wallet" = "wallet-service"
    "register" = "register-service"
    "tenant" = "tenant-service"
    "peer" = "peer-service"
    "validator" = "validator-service"
    "gateway" = "api-gateway"
    "ui" = "ui-web"
}

# Compose service names (when different from image name)
$composeNameMap = @{
    "ui" = "sorcha-ui-web"
}

# Determine which services to process
if ($Services -contains "all") {
    $servicesToPush = $serviceMap.Keys
} else {
    $servicesToPush = $Services
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Sorcha Docker Image Push Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "DockerHub User: $DockerHubUser" -ForegroundColor Yellow
Write-Host "Tag: $Tag" -ForegroundColor Yellow
Write-Host "Services: $($servicesToPush -join ', ')" -ForegroundColor Yellow
Write-Host "Skip Build: $SkipBuild" -ForegroundColor Yellow
Write-Host "Dry Run: $DryRun" -ForegroundColor Yellow
Write-Host ""

# Check if Docker is running
try {
    docker version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker is not running"
    }
} catch {
    Write-Host "❌ Error: Docker is not running or not installed" -ForegroundColor Red
    exit 1
}

# Check if user is logged into DockerHub
Write-Host "Checking DockerHub authentication..." -ForegroundColor Cyan
$dockerInfo = docker info 2>&1 | Out-String
if ($dockerInfo -notmatch "Username") {
    Write-Host "⚠️  You are not logged into DockerHub" -ForegroundColor Yellow
    Write-Host "Please run: docker login" -ForegroundColor Yellow

    if (-not $DryRun) {
        $response = Read-Host "Would you like to login now? (y/n)"
        if ($response -eq "y") {
            docker login
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ Docker login failed" -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "❌ Cannot proceed without authentication" -ForegroundColor Red
            exit 1
        }
    }
} else {
    Write-Host "✅ Already authenticated with DockerHub" -ForegroundColor Green
}

Write-Host ""

# Function to build, tag, and push a service
function Push-Service {
    param(
        [string]$ServiceKey,
        [string]$ServiceName
    )

    # Resolve compose service name (may differ from image name)
    $composeName = if ($composeNameMap.ContainsKey($ServiceKey)) { $composeNameMap[$ServiceKey] } else { $ServiceName }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Processing: $ServiceName (compose: $composeName)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $localImage = "sorchadev/${ServiceName}:latest"
    $remoteImage = "${DockerHubUser}/${ServiceName}:${Tag}"

    # Build the image
    if (-not $SkipBuild) {
        Write-Host "🔨 Building image: $localImage" -ForegroundColor Yellow

        if ($DryRun) {
            Write-Host "   [DRY RUN] Would execute: docker compose build $composeName" -ForegroundColor Gray
        } else {
            docker compose build $composeName

            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ Failed to build $ServiceName" -ForegroundColor Red
                return $false
            }
            Write-Host "✅ Build successful" -ForegroundColor Green
        }
    } else {
        Write-Host "⏭️  Skipping build (using existing local image)" -ForegroundColor Yellow

        # Check if local image exists
        $imageExists = docker images -q $localImage
        if (-not $imageExists -and -not $DryRun) {
            Write-Host "❌ Local image not found: $localImage" -ForegroundColor Red
            Write-Host "   Run without -SkipBuild to build the image first" -ForegroundColor Yellow
            return $false
        }
    }

    # Tag the image for DockerHub
    Write-Host "🏷️  Tagging image: $localImage -> $remoteImage" -ForegroundColor Yellow

    if ($DryRun) {
        Write-Host "   [DRY RUN] Would execute: docker tag $localImage $remoteImage" -ForegroundColor Gray
    } else {
        docker tag $localImage $remoteImage

        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Failed to tag $ServiceName" -ForegroundColor Red
            return $false
        }
        Write-Host "✅ Tagged successfully" -ForegroundColor Green
    }

    # Push to DockerHub
    Write-Host "📤 Pushing image: $remoteImage" -ForegroundColor Yellow

    if ($DryRun) {
        Write-Host "   [DRY RUN] Would execute: docker push $remoteImage" -ForegroundColor Gray
    } else {
        docker push $remoteImage

        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Failed to push $ServiceName" -ForegroundColor Red
            return $false
        }
        Write-Host "✅ Pushed successfully" -ForegroundColor Green
    }

    # Also tag and push as 'latest' if using a version tag
    if ($Tag -ne "latest") {
        $latestImage = "${DockerHubUser}/${ServiceName}:latest"

        Write-Host "🏷️  Also tagging as latest: $latestImage" -ForegroundColor Yellow

        if ($DryRun) {
            Write-Host "   [DRY RUN] Would execute: docker tag $localImage $latestImage" -ForegroundColor Gray
            Write-Host "   [DRY RUN] Would execute: docker push $latestImage" -ForegroundColor Gray
        } else {
            docker tag $localImage $latestImage
            docker push $latestImage

            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Latest tag pushed successfully" -ForegroundColor Green
            } else {
                Write-Host "⚠️  Warning: Failed to push latest tag" -ForegroundColor Yellow
            }
        }
    }

    Write-Host ""
    return $true
}

# Process each service
$successCount = 0
$failureCount = 0
$results = @()

foreach ($serviceKey in $servicesToPush) {
    $serviceName = $serviceMap[$serviceKey]

    if (-not $serviceName) {
        Write-Host "❌ Unknown service: $serviceKey" -ForegroundColor Red
        $failureCount++
        continue
    }

    $success = Push-Service -ServiceKey $serviceKey -ServiceName $serviceName

    $results += [PSCustomObject]@{
        Service = $serviceName
        Status = if ($success) { "✅ Success" } else { "❌ Failed" }
        Image = "${DockerHubUser}/${serviceName}:${Tag}"
    }

    if ($success) {
        $successCount++
    } else {
        $failureCount++
    }
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$results | Format-Table -AutoSize

Write-Host ""
Write-Host "Total Services: $($servicesToPush.Count)" -ForegroundColor White
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Failed: $failureCount" -ForegroundColor $(if ($failureCount -gt 0) { "Red" } else { "Green" })

if ($DryRun) {
    Write-Host ""
    Write-Host "ℹ️  This was a dry run. No images were actually pushed." -ForegroundColor Cyan
    Write-Host "   Remove -DryRun parameter to push for real." -ForegroundColor Cyan
}

Write-Host ""

if ($failureCount -eq 0) {
    Write-Host "✅ All images pushed successfully!" -ForegroundColor Green

    if (-not $DryRun) {
        Write-Host ""
        Write-Host "Your images are now available at:" -ForegroundColor Cyan
        foreach ($result in $results) {
            Write-Host "  https://hub.docker.com/r/$($result.Image -replace ':.*$', '')" -ForegroundColor Blue
        }
    }

    exit 0
} else {
    Write-Host "❌ Some images failed to push" -ForegroundColor Red
    exit 1
}
