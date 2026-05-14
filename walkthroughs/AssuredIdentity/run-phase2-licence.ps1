#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Phase 2 (Driving Licence) — DEFERRED
#
# Phase 2 previously used the legacy HAIP filesystem wallet to (a) present
# the AssuredIdentityCredential to the licensing officer via sorcha-agent
# haip present and (b) receive the DrivingLicenceCredential into the same
# filesystem wallet. Feature 124 swapped the AssuredIdentityCredential
# delivery to the Sorcha Wallet (PWA) — the filesystem-wallet path is gone
# (FR-011) — and the HAIP presentation flow that Phase 2 depended on cannot
# run scripted against the PWA.
#
# The umbrella citizen arc (`docs/superpowers/specs/2026-05-13-strathcarron-
# citizen-arc.md`) sequences this into Spec 4 — "credential-gated second
# service" — which will design the wallet-side ConsentSheet + verifier UX
# from scratch. Until Spec 4 lands, Phase 2 stays as this stub: it explains
# the deferral and exits cleanly so the walkthrough surface stays honest.

param(
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Phase 2 (Driving Licence) — DEFERRED to Spec 4"

Write-WtInfo @"
Phase 2 (Driving Licence) is currently deferred to a later spec in the
Strathcarron citizen arc — the 'credential-gated second service' beat
(Spec 4) owns the design for the wallet-side presentation UX that this
flow needs.

What's deferred:
  - Citizen presents AssuredIdentityCredential to the licensing officer
  - Licensing officer issues DrivingLicenceCredential into the citizen's
    wallet

What still works after Phase 1:
  - The citizen holds an AssuredIdentityCredential in the Sorcha Wallet
    PWA (verify on the wallet device's Home).

Run 'pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1' to land
the Assured Identity credential, then watch for Spec 4 to enable the
Driving Licence flow against the PWA-resident credential.

Umbrella context:
  docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md
"@

exit 0
