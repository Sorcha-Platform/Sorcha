# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# Idempotency decisions: provision reuse + stale-state reconciliation (FR-003, R5).
# Pure decision logic — no IO. Callers gather the probe facts, this decides.

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    Decide what New-IssuingAuthority should do given an idempotency probe.
.DESCRIPTION
    Returns one of: 'Create' | 'Reuse' | 'ReconcileStale'.
      - Force                                              -> 'Create'
      - register recorded but NOT readable on the node     -> 'ReconcileStale'
        (the live-state footgun: state/subscription points at a register absent
         from Mongo; we must reconcile, not blindly reuse)
      - org + readable register + published blueprint exist -> 'Reuse'
      - otherwise                                           -> 'Create'
.PARAMETER HasOrg
    Whether an org for the agency already exists.
.PARAMETER HasRegisterId
    Whether a register id is recorded (state.json) / discoverable.
.PARAMETER RegisterReadable
    Whether that register actually reads back on the node.
.PARAMETER BlueprintPublished
    Whether the target blueprint is published on that register.
.PARAMETER Force
    Operator asked to recreate.
#>
function Resolve-AuthorityAction {
    [CmdletBinding()]
    param(
        [bool]$HasOrg,
        [bool]$HasRegisterId,
        [bool]$RegisterReadable,
        [bool]$BlueprintPublished,
        [bool]$Force
    )
    if ($Force) { return 'Create' }
    if ($HasRegisterId -and -not $RegisterReadable) { return 'ReconcileStale' }
    if ($HasOrg -and $HasRegisterId -and $RegisterReadable -and $BlueprintPublished) { return 'Reuse' }
    return 'Create'
}

<#
.SYNOPSIS
    Decide what Connect-Subscriber should do given the subscription probe.
.DESCRIPTION
    Returns one of: 'CreateSubscription' | 'ReuseSubscription' | 'ReconcileStaleSubscription'.
      - subscription present but register NOT readable -> 'ReconcileStaleSubscription'
      - subscription Active and register readable       -> 'ReuseSubscription'
      - otherwise                                        -> 'CreateSubscription'
.PARAMETER SubscriptionStatus
    Current subscription status, or $null/'' if none.
.PARAMETER RegisterReadable
    Whether the register reads back on the subscriber.
#>
function Resolve-SubscriptionAction {
    [CmdletBinding()]
    param(
        [string]$SubscriptionStatus,
        [bool]$RegisterReadable
    )
    $hasSubscription = -not [string]::IsNullOrWhiteSpace($SubscriptionStatus)
    if ($hasSubscription -and -not $RegisterReadable) { return 'ReconcileStaleSubscription' }
    if ($SubscriptionStatus -eq 'Active' -and $RegisterReadable) { return 'ReuseSubscription' }
    return 'CreateSubscription'
}
