#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Unattended walkthrough compliance suite. For each walkthrough: run setup.ps1 (if present)
# then run.ps1, each with a timeout; capture pass/fail + a failure tail; continue on failure;
# write an incremental markdown compliance report. Overnight post-deploy check for F136.

param(
    [int]$TimeoutSeconds = 420,
    [string]$ReportPath = (Join-Path $PSScriptRoot "COMPLIANCE-REPORT.md")
)

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $PSScriptRoot "compliance-logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }

# gateway = local Docker stack. AssuredIdentity already verified PASS on n1 separately.
$suite = [ordered]@{
    "RegisterCreationFlow"  = "gateway"
    "WalletVerification"    = "gateway"
    "FormCoverage"          = "gateway"
    "PayloadTests"          = "gateway"
    "ConstructionPermit"    = "gateway"
    "ForestryCertification" = "gateway"
    "TradeFinance"          = "gateway"
    "SelfBuildHouse"        = "gateway"
    "DistributedRegister"   = "gateway"
}

$results = @()
$started = Get-Date

function Get-Args { param($scriptText, $profile)
    $a = @()
    if ($scriptText -match '\$Profile')                     { $a += @('-Profile', $profile) }
    if ($scriptText -match '\[switch\]\s*\$Force')          { $a += '-Force' }
    if ($scriptText -match '\[switch\]\s*\$SkipHealthCheck'){ $a += '-SkipHealthCheck' }
    return ,$a
}

function Invoke-Step { param($scriptPath, $args, $logFile, $errFile, $timeout)
    $t0 = Get-Date
    $proc = Start-Process -FilePath "pwsh" `
        -ArgumentList (@('-NoProfile','-File', $scriptPath) + $args) `
        -WorkingDirectory $root -RedirectStandardOutput $logFile -RedirectStandardError $errFile `
        -PassThru -NoNewWindow
    $exited = $proc.WaitForExit($timeout * 1000)
    if (-not $exited) { try { $proc.Kill($true) } catch {}; return @{ Exit=$null; Dur=((Get-Date)-$t0); TimedOut=$true } }
    return @{ Exit=$proc.ExitCode; Dur=((Get-Date)-$t0); TimedOut=$false }
}

function Get-Tail { param($logFile, $errFile)
    $errTail = (Get-Content $errFile -Tail 4 -ErrorAction SilentlyContinue | Where-Object { $_ -and $_ -notmatch 'warning (CS|CA|MTP|NU)' }) -join ' / '
    if ($errTail) { return $errTail }
    return ((Get-Content $logFile -Tail 5 -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch '={6,}|^\s*$|unapproved verbs' }) -join ' / ')
}

function Write-Report { param($results, $inProgress)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Walkthrough Compliance Report")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Run started: $($started.ToString('u')) | Generated: $((Get-Date).ToString('u'))")
    [void]$sb.AppendLine("Post-deploy compliance for F136 (tiered-audience JWT). gateway = local Docker ``:latest`` (setup.ps1 then run.ps1).")
    [void]$sb.AppendLine("AssuredIdentity verified **PASS** on n1 (end-to-end, SC-001 budget OK) in a separate run.")
    if ($inProgress) { [void]$sb.AppendLine("`n> **IN PROGRESS** — running: $inProgress") }
    [void]$sb.AppendLine("")
    $pass = ($results | Where-Object { $_.Status -eq 'PASS' }).Count
    $fail = ($results | Where-Object { $_.Status -match 'FAIL' }).Count
    $to   = ($results | Where-Object { $_.Status -eq 'TIMEOUT' }).Count
    [void]$sb.AppendLine("**$pass passed, $fail failed, $to timed out** of $($results.Count) completed (gateway).")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Walkthrough | Stage | Status | Duration | Exit | Notes |")
    [void]$sb.AppendLine("|---|---|---|---|---|---|")
    foreach ($r in $results) {
        $note = ($r.Note -replace '\|','\\|' -replace '[\r\n]+',' ' -replace '\x1b\[[0-9;]*m','').Trim()
        if ($note.Length -gt 150) { $note = $note.Substring(0,150) + "..." }
        [void]$sb.AppendLine("| $($r.Name) | $($r.Stage) | **$($r.Status)** | $($r.Duration) | $($r.Exit) | $note |")
    }
    Set-Content -Path $ReportPath -Value $sb.ToString()
}

foreach ($name in $suite.Keys) {
    $profile = $suite[$name]
    $dir = Join-Path $PSScriptRoot $name
    $setup = Join-Path $dir "setup.ps1"
    $run = Join-Path $dir "run.ps1"
    Write-Host "`n=== $name ($profile) ===" -ForegroundColor Cyan
    if (-not (Test-Path $run)) {
        $results += [pscustomobject]@{ Name=$name; Stage='-'; Status='SKIP'; Duration='-'; Exit='-'; Note='no run.ps1' }
        Write-Report $results $null; continue
    }

    # --- setup.ps1 (if present) ---
    if (Test-Path $setup) {
        $args = Get-Args (Get-Content $setup -Raw) $profile
        $r = Invoke-Step $setup $args (Join-Path $logDir "$name.setup.log") (Join-Path $logDir "$name.setup.err.log") $TimeoutSeconds
        $durStr = "{0:mm\:ss}" -f $r.Dur
        if ($r.TimedOut) {
            $results += [pscustomobject]@{ Name=$name; Stage='setup'; Status='TIMEOUT'; Duration=$durStr; Exit='-'; Note="setup exceeded ${TimeoutSeconds}s" }
            Write-Host "  setup TIMEOUT" -ForegroundColor Red; Write-Report $results $null; continue
        }
        if ($r.Exit -ne 0) {
            $results += [pscustomobject]@{ Name=$name; Stage='setup'; Status='FAIL(setup)'; Duration=$durStr; Exit=$r.Exit; Note=(Get-Tail (Join-Path $logDir "$name.setup.log") (Join-Path $logDir "$name.setup.err.log")) }
            Write-Host "  setup FAIL (exit $($r.Exit))" -ForegroundColor Red; Write-Report $results $null; continue
        }
        Write-Host "  setup OK ($durStr)" -ForegroundColor DarkGreen
    }

    # --- run.ps1 ---
    $args = Get-Args (Get-Content $run -Raw) $profile
    $r = Invoke-Step $run $args (Join-Path $logDir "$name.run.log") (Join-Path $logDir "$name.run.err.log") $TimeoutSeconds
    $durStr = "{0:mm\:ss}" -f $r.Dur
    if ($r.TimedOut) {
        $results += [pscustomobject]@{ Name=$name; Stage='run'; Status='TIMEOUT'; Duration=$durStr; Exit='-'; Note="run exceeded ${TimeoutSeconds}s" }
        Write-Host "  run TIMEOUT" -ForegroundColor Red
    } else {
        $status = if ($r.Exit -eq 0) { 'PASS' } else { 'FAIL' }
        $results += [pscustomobject]@{ Name=$name; Stage='run'; Status=$status; Duration=$durStr; Exit=$r.Exit; Note=(Get-Tail (Join-Path $logDir "$name.run.log") (Join-Path $logDir "$name.run.err.log")) }
        Write-Host "  run $status (exit $($r.Exit), $durStr)" -ForegroundColor $(if ($r.Exit -eq 0){'Green'}else{'Red'})
    }
    Write-Report $results $null
}

Write-Report $results $null
Write-Host "`nReport: $ReportPath" -ForegroundColor Cyan
