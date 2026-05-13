# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Reads the captured walkthrough-runs/*.json snapshots and produces the
# top-line summary tables that the README references.
#
# Output:
#   - summary-tables.md  (top-10 by total time, top-10 by p99, per-walkthrough percentiles)
#   - per-rule-telemetry.json  (aggregated across all sequential post-warmup runs)
#
# This is intentionally separate from the capture script so it can be re-run
# after manual edits to env.json / methodology without re-running 4 hours of
# walkthroughs.

[CmdletBinding()]
param(
    [string] $OutputRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$walkthroughRunsDir = Join-Path $OutputRoot "walkthrough-runs"
if (-not (Test-Path $walkthroughRunsDir)) {
    throw "No walkthrough-runs/ directory at $walkthroughRunsDir — run run-baseline.ps1 first."
}

# Aggregate sequential runs across all walkthroughs.
$rules    = @{}      # code -> @{ count, totalNs, maxNs, samples=@(p50,p95,p99 per run) }
$sections = @{}      # name -> same shape
$perWalkthroughSection = @{}  # walkthrough -> name -> per-run total samples

$snapshots = Get-ChildItem -Path $walkthroughRunsDir -Recurse -Filter "seq-*.json"
Write-Host "Aggregating $($snapshots.Count) sequential snapshots..."

foreach ($snap in $snapshots) {
    $env = Get-Content $snap.FullName -Raw | ConvertFrom-Json
    $wt = $env.walkthrough
    if (-not $perWalkthroughSection.ContainsKey($wt)) {
        $perWalkthroughSection[$wt] = @{}
    }

    if ($null -eq $env.telemetry) { continue }

    foreach ($prop in $env.telemetry.rules.PSObject.Properties) {
        $code = $prop.Name
        $rs = $prop.Value
        if (-not $rules.ContainsKey($code)) {
            $rules[$code] = @{ evaluations = 0; emissions = 0; totalNs = [int64]0; maxNs = [int64]0; p99samples = @() }
        }
        $rules[$code].evaluations += [int64]$rs.evaluations
        $rules[$code].emissions   += [int64]$rs.emissions
        $rules[$code].totalNs     += [int64]$rs.totalNanos
        if ([int64]$rs.maxNanos -gt $rules[$code].maxNs) { $rules[$code].maxNs = [int64]$rs.maxNanos }
        if ([int64]$rs.evaluations -gt 0) { $rules[$code].p99samples += [int64]$rs.p99Nanos }
    }

    foreach ($prop in $env.telemetry.sections.PSObject.Properties) {
        $name = $prop.Name
        $rs = $prop.Value
        if (-not $sections.ContainsKey($name)) {
            $sections[$name] = @{ count = 0; totalNs = [int64]0; maxNs = [int64]0; p99samples = @() }
        }
        $sections[$name].count   += [int64]$rs.evaluations
        $sections[$name].totalNs += [int64]$rs.totalNanos
        if ([int64]$rs.maxNanos -gt $sections[$name].maxNs) { $sections[$name].maxNs = [int64]$rs.maxNanos }
        $sections[$name].p99samples += [int64]$rs.p99Nanos

        if (-not $perWalkthroughSection[$wt].ContainsKey($name)) {
            $perWalkthroughSection[$wt][$name] = @()
        }
        if ($name -eq "Total" -and [int64]$rs.evaluations -gt 0) {
            $perWalkthroughSection[$wt][$name] += @{
                p50 = [int64]$rs.p50Nanos
                p95 = [int64]$rs.p95Nanos
                p99 = [int64]$rs.p99Nanos
            }
        }
    }
}

function Format-Ns([int64] $ns) {
    if ($ns -lt 1000) { return "$ns ns" }
    if ($ns -lt 1000000) { return "{0:N2} µs" -f ($ns / 1000.0) }
    if ($ns -lt 1000000000) { return "{0:N2} ms" -f ($ns / 1000000.0) }
    return "{0:N2} s" -f ($ns / 1000000000.0)
}

# Top 10 by total time
$topByTotal = $rules.GetEnumerator() |
    Sort-Object -Property { -$_.Value.totalNs } |
    Select-Object -First 10 |
    ForEach-Object {
        [pscustomobject]@{
            Code        = $_.Key
            Evaluations = $_.Value.evaluations
            Emissions   = $_.Value.emissions
            TotalTime   = Format-Ns $_.Value.totalNs
            MaxTime     = Format-Ns $_.Value.maxNs
        }
    }

# Top 10 by max p99
$topByP99 = $rules.GetEnumerator() |
    Where-Object { $_.Value.p99samples.Count -gt 0 } |
    Sort-Object -Property { -((($_.Value.p99samples) | Measure-Object -Maximum).Maximum) } |
    Select-Object -First 10 |
    ForEach-Object {
        $maxP99 = (($_.Value.p99samples) | Measure-Object -Maximum).Maximum
        [pscustomobject]@{
            Code        = $_.Key
            MaxP99      = Format-Ns $maxP99
            Evaluations = $_.Value.evaluations
            MaxObserved = Format-Ns $_.Value.maxNs
        }
    }

# Section breakdown
$sectionTable = $sections.GetEnumerator() |
    Sort-Object -Property { -$_.Value.totalNs } |
    ForEach-Object {
        [pscustomobject]@{
            Section     = $_.Key
            Calls       = $_.Value.count
            TotalTime   = Format-Ns $_.Value.totalNs
            MaxP99      = Format-Ns ((($_.Value.p99samples) | Measure-Object -Maximum).Maximum)
            MaxObserved = Format-Ns $_.Value.maxNs
        }
    }

# Per-walkthrough Total p50/p95/p99 (median of per-run percentiles — robust to outliers)
$walkthroughTable = @()
foreach ($wt in $perWalkthroughSection.Keys) {
    $totals = $perWalkthroughSection[$wt]["Total"]
    if ($null -eq $totals -or $totals.Count -eq 0) { continue }
    $p50s = $totals | ForEach-Object { $_.p50 } | Sort-Object
    $p95s = $totals | ForEach-Object { $_.p95 } | Sort-Object
    $p99s = $totals | ForEach-Object { $_.p99 } | Sort-Object
    $median = { param($a) $a[[Math]::Floor($a.Count / 2)] }
    $walkthroughTable += [pscustomobject]@{
        Walkthrough = $wt
        Runs        = $totals.Count
        MedianP50   = Format-Ns (& $median $p50s)
        MedianP95   = Format-Ns (& $median $p95s)
        MedianP99   = Format-Ns (& $median $p99s)
    }
}

# Build markdown
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Validator Baseline — Top Findings")
[void]$md.AppendLine("")
[void]$md.AppendLine("Generated: $(Get-Date -Format 'u')")
[void]$md.AppendLine("Sequential snapshots aggregated: $($snapshots.Count)")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Top 10 rules by total time spent")
[void]$md.AppendLine("")
[void]$md.AppendLine(($topByTotal | Format-Table -AutoSize | Out-String -Width 200))
[void]$md.AppendLine("## Top 10 rules by p99 latency")
[void]$md.AppendLine("")
[void]$md.AppendLine(($topByP99 | Format-Table -AutoSize | Out-String -Width 200))
[void]$md.AppendLine("## Section breakdown")
[void]$md.AppendLine("")
[void]$md.AppendLine(($sectionTable | Format-Table -AutoSize | Out-String -Width 200))
[void]$md.AppendLine("## Per-walkthrough end-to-end validation latency (median of per-run percentiles)")
[void]$md.AppendLine("")
[void]$md.AppendLine(($walkthroughTable | Format-Table -AutoSize | Out-String -Width 200))

$summaryPath = Join-Path $OutputRoot "summary-tables.md"
Set-Content -Path $summaryPath -Value $md.ToString() -Encoding UTF8
Write-Host "Wrote $summaryPath"

# Aggregated raw JSON
$aggregate = [ordered]@{
    generatedAtUtc       = (Get-Date).ToUniversalTime().ToString("o")
    snapshotsAggregated  = $snapshots.Count
    rules                = $rules
    sections             = $sections
    perWalkthroughTotals = $perWalkthroughSection
}
$aggregatePath = Join-Path $OutputRoot "per-rule-telemetry.json"
$aggregate | ConvertTo-Json -Depth 10 | Set-Content -Path $aggregatePath -Encoding UTF8
Write-Host "Wrote $aggregatePath"
