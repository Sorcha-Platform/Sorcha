#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Resource Monitoring Script
# Monitors Docker container CPU, memory, and network usage during performance tests

param(
    [Parameter(Mandatory=$false)]
    [int]$Duration = 60,

    [Parameter(Mandatory=$false)]
    [int]$Interval = 5,

    [Parameter(Mandatory=$false)]
    [string]$OutputFile = "resource-usage.csv"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Resource Monitoring" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[INFO] Monitoring Docker containers for $Duration seconds (interval: $Interval seconds)" -ForegroundColor Yellow
Write-Host "[INFO] Output: $OutputFile" -ForegroundColor Yellow
Write-Host ""

# Containers to monitor
$containers = @(
    "sorcha-api-gateway-1",
    "sorcha-register-service-1",
    "sorcha-blueprint-service-1",
    "sorcha-tenant-service-1",
    "sorcha-wallet-service-1",
    "sorcha-validator-service-1",
    "sorcha-register-mongodb-1",
    "sorcha-redis-1"
)

# CSV header
$csvData = @()
$csvData += "Timestamp,Container,CPU%,MemUsage,MemLimit,MemPercent,NetI/O,BlockI/O"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$iteration = 0

while ($stopwatch.Elapsed.TotalSeconds -lt $Duration) {
    $iteration++
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Collecting metrics (iteration $iteration)..." -ForegroundColor Gray

    foreach ($container in $containers) {
        try {
            # Get container stats (single shot, no streaming)
            $stats = docker stats --no-stream --format "{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}},{{.BlockIO}}" $container 2>$null

            if ($stats) {
                $csvData += "$timestamp,$container,$stats"

                # Parse and display
                $parts = $stats -split ','
                Write-Host "  $container : CPU=$($parts[0]) | Mem=$($parts[1]) ($($parts[2])) | Net=$($parts[3])" -ForegroundColor White
            }
        }
        catch {
            Write-Host "  $container : [Not Running]" -ForegroundColor Red
        }
    }

    Write-Host ""

    # Wait for next interval
    $elapsed = $stopwatch.Elapsed.TotalSeconds
    if ($elapsed -lt $Duration) {
        $remaining = [Math]::Min($Interval, $Duration - $elapsed)
        Start-Sleep -Seconds $remaining
    }
}

$stopwatch.Stop()

# Save CSV
$csvData | Set-Content -Path $OutputFile
Write-Host "[SUCCESS] Resource monitoring complete" -ForegroundColor Green
Write-Host "[INFO] Data saved to: $OutputFile" -ForegroundColor Yellow
Write-Host ""

# Summary statistics
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Duration: $($stopwatch.Elapsed.TotalSeconds.ToString('F2')) seconds" -ForegroundColor White
Write-Host "  Iterations: $iteration" -ForegroundColor White
Write-Host "  Output: $OutputFile" -ForegroundColor White
Write-Host ""
