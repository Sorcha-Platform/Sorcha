#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# System Specifications Capture
# Records host and container specs for performance comparison

$ErrorActionPreference = "Stop"

$specsFile = Join-Path $PSScriptRoot "SYSTEM-SPECS.md"

Write-Host "Capturing system specifications..." -ForegroundColor Cyan

# ============================================================================
# Host System Information
# ============================================================================

$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$memory = Get-CimInstance Win32_PhysicalMemory | Measure-Object -Property Capacity -Sum
$disk = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq "C:" }

# Docker info
$dockerVersion = (docker version --format '{{.Server.Version}}' 2>$null) -join ""
$dockerInfo = docker info --format json 2>$null | ConvertFrom-Json

# ============================================================================
# Container Information
# ============================================================================

$containers = docker ps --format json 2>$null | ConvertFrom-Json
$containerStats = @{}

foreach ($container in $containers) {
    $name = $container.Names
    $stats = docker stats --no-stream --format json $name 2>$null | ConvertFrom-Json

    if ($stats) {
        $containerStats[$name] = @{
            CPU = $stats.CPUPerc
            Memory = $stats.MemUsage
            MemLimit = $stats.MemPerc
            NetIO = $stats.NetIO
            BlockIO = $stats.BlockIO
        }
    }
}

# ============================================================================
# Generate Markdown Report
# ============================================================================

$report = @"
# System Specifications - Performance Benchmark

**Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Profile:** Performance Benchmark Walkthrough

---

## Host System

### Hardware

| Component | Specification |
|-----------|---------------|
| **CPU** | $($cpu.Name.Trim()) |
| **Cores** | $($cpu.NumberOfCores) physical, $($cpu.NumberOfLogicalProcessors) logical |
| **Base Clock** | $($cpu.MaxClockSpeed) MHz |
| **Total RAM** | $([Math]::Round($memory.Sum / 1GB, 2)) GB |
| **Available RAM** | $([Math]::Round($os.FreePhysicalMemory / 1MB, 2)) GB |

### Operating System

| Component | Specification |
|-----------|---------------|
| **OS** | $($os.Caption) |
| **Version** | $($os.Version) |
| **Build** | $($os.BuildNumber) |
| **Architecture** | $($os.OSArchitecture) |

### Storage (C: Drive)

| Component | Specification |
|-----------|---------------|
| **Total Size** | $([Math]::Round($disk.Size / 1GB, 2)) GB |
| **Free Space** | $([Math]::Round($disk.FreeSpace / 1GB, 2)) GB |
| **File System** | $($disk.FileSystem) |

---

## Docker Environment

### Docker Version

| Component | Version |
|-----------|---------|
| **Docker Engine** | $dockerVersion |
| **Total Containers** | $($dockerInfo.Containers) |
| **Running** | $($dockerInfo.ContainersRunning) |
| **Paused** | $($dockerInfo.ContainersPaused) |
| **Stopped** | $($dockerInfo.ContainersStopped) |

### Docker Resources

| Resource | Allocation |
|----------|------------|
| **CPUs** | $($dockerInfo.NCPU) |
| **Total Memory** | $([Math]::Round($dockerInfo.MemTotal / 1GB, 2)) GB |
| **Driver** | $($dockerInfo.Driver) |
| **Storage Driver** | $($dockerInfo.StorageDriver) |

---

## Running Containers (at time of capture)

"@

if ($containerStats.Count -gt 0) {
    $report += @"

| Container | CPU | Memory Usage | Network I/O | Block I/O |
|-----------|-----|--------------|-------------|-----------|

"@

    foreach ($name in $containerStats.Keys | Sort-Object) {
        $stats = $containerStats[$name]
        $report += "| $name | $($stats.CPU) | $($stats.Memory) | $($stats.NetIO) | $($stats.BlockIO) |`n"
    }
} else {
    $report += "`n*No containers running at time of capture*`n"
}

$report += @"

---

## Performance Characteristics

### Expected Baseline Performance

Based on hardware specifications:

| Metric | Conservative | Target | Notes |
|--------|-------------|--------|-------|
| **Register TPS** | 50-100 | 100-200 | Depends on payload size |
| **API Latency (P95)** | <500ms | <200ms | 5KB payloads via gateway |
| **Max Concurrent Workers** | 10 | 25 | Before significant degradation |
| **MongoDB Write TPS** | 100-500 | 500-1000 | Document insert rate |

### Bottleneck Predictions

| Component | Likely Constraint | Mitigation |
|-----------|------------------|------------|
| **CPU** | $($cpu.NumberOfLogicalProcessors) logical cores | Optimize signature verification, parallel processing |
| **Memory** | $([Math]::Round($memory.Sum / 1GB, 2)) GB RAM | Connection pooling, efficient caching |
| **Disk I/O** | Docker volumes | Use SSD, optimize MongoDB indexes |
| **Network** | Docker bridge | Direct service calls, compression |

---

## Benchmark Environment

### Test Configuration

- **Profile:** Gateway (through API Gateway on port 80)
- **Test Duration:** 15-20 minutes (full suite)
- **Payload Sizes:** 1KB, 10KB, 50KB, 100KB, 500KB, 1MB
- **Concurrency Levels:** 1, 5, 10, 25, 50 workers
- **Services Tested:** Register Service, API Gateway, Wallet Service, Validator Service

### Network Topology

``````
[Test Script] → [API Gateway :80] → [Services :5000-5999]
                     ↓
                [MongoDB :27017]
                [PostgreSQL :5432]
                [Redis :6379]
``````

---

## Reproducibility

To reproduce this benchmark on a comparable system:

1. **Minimum Requirements:**
   - CPU: 4+ cores
   - RAM: 8+ GB
   - Disk: 20+ GB free SSD storage
   - Docker Desktop with 4GB+ RAM allocation

2. **Setup:**
   ``````bash
   docker-compose up -d
   ./walkthroughs/PerformanceBenchmark/bootstrap-perf-org.ps1
   ./walkthroughs/PerformanceBenchmark/test-performance.ps1
   ``````

3. **Compare Results:**
   - Normalize TPS by CPU core count
   - Compare P95 latencies at same payload sizes
   - Analyze degradation patterns

---

**Captured by:** capture-system-specs.ps1
**Location:** $specsFile
"@

# Save report
$report | Out-File -FilePath $specsFile -Encoding UTF8

Write-Host "✓ System specifications captured" -ForegroundColor Green
Write-Host "  File: $specsFile" -ForegroundColor White

# Also output to console
Write-Host ""
Write-Host "=== Host Summary ===" -ForegroundColor Cyan
Write-Host "  CPU: $($cpu.Name.Trim())" -ForegroundColor White
Write-Host "  Cores: $($cpu.NumberOfCores) physical / $($cpu.NumberOfLogicalProcessors) logical" -ForegroundColor White
Write-Host "  RAM: $([Math]::Round($memory.Sum / 1GB, 2)) GB" -ForegroundColor White
Write-Host "  OS: $($os.Caption) $($os.Version)" -ForegroundColor White
Write-Host "  Docker: v$dockerVersion ($($dockerInfo.ContainersRunning) containers running)" -ForegroundColor White
Write-Host ""
