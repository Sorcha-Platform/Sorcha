#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# FormCoverage — Run
# Drives the FormCoverage blueprint for N rounds. Each round creates a fresh
# instance, submits the coverage form as the submitter, then submits the
# acknowledgement as the reviewer. Intended as a smoke loop for the
# SorchaFormRenderer + action pipeline.

param(
    [int]$Rounds = 5,
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "FormCoverage — Run ($Rounds round$(if($Rounds -ne 1){'s'}))"

$stateFile = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "No state.json found. Run setup.ps1 first."
    exit 1
}
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# ============================================================================
# Authenticate both users once and reuse their tokens across all rounds.
# ============================================================================
Write-WtStep "Authenticating submitter + reviewer"

$submitterAuth = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.submitter.email `
    -Password $state.submitter.password `
    -OrganizationId $state.submitter.organizationId

$reviewerAuth = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.reviewer.email `
    -Password $state.reviewer.password `
    -OrganizationId $state.reviewer.organizationId

$submitterToken = $submitterAuth.Token
$reviewerToken  = $reviewerAuth.Token
$submitterWallet = $state.submitter.walletAddress
$reviewerWallet  = $state.reviewer.walletAddress

Write-WtInfo "Submitter wallet: $submitterWallet"
Write-WtInfo "Reviewer wallet:  $reviewerWallet"

# ============================================================================
# Round loop
# ============================================================================
$roundsPassed = 0
$totalStart = Get-Date
$roundTimings = @()

for ($i = 1; $i -le $Rounds; $i++) {
    Write-WtStep "Round $i / $Rounds"
    $roundStart = Get-Date

    # --- Step 1: Create instance ---
    try {
        $instanceBody = @{
            blueprintId = $state.blueprintId
            registerId  = $state.registerId
            tenantId    = $state.submitter.organizationId
            metadata    = @{ source = "FormCoverage/run.ps1"; round = $i }
        }
        $instance = Invoke-SorchaApi -Method POST `
            -Uri "$($state.blueprintUrl)/instances/" `
            -Body $instanceBody `
            -Headers @{ Authorization = "Bearer $submitterToken" } `
            -ShowJson:$ShowJson
        $instanceId = $instance.id
        Write-WtInfo "  Instance: $instanceId"
    } catch {
        Write-WtFail "Instance creation failed: $($_.Exception.Message)"
        continue
    }

    # --- Step 2: Submit the coverage form (action 1) ---
    $wantsFollowUp = ($i % 2 -eq 0)  # alternate — tests the x-rule SHOW path
    $payload1 = @{
        givenName          = "Ada"
        familyName         = "Lovelace"
        email              = "ada.lovelace.round$i@example.com"
        biography          = "Round $i run of the form coverage walkthrough. This biography is long enough that the auto-generator would pick TextArea, but the form explicitly forces TextArea anyway. Filled programmatically by FormCoverage/run.ps1."
        dateOfBirth        = "1815-12-10"
        appointmentAt      = (Get-Date).AddDays($i).ToString("o")
        favouriteNumber    = 42 + $i
        satisfactionRating = "Satisfied"
        ticketSeverity     = "Medium"
        wantsFollowUp      = $wantsFollowUp
        confirmAccurate    = $true
    }
    if ($wantsFollowUp) {
        $payload1.followUpNotes = "Follow-up requested on round $i — see the auto-generated appointment."
    }

    try {
        $null = Invoke-SorchaAction `
            -BlueprintUrl $state.blueprintUrl `
            -InstanceId $instanceId `
            -ActionId "1" `
            -BlueprintId $state.blueprintId `
            -SenderWallet $submitterWallet `
            -RegisterId $state.registerId `
            -Token $submitterToken `
            -PayloadData $payload1
        Write-WtInfo "  Submit action ok"
    } catch {
        Write-WtFail "  Submit failed: $($_.Exception.Message)"
        try {
            $errBody = Get-SorchaErrorBody -ErrorRecord $_
            if ($errBody) { Write-WtWarn "  Detail: $errBody" }
        } catch {}
        continue
    }

    # --- Step 3: Reviewer acknowledges (action 2) ---
    $payload2 = @{
        acknowledged = $true
        reviewerNote = "Round $i acknowledged."
    }
    try {
        $null = Invoke-SorchaAction `
            -BlueprintUrl $state.blueprintUrl `
            -InstanceId $instanceId `
            -ActionId "2" `
            -BlueprintId $state.blueprintId `
            -SenderWallet $reviewerWallet `
            -RegisterId $state.registerId `
            -Token $reviewerToken `
            -PayloadData $payload2
        Write-WtInfo "  Acknowledge action ok"
    } catch {
        Write-WtFail "  Acknowledge failed: $($_.Exception.Message)"
        try {
            $errBody = Get-SorchaErrorBody -ErrorRecord $_
            if ($errBody) { Write-WtWarn "  Detail: $errBody" }
        } catch {}
        continue
    }

    $roundDuration = (Get-Date) - $roundStart
    $roundTimings += $roundDuration.TotalSeconds
    Write-WtSuccess "Round $i OK ($([math]::Round($roundDuration.TotalSeconds, 2))s)"
    $roundsPassed++
}

# ============================================================================
# Summary
# ============================================================================
$totalDuration = (Get-Date) - $totalStart

Write-WtBanner "FormCoverage Run Summary"
Write-WtInfo "Rounds passed : $roundsPassed / $Rounds"
Write-WtInfo "Total duration: $([math]::Round($totalDuration.TotalSeconds, 2))s"
if ($roundTimings.Count -gt 0) {
    $avg = ($roundTimings | Measure-Object -Average).Average
    $min = ($roundTimings | Measure-Object -Minimum).Minimum
    $max = ($roundTimings | Measure-Object -Maximum).Maximum
    Write-WtInfo "Avg per round : $([math]::Round($avg, 2))s  (min $([math]::Round($min, 2))s / max $([math]::Round($max, 2))s)"
}

if ($roundsPassed -eq $Rounds) {
    Write-WtSuccess "All rounds passed"
    exit 0
} else {
    Write-WtFail "$($Rounds - $roundsPassed) round(s) failed"
    exit 1
}
