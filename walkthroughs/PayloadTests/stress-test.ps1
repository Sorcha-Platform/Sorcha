#!/usr/bin/env pwsh
# Stress test: 100 random-sized file transfers over ~20 minutes on n1
param(
    [int]$TotalRuns = 100,
    [int]$DurationMinutes = 20,
    [string]$Profile = 'n1'
)

$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Random file sizes: 512B to 10MB (weighted toward smaller files)
$sizes = @(
    @{ Label = "512B";  Bytes = 512 },
    @{ Label = "1KB";   Bytes = 1024 },
    @{ Label = "2KB";   Bytes = 2048 },
    @{ Label = "5KB";   Bytes = 5120 },
    @{ Label = "10KB";  Bytes = 10240 },
    @{ Label = "50KB";  Bytes = 51200 },
    @{ Label = "100KB"; Bytes = 102400 },
    @{ Label = "256KB"; Bytes = 262144 },
    @{ Label = "500KB"; Bytes = 512000 },
    @{ Label = "1MB";   Bytes = 1048576 },
    @{ Label = "2MB";   Bytes = 2097152 },
    @{ Label = "4MB";   Bytes = 4194304 },
    @{ Label = "5MB";   Bytes = 5242880 },
    @{ Label = "10MB";  Bytes = 10485760 }
)

$delaySeconds = [math]::Floor(($DurationMinutes * 60) / $TotalRuns)
$startTime = Get-Date
$passed = 0
$failed = 0
$results = @()

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Stress Test: $TotalRuns transfers over ${DurationMinutes}min (~${delaySeconds}s apart)" -ForegroundColor Cyan
Write-Host "  Profile: $Profile" -ForegroundColor Cyan
Write-Host "  Start: $($startTime.ToString('HH:mm:ss'))" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

for ($i = 1; $i -le $TotalRuns; $i++) {
    $size = $sizes | Get-Random
    $elapsed = (Get-Date) - $startTime
    $pct = [math]::Round(($i / $TotalRuns) * 100)

    Write-Host "[$i/$TotalRuns] ($pct%) $($size.Label) - " -NoNewline -ForegroundColor Yellow

    $runStart = Get-Date
    try {
        $output = & pwsh -File "$scriptDir/run.ps1" -Profile $Profile -FileSize $size.Label -Rounds 1 2>&1
        $statusLine = $output | Select-String "Status:" | Select-Object -Last 1
        $stepsLine = $output | Select-String "Steps:" | Select-Object -Last 1

        if ($statusLine -match "ALL PASSED") {
            $runDuration = (Get-Date) - $runStart
            Write-Host "PASS ($([math]::Round($runDuration.TotalSeconds, 1))s)" -ForegroundColor Green
            $passed++
            $results += @{ Run = $i; Size = $size.Label; Status = "PASS"; Duration = $runDuration.TotalSeconds }
        } else {
            $runDuration = (Get-Date) - $runStart
            Write-Host "FAIL ($([math]::Round($runDuration.TotalSeconds, 1))s)" -ForegroundColor Red
            $failLine = $output | Select-String "\[X\]" | Select-Object -Last 1
            if ($failLine) { Write-Host "  $failLine" -ForegroundColor DarkRed }
            $failed++
            $results += @{ Run = $i; Size = $size.Label; Status = "FAIL"; Duration = $runDuration.TotalSeconds }
        }
    } catch {
        $runDuration = (Get-Date) - $runStart
        Write-Host "ERROR ($([math]::Round($runDuration.TotalSeconds, 1))s) - $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        $results += @{ Run = $i; Size = $size.Label; Status = "ERROR"; Duration = $runDuration.TotalSeconds }
    }

    # Pace to fit within duration (subtract time already spent)
    if ($i -lt $TotalRuns) {
        $targetTime = $startTime.AddSeconds($delaySeconds * $i)
        $waitMs = [math]::Max(0, ($targetTime - (Get-Date)).TotalMilliseconds)
        if ($waitMs -gt 100) {
            Start-Sleep -Milliseconds $waitMs
        }
    }
}

$totalDuration = (Get-Date) - $startTime
$avgDuration = if ($results.Count -gt 0) { [math]::Round(($results | Measure-Object -Property Duration -Average).Average, 1) } else { 0 }

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  STRESS TEST COMPLETE" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Total:    $TotalRuns runs" -ForegroundColor White
Write-Host "  Passed:   $passed" -ForegroundColor Green
Write-Host "  Failed:   $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "  Duration: $([math]::Round($totalDuration.TotalMinutes, 1)) minutes" -ForegroundColor White
Write-Host "  Avg/run:  ${avgDuration}s" -ForegroundColor White
Write-Host "  Rate:     $([math]::Round($passed / $totalDuration.TotalMinutes, 1)) pass/min" -ForegroundColor White
Write-Host "================================================================" -ForegroundColor Cyan

# Size breakdown
Write-Host ""
Write-Host "  Size breakdown:" -ForegroundColor Yellow
$results | Group-Object -Property Size | Sort-Object { $_.Group[0].Duration } | ForEach-Object {
    $p = ($_.Group | Where-Object { $_.Status -eq "PASS" }).Count
    $f = ($_.Group | Where-Object { $_.Status -ne "PASS" }).Count
    $avg = [math]::Round(($_.Group | Measure-Object -Property Duration -Average).Average, 1)
    Write-Host "    $($_.Name.PadRight(8)) $p pass / $f fail  (avg ${avg}s)" -ForegroundColor $(if ($f -gt 0) { "Yellow" } else { "White" })
}
