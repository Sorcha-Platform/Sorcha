# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

<#
.SYNOPSIS
    Per-blueprint publish-or-reuse decision for Publish-AiasBlueprint.
.DESCRIPTION
    Issue #1269 follow-up. #1269 taught the publish BODY to publish both templates, but left the
    idempotency guard keyed on the SCALAR state.blueprintId:

        if (-not $Force -and $state.blueprintId) {
            if ($pub -contains $state.blueprintId) { return $state }   # <- early return
        }

    On any already-provisioned node the application workflow IS published, so the guard fired and
    returned before the both-templates loop was ever reached. The device-registration blueprint
    therefore stayed unpublished, and state.blueprintIds was never written — the same structural
    blindness #1269 set out to remove, moved one level up into the guard.

    Forcing past it was not a fix either: blueprint ids are timestamped, so -Force republishes the
    application workflow under a NEW id, leaving a duplicate behind and re-pointing state at it.

    The decision is therefore PER SPEC, not all-or-nothing: reuse what is already published, publish
    only what is missing. That publishes the device blueprint on an existing node without -Force and
    without duplicating the application workflow.

    Pure function — no HTTP, no state file — so it is unit-testable, mirroring
    demos/AssuredIdentity/lib/Idempotency.ps1.
#>

function Resolve-BlueprintPublishAction {
    [CmdletBinding()]
    param(
        # Id prefix for the spec, e.g. 'aias-device-registration'. Published ids are
        # "{IdPrefix}-{timestamp}".
        [Parameter(Mandatory)][string]$IdPrefix,

        # Blueprint ids currently published on the register.
        [string[]]$PublishedIds = @(),

        # The id state already records for this spec, when known. When several published ids share
        # the prefix (a previous -Force leaves duplicates behind) this one wins, so reuse is
        # deterministic rather than dependent on the order the register happens to return.
        [string]$PreferredId,

        [bool]$Force = $false
    )

    if ($Force) {
        return @{ Action = 'Publish'; ExistingId = $null }
    }

    # Match on the "{prefix}-" boundary, never a bare prefix match: 'aias-assured-identity' must not
    # be satisfied by some future 'aias-assured-identity-lite-...'.
    $matches = @($PublishedIds | Where-Object { $_ -and $_.StartsWith("$IdPrefix-", [System.StringComparison]::Ordinal) })

    if ($matches.Count -eq 0) {
        return @{ Action = 'Publish'; ExistingId = $null }
    }

    if ($PreferredId -and ($matches -contains $PreferredId)) {
        return @{ Action = 'Reuse'; ExistingId = $PreferredId }
    }

    return @{ Action = 'Reuse'; ExistingId = $matches[0] }
}
