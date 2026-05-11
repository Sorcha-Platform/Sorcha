# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Top-level orchestrator for the validator baseline capture. Run on a
# QUIESCENT machine — close other apps, disable scheduled tasks, plug in.
#
# Order of operations:
#   1. Sanity-check Docker stack is up + benchmark overlay is applied
#   2. Capture environment metadata into env.json
#   3. Run BenchmarkDotNet suite → microbenchmarks/
#   4. Start dotnet-counters background captures (one per service)
#   5. Run each walkthrough sequentially (×100, discard first 10)
#   6. Run each walkthrough concurrently (N=10)
#   7. Stop dotnet-counters
#   8. Print "captured at <UTC>; review {output-root} and run summarise.ps1"
#
# Runtime: ~3-5h depending on walkthroughs, hardware, dotnet-counters interval.

[CmdletBinding()]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,

    [string] $OutputRoot = $PSScriptRoot,

    [string] $ValidatorBaseUrl = "http://localhost:5800",

    [int] $TotalRuns = 100,

    [int] $WarmupRuns = 10,

    [int] $Concurrency = 10,

    [switch] $SkipBenchmarks,

    [switch] $SkipCounters,

    [switch] $SmokeOnly  # 5 sequential AssuredIdentity runs only — fast harness validation
)

$ErrorActionPreference = "Stop"
$walkthroughRunsDir = Join-Path $OutputRoot "walkthrough-runs"
$microbenchDir = Join-Path $OutputRoot "microbenchmarks"
$countersDir = Join-Path $OutputRoot "dotnet-counters"
$rawDir = Join-Path $OutputRoot "raw"
New-Item -ItemType Directory -Force -Path $walkthroughRunsDir, $microbenchDir, $countersDir, $rawDir | Out-Null

# 1. Capture environment
Write-Host "==> Capturing environment metadata..." -ForegroundColor Cyan
$envInfo = [ordered]@{
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    gitSha        = (& git -C $RepoRoot rev-parse HEAD).Trim()
    gitBranch     = (& git -C $RepoRoot branch --show-current).Trim()
    gitClean      = ((& git -C $RepoRoot status --porcelain) -eq $null)
    dotnetVersion = (& dotnet --version).Trim()
    osVersion     = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    osArch        = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    processArch   = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    cpuCount      = [Environment]::ProcessorCount
    cpuName       = (Get-CimInstance -ClassName Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Name)
    machineName   = [Environment]::MachineName
    totalMemoryGB = if ((Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction SilentlyContinue)) {
        [Math]::Round((Get-CimInstance -ClassName Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    } else { $null }
    runConfig     = [ordered]@{
        totalRuns   = $TotalRuns
        warmupRuns  = $WarmupRuns
        concurrency = $Concurrency
        smokeOnly   = $SmokeOnly.IsPresent
    }
}
$envInfo | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputRoot "env.json") -Encoding UTF8
Write-Host "    git: $($envInfo.gitSha) on $($envInfo.gitBranch) (clean=$($envInfo.gitClean))"
Write-Host "    dotnet: $($envInfo.dotnetVersion)   cpu: $($envInfo.cpuCount)x   mem: $($envInfo.totalMemoryGB) GB"

# 2. BenchmarkDotNet suite
if (-not $SkipBenchmarks) {
    Write-Host "==> Running BenchmarkDotNet suite (≈ 5-15 min)..." -ForegroundColor Cyan
    Push-Location (Join-Path $RepoRoot "bench\Sorcha.Validator.Benchmarks")
    try {
        dotnet run -c Release --no-build -- --filter "*" --artifacts $microbenchDir
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "BenchmarkDotNet exited $LASTEXITCODE — continuing"
        }
    } finally {
        Pop-Location
    }
}

# 3. dotnet-counters captures (background)
$counterProcs = @()
if (-not $SkipCounters) {
    $hasCounters = $null -ne (Get-Command dotnet-counters -ErrorAction SilentlyContinue)
    if (-not $hasCounters) {
        Write-Warning "dotnet-counters not on PATH; install with 'dotnet tool install --global dotnet-counters'. Skipping counters."
    }
    else {
        # Targets validator process inside the container via diagnostic socket
        # mounted by docker-compose.benchmark.yml.
        $diagSock = Join-Path $OutputRoot "bench-diag\sorcha-validator-diag.sock"
        if (-not (Test-Path $diagSock)) {
            Write-Warning "Diagnostic socket not found at $diagSock — is the benchmark overlay applied? Skipping counters."
        }
        else {
            $countersFile = Join-Path $countersDir "validator.csv"
            Write-Host "==> Starting dotnet-counters → $countersFile" -ForegroundColor Cyan
            $counterProcs += Start-Process -FilePath "dotnet-counters" `
                -ArgumentList @("collect", "--diagnostic-port", $diagSock, `
                    "--output", $countersFile, "--format", "csv", `
                    "--counters", "System.Runtime,Sorcha.Validator.Mempool,Sorcha.Storage") `
                -PassThru -WindowStyle Hidden
        }
    }
}

# 4. Walkthrough runs
# Note: ConstructionPermit + PayloadTests use different param shapes
# (-Scenario A/B/C/all and -FileSize/-Rounds respectively, no -Profile),
# and need their own setup.ps1 runs to seed state. Baseline v1 narrows to
# AssuredIdentity only; CP + PT can be added once their harness wiring is
# proven separately. Walkthrough list is data, not code — edit here when
# adding more.
$walkthroughs = @(
    @{
        # Call the phase scripts directly; the wrapping run.ps1 re-runs setup
        # which short-returns when state.json exists, leaving $LASTEXITCODE in
        # an undefined state that trips its own throw. Setup is a one-time
        # prerequisite for the bench harness.
        name        = "AssuredIdentity"
        runCommand  = "& '$RepoRoot\walkthroughs\AssuredIdentity\run-phase1-identity.ps1'; if (`$LASTEXITCODE -eq 0) { & '$RepoRoot\walkthroughs\AssuredIdentity\run-phase2-licence.ps1' }"
        category    = "presentation-lifecycle"
    }
)

if ($SmokeOnly) {
    Write-Host "==> SMOKE MODE: AssuredIdentity ×5 sequential, no concurrency" -ForegroundColor Yellow
    $walkthroughs = @($walkthroughs[0])
    $TotalRuns = 5
    $WarmupRuns = 0
    $Concurrency = 1
}

foreach ($wt in $walkthroughs) {
    Write-Host "==> Sequential $($wt.name) ×$TotalRuns (warmup $WarmupRuns)" -ForegroundColor Cyan
    & "$PSScriptRoot\run-walkthrough.ps1" `
        -Walkthrough $wt.name `
        -RunCommand $wt.runCommand `
        -TotalRuns $TotalRuns `
        -WarmupRuns $WarmupRuns `
        -Concurrency 1 `
        -ValidatorBaseUrl $ValidatorBaseUrl `
        -OutputRoot $walkthroughRunsDir

    if ($Concurrency -gt 1 -and -not $SmokeOnly) {
        Write-Host "==> Concurrent $($wt.name) N=$Concurrency" -ForegroundColor Cyan
        & "$PSScriptRoot\run-walkthrough.ps1" `
            -Walkthrough $wt.name `
            -RunCommand $wt.runCommand `
            -TotalRuns $TotalRuns `
            -WarmupRuns $WarmupRuns `
            -Concurrency $Concurrency `
            -ValidatorBaseUrl $ValidatorBaseUrl `
            -OutputRoot $walkthroughRunsDir
    }
}

# 5. Stop counters
foreach ($p in $counterProcs) {
    if (-not $p.HasExited) {
        Write-Host "==> Stopping dotnet-counters PID $($p.Id)" -ForegroundColor Cyan
        Stop-Process -Id $p.Id -Force
    }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "Capture complete." -ForegroundColor Green
Write-Host "  env:           $OutputRoot\env.json"
Write-Host "  walkthroughs:  $walkthroughRunsDir"
Write-Host "  microbenches:  $microbenchDir"
Write-Host "  counters:      $countersDir"
Write-Host ""
Write-Host "Next: pwsh $PSScriptRoot\summarise.ps1" -ForegroundColor Yellow
