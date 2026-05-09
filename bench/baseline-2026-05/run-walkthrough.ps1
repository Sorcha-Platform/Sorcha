# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Runs one walkthrough N times against an already-running, already-bootstrapped
# Sorcha stack with benchmark-mode validator. Resets the telemetry between
# runs and flushes a JSON snapshot per run into walkthrough-runs/{name}/.
#
# Discards the first $WarmupRuns runs (default 10) — JIT, cache warmup, etc.
# Concurrent mode (-Concurrency >1) launches N parallel walkthrough processes,
# flushes ONCE at the end (the runs share a per-validator telemetry collector
# so per-run isolation is impossible under concurrency).

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Walkthrough,

    [Parameter(Mandatory)]
    [string] $RunCommand,

    [int] $TotalRuns = 100,

    [int] $WarmupRuns = 10,

    [int] $Concurrency = 1,

    [string] $ValidatorBaseUrl = "http://localhost:5004",

    [string] $ServiceToken = $env:SORCHA_BENCH_SERVICE_TOKEN,

    [Parameter(Mandatory)]
    [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ServiceToken)) {
    throw "SORCHA_BENCH_SERVICE_TOKEN env var (or -ServiceToken) is required to call /api/internal/benchmark/*"
}

$headers = @{ "Authorization" = "Bearer $ServiceToken" }

function Invoke-BenchEndpoint {
    param([string] $Path, [string] $Method = "POST")
    $uri = "$ValidatorBaseUrl$Path"
    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ErrorAction Stop
}

$walkthroughDir = Join-Path $OutputRoot $Walkthrough
New-Item -ItemType Directory -Force -Path $walkthroughDir | Out-Null

Write-Host "[$Walkthrough] Verifying benchmark mode is enabled..." -ForegroundColor Cyan
$status = Invoke-BenchEndpoint "/api/internal/benchmark/status" "GET"
if (-not $status.enabled) {
    throw "Benchmark mode is OFF on $ValidatorBaseUrl. Apply docker-compose.benchmark.yml first."
}

Write-Host "[$Walkthrough] Resetting telemetry collector..." -ForegroundColor Cyan
Invoke-BenchEndpoint "/api/internal/benchmark/reset" | Out-Null

if ($Concurrency -le 1) {
    # Sequential mode — flush per run for per-run distributions
    for ($i = 1; $i -le $TotalRuns; $i++) {
        $runLabel = "{0:D3}" -f $i
        $isWarmup = $i -le $WarmupRuns
        Write-Host "[$Walkthrough] run $runLabel/$TotalRuns $(if ($isWarmup) { '(warmup)' } else { '' })..." -ForegroundColor DarkGray
        Invoke-BenchEndpoint "/api/internal/benchmark/reset" | Out-Null

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & pwsh -NoProfile -Command $RunCommand 2>&1 | Out-Null
        $exit = $LASTEXITCODE
        $sw.Stop()

        $snapshot = Invoke-BenchEndpoint "/api/internal/benchmark/flush" "POST"
        if ($isWarmup) {
            continue
        }

        $outFile = Join-Path $walkthroughDir "seq-$runLabel.json"
        $envelope = [ordered]@{
            walkthrough  = $Walkthrough
            mode         = "sequential"
            run          = $i
            wallTimeMs   = $sw.ElapsedMilliseconds
            walkthroughExitCode = $exit
            telemetry    = $snapshot
        } | ConvertTo-Json -Depth 64
        Set-Content -Path $outFile -Value $envelope -Encoding UTF8
    }
}
else {
    # Concurrent mode — drain warmup serially first, then concurrent burst, single flush.
    for ($i = 1; $i -le $WarmupRuns; $i++) {
        Write-Host "[$Walkthrough] warmup $i/$WarmupRuns..." -ForegroundColor DarkGray
        & pwsh -NoProfile -Command $RunCommand 2>&1 | Out-Null
    }

    Invoke-BenchEndpoint "/api/internal/benchmark/reset" | Out-Null
    Write-Host "[$Walkthrough] concurrent burst N=$Concurrency, $($TotalRuns - $WarmupRuns) total runs..." -ForegroundColor Cyan

    $remaining = $TotalRuns - $WarmupRuns
    $batches = [Math]::Ceiling($remaining / $Concurrency)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    for ($b = 0; $b -lt $batches; $b++) {
        $batchSize = [Math]::Min($Concurrency, $remaining - $b * $Concurrency)
        $jobs = @()
        for ($k = 0; $k -lt $batchSize; $k++) {
            $jobs += Start-Job -ScriptBlock { param($cmd) & pwsh -NoProfile -Command $cmd } -ArgumentList $RunCommand
        }
        Wait-Job -Job $jobs | Out-Null
        Remove-Job -Job $jobs | Out-Null
    }

    $sw.Stop()

    $snapshot = Invoke-BenchEndpoint "/api/internal/benchmark/flush" "POST"
    $outFile = Join-Path $walkthroughDir "concurrent-N$Concurrency.json"
    $envelope = [ordered]@{
        walkthrough        = $Walkthrough
        mode               = "concurrent"
        concurrency        = $Concurrency
        runs               = $remaining
        totalWallTimeMs    = $sw.ElapsedMilliseconds
        throughputRunsPerSec = if ($sw.Elapsed.TotalSeconds -gt 0) { $remaining / $sw.Elapsed.TotalSeconds } else { 0 }
        telemetry          = $snapshot
    } | ConvertTo-Json -Depth 64
    Set-Content -Path $outFile -Value $envelope -Encoding UTF8
}

Write-Host "[$Walkthrough] done — wrote results to $walkthroughDir" -ForegroundColor Green
