# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Cross-node readiness verdict aggregation (FR-018, SC-007). Pure logic.

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Compute the demo-wide readiness verdict from gathered per-node signals.
.DESCRIPTION
    Pure aggregation. The verdict MUST predict tester success (SC-007):
      Ready iff the issuer is reachable AND an approver is present AND at least
      one subscriber is fully ready (Active + CaughtUp + blueprint published).
    Returns:
      { Verdict = 'Ready'|'NotReady'; PerNode = [..]; Reasons = [string[]] }
.PARAMETER IssuerReachable
    Whether the issuer gateway answered a health probe.
.PARAMETER ApproverPresent
    Whether the approval agent is running (rules/ai) or human-acknowledged.
.PARAMETER Subscribers
    Array of per-subscriber objects with: NodeId, SubscriptionStatus, SyncState,
    PublishedBlueprintIds, TargetBlueprintId.
#>
function Get-ReadinessVerdict {
    [CmdletBinding()]
    param(
        [bool]$IssuerReachable,
        [bool]$ApproverPresent,
        [object[]]$Subscribers = @()
    )
    $reasons = @()
    if (-not $IssuerReachable) { $reasons += "issuer-unreachable" }
    if (-not $ApproverPresent) { $reasons += "approver-absent" }

    $perNode = @()
    $anySubscriberReady = $false
    foreach ($sub in $Subscribers) {
        $v = Test-SubscriberReady -SubscriptionStatus $sub.SubscriptionStatus -SyncState $sub.SyncState `
            -PublishedBlueprintIds @($sub.PublishedBlueprintIds) -TargetBlueprintId $sub.TargetBlueprintId
        if ($v.Ready) { $anySubscriberReady = $true }
        $perNode += [pscustomobject]@{
            NodeId  = $sub.NodeId
            Ready   = $v.Ready
            Reasons = $v.Reasons
        }
    }

    if (@($Subscribers).Count -eq 0) { $reasons += "no-subscribers" }
    elseif (-not $anySubscriberReady) { $reasons += "no-subscriber-ready" }

    return [pscustomobject]@{
        Verdict = if ($reasons.Count -eq 0) { 'Ready' } else { 'NotReady' }
        PerNode = $perNode
        Reasons = $reasons
    }
}
