#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# run-all.ps1 — Master script to run all walkthroughs in order.
# Runs Foundation -> Single-Org -> Multi-Org -> Advanced.
# Reports pass/fail for each walkthrough.
#
# Usage:
#   pwsh walkthroughs/run-all.ps1
#   pwsh walkthroughs/run-all.ps1 -SkipAdvanced
#   pwsh walkthroughs/run-all.ps1 -OnlySetup

param(
    [ValidateSet('gateway', 'direct', 'aspire')]
    [string]$Profile = 'gateway',
    [switch]$SkipAdvanced,
    [switch]$OnlySetup
)

$ErrorActionPreference = "Continue"

$modulePath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "Sorcha Walkthroughs — Run All"

# Ensure secrets exist
$secretsFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ".secrets/passwords.json"
if (-not (Test-Path $secretsFile)) {
    Write-WtInfo "Generating secrets..."
    $initScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "initialize-secrets.ps1"
    & pwsh $initScript
}

# Define walkthrough execution order
$walkthroughs = @(
    # Foundation
    @{ Name = "AdminIntegration"; Category = "Foundation"; HasSetupRun = $false; Script = "test-admin-integration.ps1" }
    @{ Name = "McpServerBasics"; Category = "Foundation"; HasSetupRun = $false; Script = "test-mcp-server.ps1" }

    # Single-Org
    @{ Name = "RegisterCreationFlow"; Category = "Single-Org"; HasSetupRun = $true }
    @{ Name = "WalletVerification"; Category = "Single-Org"; HasSetupRun = $true }
    @{ Name = "RegisterMongoDB"; Category = "Single-Org"; HasSetupRun = $false; Script = "test-mongodb-integration.ps1" }

    # Multi-Org
    @{ Name = "ConstructionPermit"; Category = "Multi-Org"; HasSetupRun = $true }
    @{ Name = "SelfBuildHouse"; Category = "Multi-Org"; HasSetupRun = $true }

    # HAIP (External Wallet) — must run in order (licence depends on identity)
    @{ Name = "HaipVerifiedCitizen"; Category = "HAIP"; HasSetupRun = $true }
    @{ Name = "HaipDrivingLicence"; Category = "HAIP"; HasSetupRun = $true }
)

if (-not $SkipAdvanced) {
    $walkthroughs += @(
        @{ Name = "DistributedRegister"; Category = "Advanced"; HasSetupRun = $true }
        @{ Name = "PerformanceBenchmark"; Category = "Advanced"; HasSetupRun = $true; RunScript = "test-performance.ps1" }
    )
}

$results = @()
$wtRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$totalStart = Get-Date

foreach ($wt in $walkthroughs) {
    $wtDir = Join-Path $wtRoot $wt.Name
    if (-not (Test-Path $wtDir)) {
        Write-WtWarn "Walkthrough directory not found: $($wt.Name) — skipping"
        $results += @{ Name = $wt.Name; Category = $wt.Category; Status = "SKIP"; Duration = 0 }
        continue
    }

    Write-WtStep "$($wt.Category): $($wt.Name)"
    $wtStart = Get-Date
    $passed = $true

    if ($wt.HasSetupRun) {
        # setup.ps1 + run.ps1 pattern
        $setupScript = Join-Path $wtDir "setup.ps1"
        if (Test-Path $setupScript) {
            Write-WtInfo "Running setup.ps1..."
            try {
                & pwsh $setupScript -Profile $Profile -SkipHealthCheck 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) { $passed = $false }
            } catch { $passed = $false }
        }

        if ($passed -and -not $OnlySetup) {
            $runScript = if ($wt.RunScript) { Join-Path $wtDir $wt.RunScript } else { Join-Path $wtDir "run.ps1" }
            if (Test-Path $runScript) {
                Write-WtInfo "Running $(Split-Path -Leaf $runScript)..."
                try {
                    & pwsh $runScript 2>&1 | Out-Null
                    if ($LASTEXITCODE -ne 0) { $passed = $false }
                } catch { $passed = $false }
            }
        }
    } else {
        # Single-script pattern
        $script = Join-Path $wtDir $wt.Script
        if (Test-Path $script) {
            Write-WtInfo "Running $($wt.Script)..."
            try {
                & pwsh $script -Profile $Profile 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) { $passed = $false }
            } catch { $passed = $false }
        } else {
            Write-WtWarn "Script not found: $($wt.Script)"
            $passed = $false
        }
    }

    $wtDuration = ((Get-Date) - $wtStart).TotalSeconds
    $status = if ($passed) { "PASS" } else { "FAIL" }
    $statusColor = if ($passed) { "Green" } else { "Red" }

    Write-Host "  [$status] $($wt.Name) ($([math]::Round($wtDuration, 1))s)" -ForegroundColor $statusColor
    $results += @{ Name = $wt.Name; Category = $wt.Category; Status = $status; Duration = [math]::Round($wtDuration, 1) }
}

# Final Summary
$totalDuration = ((Get-Date) - $totalStart).TotalSeconds
$passCount = ($results | Where-Object { $_.Status -eq "PASS" }).Count
$failCount = ($results | Where-Object { $_.Status -eq "FAIL" }).Count
$skipCount = ($results | Where-Object { $_.Status -eq "SKIP" }).Count

Write-Host ""
Write-WtBanner "Walkthrough Results"

$currentCategory = ""
foreach ($r in $results) {
    if ($r.Category -ne $currentCategory) {
        $currentCategory = $r.Category
        Write-Host "  --- $currentCategory ---" -ForegroundColor Yellow
    }
    $icon = switch ($r.Status) { "PASS" { "[OK]" } "FAIL" { "[X]" } "SKIP" { "[--]" } }
    $color = switch ($r.Status) { "PASS" { "Green" } "FAIL" { "Red" } "SKIP" { "Gray" } }
    Write-Host "  $icon $($r.Name) ($($r.Duration)s)" -ForegroundColor $color
}

Write-Host ""
Write-Host "  Total: $passCount pass, $failCount fail, $skipCount skip" -ForegroundColor White
Write-Host "  Duration: $([math]::Round($totalDuration, 1))s" -ForegroundColor White
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "  RESULT: ALL PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  RESULT: $failCount FAILED" -ForegroundColor Red
    exit 1
}
