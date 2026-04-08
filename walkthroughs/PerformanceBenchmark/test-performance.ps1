#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PerformanceBenchmark — Run
# Measures Register Service performance: payload sizes, throughput, latency, concurrency, docket building.
# Reads org/wallet/URLs from state.json produced by setup.ps1.

param(
    [switch]$QuickTest,
    [string]$MaxPayloadSize = "1MB",
    [int]$Iterations = 100,
    [int]$Concurrency = 25,
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PerformanceBenchmark — Run"

# Load state from setup.ps1
$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($state.adminToken)" }

# Results directory
$ResultsDir = Join-Path $PSScriptRoot "results"
$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$ResultFile = Join-Path $ResultsDir "benchmark-$Timestamp.json"
$SummaryFile = Join-Path $ResultsDir "summary-$Timestamp.md"
if (-not (Test-Path $ResultsDir)) { New-Item -Path $ResultsDir -ItemType Directory | Out-Null }

# Test configuration
$TestConfig = @{
    PayloadSizes = if ($QuickTest) { @("1KB", "10KB", "50KB") } else { @("1KB", "10KB", "50KB", "100KB", "500KB", $MaxPayloadSize) }
    ThroughputDuration = if ($QuickTest) { 30 } else { 60 }
    LatencyIterations = if ($QuickTest) { 50 } else { $Iterations }
    ConcurrencyLevels = if ($QuickTest) { @(1, 5, 10) } else { @(1, 5, 10, 25, $Concurrency) }
    DocketSizes = if ($QuickTest) { @(10, 50) } else { @(10, 50, 100, 500) }
}

# Results storage
$Script:Results = @{
    Timestamp   = $Timestamp
    Profile     = $state.profile
    Environment = @{
        OS         = [System.Environment]::OSVersion.VersionString
        PowerShell = $PSVersionTable.PSVersion.ToString()
        Docker     = ""
    }
    Tests = @{
        PayloadSize    = @()
        Throughput     = @()
        Latency        = @()
        Concurrency    = @()
        DocketBuilding = @()
    }
    Summary = @{
        TotalTests  = 0
        TotalErrors = 0
        Duration    = 0
    }
}

# ============================================================================
# Specialized Performance Helpers
# ============================================================================

function Write-Metric {
    param([string]$Name, [string]$Value, [string]$Unit = "")
    $displayValue = if ($Unit) { "$Value $Unit" } else { $Value }
    Write-Host "    $Name : " -NoNewline -ForegroundColor Gray
    Write-Host $displayValue -ForegroundColor Cyan
}

function ConvertTo-Bytes {
    param([string]$Size)
    if ($Size -match '^(\d+(?:\.\d+)?)\s*(KB|MB|GB)?$') {
        $value = [double]$Matches[1]
        switch ($Matches[2]) {
            "KB" { return [int]($value * 1024) }
            "MB" { return [int]($value * 1024 * 1024) }
            "GB" { return [int]($value * 1024 * 1024 * 1024) }
            default { return [int]$value }
        }
    }
    return 1024
}

function Get-Statistics {
    param([double[]]$Values)
    if ($Values.Count -eq 0) {
        return @{ Min = 0; Max = 0; Mean = 0; Median = 0; P95 = 0; P99 = 0; StdDev = 0; Count = 0 }
    }
    $sorted = $Values | Sort-Object
    $count = $sorted.Count
    $mean = ($sorted | Measure-Object -Average).Average
    $median = if ($count % 2 -eq 0) { ($sorted[($count/2)-1] + $sorted[$count/2]) / 2 } else { $sorted[[Math]::Floor($count/2)] }
    $p95Index = [Math]::Ceiling($count * 0.95) - 1
    $p99Index = [Math]::Ceiling($count * 0.99) - 1
    $variance = ($sorted | ForEach-Object { [Math]::Pow($_ - $mean, 2) } | Measure-Object -Average).Average
    return @{
        Min    = $sorted[0]
        Max    = $sorted[-1]
        Mean   = $mean
        Median = $median
        P95    = $sorted[$p95Index]
        P99    = $sorted[$p99Index]
        StdDev = [Math]::Sqrt($variance)
        Count  = $count
    }
}

function New-TestTransaction {
    param(
        [string]$RegisterId,
        [string]$WalletAddress,
        [int]$PayloadBytes,
        [string]$Token
    )

    $payload = @{
        testData         = -join ((1..$PayloadBytes) | ForEach-Object { [char](Get-Random -Minimum 65 -Maximum 90) })
        timestamp        = (Get-Date).ToString("o")
        sequence         = Get-Random -Minimum 1 -Maximum 1000000
        payloadSizeBytes = $PayloadBytes
    }

    $payloadJson = $payload | ConvertTo-Json -Compress -Depth 10
    $payloadForRequest = $payloadJson | ConvertFrom-Json
    $finalPayloadJson = $payloadForRequest | ConvertTo-Json -Compress -Depth 10
    $jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($finalPayloadJson)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $payloadHashBytes = $sha256.ComputeHash($jsonBytes)
    $payloadHash = [BitConverter]::ToString($payloadHashBytes).Replace('-', '').ToLowerInvariant()

    $txIdSource = "$RegisterId-$(Get-Date -Format 'o')-$(Get-Random)"
    $txIdBytes = [System.Text.Encoding]::UTF8.GetBytes($txIdSource)
    $txIdHashBytes = $sha256.ComputeHash($txIdBytes)
    $transactionId = [BitConverter]::ToString($txIdHashBytes).Replace('-', '').ToLowerInvariant()

    $dataToSignBase64 = [Convert]::ToBase64String($txIdHashBytes)
    $signBody = @{ transactionData = $dataToSignBase64; isPreHashed = $true } | ConvertTo-Json -Depth 10

    try {
        $signResponse = Invoke-RestMethod -Method Post `
            -Uri "$($state.walletUrl)/v1/wallets/$WalletAddress/sign" `
            -Headers $headers -Body $signBody -ContentType "application/json" -UseBasicParsing
    } catch {
        Write-Host "  ! Signing failed: $($_.Exception.Message)" -ForegroundColor Yellow
        return $null
    }

    return @{
        transactionId = $transactionId
        registerId    = $RegisterId
        blueprintId   = "performance-test-v1"
        actionId      = "1"
        payload       = $payloadForRequest
        payloadHash   = $payloadHash
        signatures    = @(@{
            publicKey      = $signResponse.publicKey
            signatureValue = $signResponse.signature
            algorithm      = if ($signResponse.algorithm) { $signResponse.algorithm } else { "ED25519" }
        })
        createdAt = (Get-Date).ToUniversalTime().ToString("o")
        expiresAt = (Get-Date).AddMinutes(5).ToUniversalTime().ToString("o")
        priority  = 1
        metadata  = @{ source = "performance-benchmark"; payloadSize = "$PayloadBytes" }
    }
}

function Submit-Transaction {
    param([object]$Transaction, [string]$Token)

    $txBody = $Transaction | ConvertTo-Json -Depth 10
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $response = Invoke-RestMethod -Method Post `
            -Uri "$($state.registerUrl)/validator/transactions/validate" `
            -Headers $headers -Body $txBody -ContentType "application/json" -UseBasicParsing
        $stopwatch.Stop()
        return @{ Success = ($response.isValid -eq $true -and $response.added -eq $true); Duration = $stopwatch.Elapsed.TotalMilliseconds; Response = $response }
    } catch {
        $stopwatch.Stop()
        $errorMsg = $_.Exception.Message
        try {
            $errorStream = $_.Exception.Response.GetResponseStream()
            $errorReader = New-Object System.IO.StreamReader($errorStream)
            $errorBody = $errorReader.ReadToEnd()
            if ($errorBody) { $errorMsg = "$errorMsg | Response: $errorBody" }
        } catch {}
        return @{ Success = $false; Duration = $stopwatch.Elapsed.TotalMilliseconds; Error = $errorMsg }
    }
}

# ============================================================================
# Test 1: Payload Size Performance
# ============================================================================

function Test-PayloadSizes {
    param([string]$RegisterId, [string]$WalletAddress)

    Write-WtStep "TEST 1: Payload Size Performance"

    foreach ($sizeStr in $TestConfig.PayloadSizes) {
        Write-WtInfo "Testing payload size: $sizeStr"
        $sizeBytes = ConvertTo-Bytes $sizeStr
        $iters = if ($sizeStr -match "KB") { 100 } elseif ($sizeStr -match "^(500|1)") { 25 } else { 50 }
        if ($QuickTest) { $iters = [Math]::Min($iters, 20) }

        $latencies = @()
        $errors = 0

        for ($i = 1; $i -le $iters; $i++) {
            $tx = New-TestTransaction -RegisterId $RegisterId -WalletAddress $WalletAddress -PayloadBytes $sizeBytes -Token $state.adminToken
            if ($null -eq $tx) { $errors++; continue }
            $result = Submit-Transaction -Transaction $tx -Token $state.adminToken
            if ($result.Success) { $latencies += $result.Duration } else { $errors++ }
            Start-Sleep -Milliseconds 50
        }

        $stats = Get-Statistics $latencies
        Write-WtSuccess "${sizeStr}: $($iters - $errors)/$iters success, mean=$($stats.Mean.ToString('F2'))ms, P95=$($stats.P95.ToString('F2'))ms"

        $Script:Results.Tests.PayloadSize += @{
            PayloadSize = $sizeStr; PayloadBytes = $sizeBytes; Iterations = $iters
            Successful = $iters - $errors; Errors = $errors; Statistics = $stats
        }
        $Script:Results.Summary.TotalTests += $iters
        $Script:Results.Summary.TotalErrors += $errors
    }
}

# ============================================================================
# Test 2: Throughput Testing
# ============================================================================

function Test-Throughput {
    param([string]$RegisterId, [string]$WalletAddress)

    Write-WtStep "TEST 2: Throughput Testing"
    $duration = $TestConfig.ThroughputDuration
    $payloadSize = ConvertTo-Bytes "5KB"
    Write-WtInfo "Sustained throughput ($duration seconds, 5KB payloads)"

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $transactions = 0; $errors = 0; $latencies = @()

    while ($stopwatch.Elapsed.TotalSeconds -lt $duration) {
        $tx = New-TestTransaction -RegisterId $RegisterId -WalletAddress $WalletAddress -PayloadBytes $payloadSize -Token $state.adminToken
        if ($null -eq $tx) { $errors++; continue }
        $result = Submit-Transaction -Transaction $tx -Token $state.adminToken
        $transactions++
        if ($result.Success) { $latencies += $result.Duration } else { $errors++ }
        Start-Sleep -Milliseconds 10
    }

    $stopwatch.Stop()
    $totalSeconds = $stopwatch.Elapsed.TotalSeconds
    $tps = $transactions / $totalSeconds
    $stats = Get-Statistics $latencies

    Write-WtSuccess "Throughput: $($tps.ToString('F2')) TPS, $transactions txs in $($totalSeconds.ToString('F2'))s"
    Write-Metric "Errors" $errors "txs"
    Write-Metric "Mean Latency" "$($stats.Mean.ToString('F2'))" "ms"
    Write-Metric "P95 Latency" "$($stats.P95.ToString('F2'))" "ms"

    $Script:Results.Tests.Throughput += @{
        Duration = $totalSeconds; Transactions = $transactions; Successful = $transactions - $errors
        Errors = $errors; TPS = $tps; Statistics = $stats
    }
    $Script:Results.Summary.TotalTests += $transactions
    $Script:Results.Summary.TotalErrors += $errors
}

# ============================================================================
# Test 3: Latency Benchmarks
# ============================================================================

function Test-Latency {
    param([string]$RegisterId, [string]$WalletAddress)

    Write-WtStep "TEST 3: Latency Benchmarks"

    $scenarios = @(
        @{ Name = "Single-threaded"; Delay = 0 }
        @{ Name = "Light load"; Delay = 100 }
        @{ Name = "Moderate load"; Delay = 50 }
        @{ Name = "Heavy load"; Delay = 10 }
    )

    $payloadSize = ConvertTo-Bytes "5KB"
    $iters = $TestConfig.LatencyIterations

    foreach ($scenario in $scenarios) {
        Write-WtInfo "$($scenario.Name) ($iters iterations)"
        $latencies = @(); $errors = 0

        for ($i = 1; $i -le $iters; $i++) {
            $tx = New-TestTransaction -RegisterId $RegisterId -WalletAddress $WalletAddress -PayloadBytes $payloadSize -Token $state.adminToken
            if ($null -eq $tx) { $errors++; continue }
            $result = Submit-Transaction -Transaction $tx -Token $state.adminToken
            if ($result.Success) { $latencies += $result.Duration } else { $errors++ }
            if ($scenario.Delay -gt 0) { Start-Sleep -Milliseconds $scenario.Delay }
        }

        $stats = Get-Statistics $latencies
        Write-WtSuccess "$($scenario.Name): mean=$($stats.Mean.ToString('F2'))ms, P95=$($stats.P95.ToString('F2'))ms, P99=$($stats.P99.ToString('F2'))ms"

        $Script:Results.Tests.Latency += @{
            Scenario = $scenario.Name; Iterations = $iters; Successful = $iters - $errors
            Errors = $errors; Statistics = $stats
        }
        $Script:Results.Summary.TotalTests += $iters
        $Script:Results.Summary.TotalErrors += $errors
    }
}

# ============================================================================
# Test 4: Concurrency Testing
# ============================================================================

function Test-Concurrency {
    param([string]$RegisterId, [string]$WalletAddress)

    Write-WtStep "TEST 4: Concurrency Testing"

    $payloadSize = ConvertTo-Bytes "5KB"
    $txPerWorker = if ($QuickTest) { 20 } else { 50 }

    foreach ($workers in $TestConfig.ConcurrencyLevels) {
        Write-WtInfo "$workers concurrent workers ($txPerWorker txs each)"

        $totalTxs = $workers * $txPerWorker
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        $jobs = 1..$workers | ForEach-Object {
            Start-Job -ScriptBlock {
                param($RegisterId, $WalletAddress, $Token, $RegisterUrl, $WalletUrl, $Count, $PayloadSize)
                $results = @{ Latencies = @(); Errors = 0 }
                $hdrs = @{ Authorization = "Bearer $Token" }
                for ($i = 1; $i -le $Count; $i++) {
                    try {
                        $payload = @{
                            testData  = -join ((1..$PayloadSize) | ForEach-Object { [char](Get-Random -Minimum 65 -Maximum 90) })
                            timestamp = (Get-Date).ToString("o")
                            sequence  = Get-Random
                        }
                        $tx = @{ registerId = $RegisterId; senderAddress = $WalletAddress; actionType = "TEST_TRANSACTION"; payload = $payload }
                        $sw = [System.Diagnostics.Stopwatch]::StartNew()
                        $null = Invoke-RestMethod -Method Post -Uri "$RegisterUrl/registers/$RegisterId/transactions" `
                            -Headers $hdrs -Body ($tx | ConvertTo-Json -Depth 10) -ContentType "application/json"
                        $sw.Stop()
                        $results.Latencies += $sw.Elapsed.TotalMilliseconds
                    } catch { $results.Errors++ }
                }
                return $results
            } -ArgumentList $RegisterId, $WalletAddress, $state.adminToken, $state.registerUrl, $state.walletUrl, $txPerWorker, $payloadSize
        }

        $jobResults = $jobs | Wait-Job | Receive-Job
        $jobs | Remove-Job
        $stopwatch.Stop()

        $allLatencies = @(); $totalErrors = 0
        foreach ($r in $jobResults) { $allLatencies += $r.Latencies; $totalErrors += $r.Errors }

        $stats = Get-Statistics $allLatencies
        $totalSeconds = $stopwatch.Elapsed.TotalSeconds
        $tps = $totalTxs / $totalSeconds

        Write-WtSuccess "$workers workers: $($tps.ToString('F2')) TPS, $($totalTxs - $totalErrors)/$totalTxs success"
        Write-Metric "Mean Latency" "$($stats.Mean.ToString('F2'))" "ms"
        Write-Metric "P95 Latency" "$($stats.P95.ToString('F2'))" "ms"

        $Script:Results.Tests.Concurrency += @{
            Workers = $workers; TransactionsPerWorker = $txPerWorker; TotalTransactions = $totalTxs
            Duration = $totalSeconds; TPS = $tps; Successful = $totalTxs - $totalErrors
            Errors = $totalErrors; Statistics = $stats
        }
        $Script:Results.Summary.TotalTests += $totalTxs
        $Script:Results.Summary.TotalErrors += $totalErrors

        Start-Sleep -Seconds 2
    }
}

# ============================================================================
# Test 5: Docket Building Performance
# ============================================================================

function Test-DocketBuilding {
    param([string]$RegisterId, [string]$WalletAddress)

    Write-WtStep "TEST 5: Docket Building Performance"
    $payloadSize = ConvertTo-Bytes "2KB"

    foreach ($docketSize in $TestConfig.DocketSizes) {
        Write-WtInfo "Docket with $docketSize transactions"
        $txIds = @()
        $submitStart = Get-Date

        for ($i = 1; $i -le $docketSize; $i++) {
            $tx = New-TestTransaction -RegisterId $RegisterId -WalletAddress $WalletAddress -PayloadBytes $payloadSize -Token $state.adminToken
            if ($null -eq $tx) { continue }
            $result = Submit-Transaction -Transaction $tx -Token $state.adminToken
            if ($result.Success) { $txIds += $result.Response.transactionId }
            Start-Sleep -Milliseconds 20
        }

        $submitTime = ((Get-Date) - $submitStart).TotalMilliseconds

        if ($txIds.Count -gt 0) {
            $submitTps = $txIds.Count / ($submitTime / 1000)
            Write-WtSuccess "Submitted $($txIds.Count) txs in $($submitTime.ToString('F0'))ms ($($submitTps.ToString('F2')) TPS)"
            Write-WtInfo "Docket building occurs automatically via Validator Service"

            $Script:Results.Tests.DocketBuilding += @{
                DocketSize = $docketSize; Submitted = $txIds.Count
                SubmitTime = $submitTime; SubmitTPS = $submitTps
            }
        } else {
            Write-WtWarn "No transactions submitted for docket size $docketSize"
            $Script:Results.Tests.DocketBuilding += @{ DocketSize = $docketSize; Error = "No transactions submitted" }
        }

        Start-Sleep -Seconds 2
    }
}

# ============================================================================
# Report Generation
# ============================================================================

function Export-Results {
    Write-WtStep "Generating Performance Report"

    $Script:Results | ConvertTo-Json -Depth 10 | Set-Content -Path $ResultFile
    Write-WtSuccess "Saved JSON: $ResultFile"

    $summary = @"
# Performance Benchmark Report

**Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Profile:** $($state.profile)
**Environment:** $($Script:Results.Environment.OS)

---

## Summary

| Metric | Value |
|--------|-------|
| Total Tests | $($Script:Results.Summary.TotalTests) |
| Total Errors | $($Script:Results.Summary.TotalErrors) |
| Error Rate | $(if ($Script:Results.Summary.TotalTests -gt 0) { ($Script:Results.Summary.TotalErrors / $Script:Results.Summary.TotalTests * 100).ToString('F2') } else { "0" })% |
| Duration | $($Script:Results.Summary.Duration.ToString('F2')) seconds |

---

## Payload Size Performance

| Size | Iterations | Success Rate | Mean Latency | P95 | P99 |
|------|------------|-------------|--------------|-----|-----|
"@

    foreach ($test in $Script:Results.Tests.PayloadSize) {
        $successRate = if ($test.Iterations -gt 0) { (($test.Successful / $test.Iterations) * 100).ToString('F2') } else { "0" }
        $summary += "`n| $($test.PayloadSize) | $($test.Iterations) | $successRate% | $($test.Statistics.Mean.ToString('F2'))ms | $($test.Statistics.P95.ToString('F2'))ms | $($test.Statistics.P99.ToString('F2'))ms |"
    }

    if ($Script:Results.Tests.Throughput.Count -gt 0) {
        $tp = $Script:Results.Tests.Throughput[0]
        $summary += @"

## Throughput

| Metric | Value |
|--------|-------|
| Duration | $($tp.Duration.ToString('F2'))s |
| Transactions | $($tp.Transactions) |
| TPS | $($tp.TPS.ToString('F2')) |
| Mean Latency | $($tp.Statistics.Mean.ToString('F2'))ms |
| P95 Latency | $($tp.Statistics.P95.ToString('F2'))ms |
"@
    }

    $summary += @"

## Latency Benchmarks

| Scenario | Success Rate | Min | Mean | Median | P95 | P99 | Max |
|----------|-------------|-----|------|--------|-----|-----|-----|
"@

    foreach ($test in $Script:Results.Tests.Latency) {
        $successRate = if ($test.Iterations -gt 0) { (($test.Successful / $test.Iterations) * 100).ToString('F2') } else { "0" }
        $summary += "`n| $($test.Scenario) | $successRate% | $($test.Statistics.Min.ToString('F2'))ms | $($test.Statistics.Mean.ToString('F2'))ms | $($test.Statistics.Median.ToString('F2'))ms | $($test.Statistics.P95.ToString('F2'))ms | $($test.Statistics.P99.ToString('F2'))ms | $($test.Statistics.Max.ToString('F2'))ms |"
    }

    $summary += @"

## Concurrency

| Workers | Total Txs | Duration | TPS | Success Rate | Mean Latency | P95 |
|---------|-----------|----------|-----|-------------|--------------|-----|
"@

    foreach ($test in $Script:Results.Tests.Concurrency) {
        $successRate = if ($test.TotalTransactions -gt 0) { (($test.Successful / $test.TotalTransactions) * 100).ToString('F2') } else { "0" }
        $summary += "`n| $($test.Workers) | $($test.TotalTransactions) | $($test.Duration.ToString('F2'))s | $($test.TPS.ToString('F2')) | $successRate% | $($test.Statistics.Mean.ToString('F2'))ms | $($test.Statistics.P95.ToString('F2'))ms |"
    }

    $summary += @"

## Docket Building

| Size | Submitted | Submit Time | Submit TPS |
|------|-----------|-------------|------------|
"@

    foreach ($test in $Script:Results.Tests.DocketBuilding) {
        if ($test.Error) {
            $summary += "`n| $($test.DocketSize) | - | - | Error: $($test.Error) |"
        } else {
            $summary += "`n| $($test.DocketSize) | $($test.Submitted) | $($test.SubmitTime.ToString('F0'))ms | $($test.SubmitTPS.ToString('F2')) |"
        }
    }

    $summary += @"

---
**Generated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@

    $summary | Set-Content -Path $SummaryFile
    Write-WtSuccess "Saved summary: $SummaryFile"
}

# ============================================================================
# Main Execution
# ============================================================================

Write-WtInfo "Configuration:"
Write-WtInfo "  Profile: $($state.profile)"
Write-WtInfo "  Quick Test: $QuickTest"
Write-WtInfo "  Max Payload: $MaxPayloadSize"
Write-WtInfo "  Iterations: $Iterations"
Write-WtInfo "  Concurrency: $Concurrency"

$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    try {
        $dockerVersion = docker --version
        $Script:Results.Environment.Docker = $dockerVersion
        Write-WtInfo "Docker: $dockerVersion"
    } catch {
        Write-WtFail "Docker not found"
        exit 1
    }

    # Create test register using shared module
    Write-WtStep "Creating test register"
    $register = New-SorchaRegister `
        -RegisterUrl $state.registerUrl -WalletUrl $state.walletUrl `
        -Name "Performance Benchmark Register" -Description "Register for performance benchmarking" `
        -TenantId $state.organizationId -OwnerUserId $state.adminUserId `
        -OwnerWalletAddress $state.walletAddress -Headers $headers
    Write-WtSuccess "Register created: $($register.RegisterId)"

    # Run tests
    Test-PayloadSizes -RegisterId $register.RegisterId -WalletAddress $state.walletAddress
    Test-Throughput -RegisterId $register.RegisterId -WalletAddress $state.walletAddress
    Test-Latency -RegisterId $register.RegisterId -WalletAddress $state.walletAddress
    Test-Concurrency -RegisterId $register.RegisterId -WalletAddress $state.walletAddress
    Test-DocketBuilding -RegisterId $register.RegisterId -WalletAddress $state.walletAddress

    $totalStopwatch.Stop()
    $Script:Results.Summary.Duration = $totalStopwatch.Elapsed.TotalSeconds

    Export-Results

    # Final summary
    Write-Host ""
    Write-WtBanner "PerformanceBenchmark — Results"
    Write-Host "  Total Duration: $($Script:Results.Summary.Duration.ToString('F2'))s" -ForegroundColor White
    Write-Host "  Total Tests: $($Script:Results.Summary.TotalTests)" -ForegroundColor White
    Write-Host "  Total Errors: $($Script:Results.Summary.TotalErrors)" -ForegroundColor White

    if ($Script:Results.Summary.TotalTests -gt 0) {
        $successRate = ((1 - ($Script:Results.Summary.TotalErrors / $Script:Results.Summary.TotalTests)) * 100).ToString('F2')
        Write-Host "  Success Rate: $successRate%" -ForegroundColor White
    }

    Write-Host ""
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    exit 0
} catch {
    Write-WtFail "Benchmark failed: $($_.Exception.Message)"
    Write-Host $_.ScriptStackTrace -ForegroundColor Red

    $totalStopwatch.Stop()
    $Script:Results.Summary.Duration = $totalStopwatch.Elapsed.TotalSeconds
    Export-Results

    exit 1
}
