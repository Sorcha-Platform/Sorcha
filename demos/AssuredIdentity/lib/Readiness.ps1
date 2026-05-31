# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Subscriber readiness predicate + poll loop (FR-004, R4).
# Pure predicate is separated from IO so it is unit-testable offline.

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Pure readiness predicate for a subscriber against a target register/blueprint.
.DESCRIPTION
    Returns { Ready = [bool]; Reasons = [string[]] }. Ready iff ALL hold (R4):
      - subscription status == 'Active'
      - sync-state         == 'CaughtUp'
      - the target blueprint id is present in the published-blueprints listing
    The third condition is the one that absorbs the ~60s BlueprintRecoveryService
    window — without it a tester hits POST /api/instances -> 409 blueprint_not_available.
.PARAMETER SubscriptionStatus
    Value from GET /api/organizations/{orgId}/register-subscriptions/{registerId}.
.PARAMETER SyncState
    Value from GET /api/registers/{id}/sync-state (Indeterminate|Syncing|CaughtUp|Error).
.PARAMETER PublishedBlueprintIds
    Blueprint ids from GET /api/registers/{id}/blueprints/published.
.PARAMETER TargetBlueprintId
    The blueprint the tester must be able to start.
#>
function Test-SubscriberReady {
    [CmdletBinding()]
    param(
        [string]$SubscriptionStatus,
        [string]$SyncState,
        [string[]]$PublishedBlueprintIds = @(),
        [Parameter(Mandatory)][string]$TargetBlueprintId
    )
    $reasons = @()
    if ($SubscriptionStatus -ne 'Active')  { $reasons += "subscription-not-active($SubscriptionStatus)" }
    if ($SyncState -ne 'CaughtUp')          { $reasons += "sync-state-not-caughtup($SyncState)" }
    if (@($PublishedBlueprintIds) -notcontains $TargetBlueprintId) { $reasons += "blueprint-not-published" }

    return [pscustomobject]@{
        Ready   = ($reasons.Count -eq 0)
        Reasons = $reasons
    }
}

<#
.SYNOPSIS
    Poll the injected signal probes until the subscriber is ready or timeout.
.DESCRIPTION
    IO orchestration around Test-SubscriberReady. The three probes are scriptblocks
    so the loop is testable with injected fakes. Returns:
      { Status = 'Ready'|'NotReady'; Reasons = [string[]]; Elapsed = [timespan] }
    On timeout returns NotReady (a soft, retryable outcome — never throws) with the
    last failing reasons (e.g. 'blueprint-not-published' = recovery in progress).
.PARAMETER GetSubscriptionStatus / GetSyncState / GetPublishedBlueprintIds
    Scriptblocks returning the current signal value.
.PARAMETER TargetBlueprintId
    The blueprint to wait for.
.PARAMETER TimeoutSeconds
    Poll cap (default 120 — comfortably over the observed <=60s recovery window).
.PARAMETER PollSeconds
    Delay between polls (default 5).
#>
function Wait-SubscriberReady {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock]$GetSubscriptionStatus,
        [Parameter(Mandatory)][scriptblock]$GetSyncState,
        [Parameter(Mandatory)][scriptblock]$GetPublishedBlueprintIds,
        [Parameter(Mandatory)][string]$TargetBlueprintId,
        [int]$TimeoutSeconds = 120,
        [int]$PollSeconds = 5
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $verdict = $null
    do {
        $subscription = & $GetSubscriptionStatus
        $sync         = & $GetSyncState
        $published    = @(& $GetPublishedBlueprintIds)

        $verdict = Test-SubscriberReady -SubscriptionStatus $subscription -SyncState $sync `
            -PublishedBlueprintIds $published -TargetBlueprintId $TargetBlueprintId

        if ($verdict.Ready) { break }
        if ($sw.Elapsed.TotalSeconds -ge $TimeoutSeconds) { break }
        Start-Sleep -Seconds $PollSeconds
    } while ($true)

    $sw.Stop()
    return [pscustomobject]@{
        Status  = if ($verdict.Ready) { 'Ready' } else { 'NotReady' }
        Reasons = $verdict.Reasons
        Elapsed = $sw.Elapsed
    }
}
