# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

<#
.SYNOPSIS
    Provisions the Strathcarron Council demo environment for the Feature 126
    cold-start enrolment walkthroughs.

.DESCRIPTION
    Stub created in PR-A (T002). Body fleshed out across Phases 3-5:
      - Phase 3 (US1): Strathcarron org provisioning + DrivingLicence blueprint
        publish + cold-start-<random>@example.test test citizen.
      - Phase 4 (US2): Tier 1 returning-<random>@example.test test citizen
        (signup + F114 device-pairing pre-run).
      - Phase 5 (US3): Tier 2 mini-gate-<random>@example.test test citizen
        (signup only, no device).

    Re-running this script resets all three test accounts to their target tier.

.PARAMETER ApiBase
    The API gateway base URL. Defaults to http://localhost.

.PARAMETER Profile
    'local' for Docker Compose, 'n1' for n1.sorcha.dev.

.EXAMPLE
    pwsh walkthroughs/Strathcarron/setup-cold-start-demo.ps1
    pwsh walkthroughs/Strathcarron/setup-cold-start-demo.ps1 -Profile n1
#>
[CmdletBinding()]
param(
    [string]$ApiBase = 'http://localhost',
    [ValidateSet('local', 'n1')]
    [string]$Profile = 'local'
)

$ErrorActionPreference = 'Stop'

Write-Host "Strathcarron cold-start demo setup — Profile: $Profile, ApiBase: $ApiBase"
Write-Host "Stub: full implementation lands across Phases 3-5 (T038, T047, T052)."
