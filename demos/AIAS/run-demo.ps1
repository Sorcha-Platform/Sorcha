# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Assured Identity demo (Feature 174 / M1) — thin entry point.
# Idempotently provisions AIAS (org + master key + blueprint) and launches the
# autonomous Assure-ID agent, Docker-first or against n1. Safe to re-run after a
# network wipe (FR-010, SC-001). See specs/174-aias-assured-identity/quickstart.md.
#
# Usage:
#   ./demos/AIAS/run-demo.ps1            # Docker (default)
#   ./demos/AIAS/run-demo.ps1 -Target n1 # against n1.sorcha.dev
#   ./demos/AIAS/run-demo.ps1 -Force     # force recreate org + republish blueprint
[CmdletBinding()]
param(
    [ValidateSet('docker', 'n1')][string]$Target = 'docker',
    [string]$StateFile = (Join-Path $PSScriptRoot "state.json"),
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Import the shared walkthrough helpers directly (not just transitively via AiasDemo) so the
# Write-Wt* presentation cmdlets are in THIS script's scope — AiasDemo imports them as a nested
# module, which does not re-export them to the caller, so run-demo's own banner/info calls below
# would otherwise fail with "Write-WtBanner is not recognized" once provisioning returns.
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot "AiasDemo.psm1") -Force -DisableNameChecking

# One idempotent call: org -> master key -> blueprint -> agent config + launch.
$result = Initialize-AiasDemo -Target $Target -StateFile $StateFile -Force:$Force.IsPresent

# Final status — a clear, single-screen verdict the operator can read at a glance.
$status = Get-AiasDemoStatus -StateFile $StateFile

Write-WtBanner "AIAS demo — provisioning complete ($Target)"
Write-WtInfo "organization : $($result.organizationId)"
Write-WtInfo "register     : $($result.registerId)"
Write-WtInfo "blueprint    : $($result.blueprintId)"
Write-WtInfo "agent config : $($result.agentConfig)"
Write-WtInfo "agent running: $($result.agentRunning)"
Write-WtInfo "verdict      : $($status.Verdict)"

if ($status.Verdict -ne 'Ready') {
    Write-WtWarn "Not fully ready: $($status.Reasons -join ', '). If sorcha-agent is not on PATH, start it with the printed command; otherwise re-run after the stack settles."
    exit 1
}

Write-WtSuccess "AIAS is demo-ready. Hand a tester the web app and walk the happy path (quickstart section 2)."
exit 0
