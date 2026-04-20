#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — full two-phase orchestrator.
# Runs setup.ps1 (idempotent) then Phase 1 (identity) then Phase 2
# (driving licence). Logs total elapsed time so the SC-001 (under 3 min
# for identity) and SC-002 (under 2 min for licence) success criteria
# can be checked against real runs.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force,
    [switch]$ShowJson,
    [switch]$IncludePortrait
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Full Run (Phases 1 + 2)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$totalStart = Get-Date

# Setup (idempotent)
Write-WtStep "Setup"
& (Join-Path $scriptDir "setup.ps1") -Profile $Profile -SkipHealthCheck:$SkipHealthCheck -Force:$Force
if ($LASTEXITCODE -ne 0) { throw "setup.ps1 failed (exit $LASTEXITCODE)" }

# Phase 1 — Assured Identity issuance
$phase1Start = Get-Date
$phase1Args = @()
if ($ShowJson)         { $phase1Args += '-ShowJson' }
if ($IncludePortrait)  { $phase1Args += '-IncludePortrait' }
& (Join-Path $scriptDir "run-phase1-identity.ps1") @phase1Args
if ($LASTEXITCODE -ne 0) { throw "run-phase1-identity.ps1 failed (exit $LASTEXITCODE)" }
$phase1Elapsed = (Get-Date) - $phase1Start
Write-WtInfo ("Phase 1 elapsed: {0:mm\:ss}" -f $phase1Elapsed)

# Phase 2 — Driving Licence chain
$phase2Start = Get-Date
$phase2Args = @()
if ($ShowJson)         { $phase2Args += '-ShowJson' }
if ($IncludePortrait)  { $phase2Args += '-IncludePortrait' }
& (Join-Path $scriptDir "run-phase2-licence.ps1") @phase2Args
if ($LASTEXITCODE -ne 0) { throw "run-phase2-licence.ps1 failed (exit $LASTEXITCODE)" }
$phase2Elapsed = (Get-Date) - $phase2Start
Write-WtInfo ("Phase 2 elapsed: {0:mm\:ss}" -f $phase2Elapsed)

$totalElapsed = (Get-Date) - $totalStart
Write-WtBanner "AssuredIdentity — Full Run Complete"
Write-WtSuccess ("Total elapsed: {0:mm\:ss}" -f $totalElapsed)
Write-WtInfo "SC-001 budget: Phase 1 under 3 min — $(if ($phase1Elapsed.TotalMinutes -lt 3) { 'OK' } else { 'OVER' })"
Write-WtInfo "SC-002 budget: Phase 2 under 2 min — $(if ($phase2Elapsed.TotalMinutes -lt 2) { 'OK' } else { 'OVER' })"
