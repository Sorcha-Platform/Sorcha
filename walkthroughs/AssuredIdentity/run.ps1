#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — orchestrator.
# Runs setup.ps1 (idempotent) then Phase 1 (identity, PWA delivery). Phase 2
# (driving licence) is currently a stub — Feature 124 deferred the
# credential-gated second-service flow to Spec 4 of the citizen arc.
# Logs total elapsed time so the SC-001 (under 3 min for identity) success
# criterion can be checked against real runs.

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

# Phase 2 — deferred to Spec 4 of the citizen arc. The stub script logs the
# deferral and exits 0; we surface its banner but skip the timing budget.
& (Join-Path $scriptDir "run-phase2-licence.ps1")

$totalElapsed = (Get-Date) - $totalStart
Write-WtBanner "AssuredIdentity — Full Run Complete"
Write-WtSuccess ("Total elapsed: {0:mm\:ss}" -f $totalElapsed)
Write-WtInfo "SC-001 budget: Phase 1 under 3 min — $(if ($phase1Elapsed.TotalMinutes -lt 3) { 'OK' } else { 'OVER' })"
