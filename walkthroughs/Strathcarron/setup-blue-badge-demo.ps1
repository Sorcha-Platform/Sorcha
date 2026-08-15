#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Strathcarron Council — Blue Badge demo setup (Feature 127 / PR-C).
#
# Chains off the Spec 3 (Feature 126) cold-start setup: reads its
# state.json, publishes the Blue Badge blueprint against the existing
# Strathcarron register, and writes the Blue Badge blueprint id back so
# the council portal page can pick it up.
#
# PREREQUISITES (operator runs these first):
#   1. walkthroughs/Strathcarron/setup-cold-start-demo.ps1 — provisions
#      the Strathcarron Council org, register, driving-licence blueprint,
#      and the three Tier-1/2/3 test citizens.
#   2. Sign Tier 1 (returning-*@example.test) into http://localhost/wallet/
#      and pair a device via Settings → Enrol this device.
#   3. Walk the driving-licence journey for the Tier 1 citizen so they
#      receive an AssuredIdentityCredential — Spec 4's gate requires it.
#
# After this script:
#   1. Browse http://localhost:5400/services/blue-badge
#   2. Sign in as the Tier 1 returning-*@example.test citizen
#   3. Walk the Blue Badge journey end-to-end (Walk 1 from spec quickstart).

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "Strathcarron — Blue Badge demo setup (Feature 127)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$coldStartStateFile = Join-Path $scriptDir "state.json"

# ============================================================================
# Step 1: Verify the Spec 3 cold-start has run
# ============================================================================
if (-not (Test-Path $coldStartStateFile)) {
    Write-WtError "Spec 3 cold-start state not found at $coldStartStateFile."
    Write-WtError "Run walkthroughs/Strathcarron/setup-cold-start-demo.ps1 first, then re-run this script."
    exit 1
}

$coldStartState = Get-Content $coldStartStateFile -Raw | ConvertFrom-Json

# Idempotency: if Blue Badge is already provisioned and -Force isn't set, exit cleanly.
if (-not $Force -and $coldStartState.PSObject.Properties.Name -contains "blueBadgeBlueprintId" -and $coldStartState.blueBadgeBlueprintId) {
    Write-WtInfo "Blue Badge blueprint already provisioned (id $($coldStartState.blueBadgeBlueprintId))."
    Write-WtInfo "Use -Force to re-publish."
    Write-WtInfo "Demo URL: http://localhost:5400/services/blue-badge"
    return
}

$secrets = Get-SorchaSecrets -WalkthroughName "strathcarron-blue-badge" -Profile $Profile
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

# ============================================================================
# Step 2: Sign the council admin back in
# ============================================================================
Write-WtStep "Step 2: Sign in as Strathcarron council admin"
$councilOrgId = $coldStartState.councilOrgId
# Take the operator from cold-start state rather than repeating the address here — the two scripts
# drifted apart exactly this way, and a hardcoded email that is not a member of the council org logs
# in against the Public org instead, then fails several steps later with an unrelated-looking 403.
$councilAdminEmail = $coldStartState.councilAdminEmail
if (-not $councilAdminEmail) {
    throw ("Cold-start state carries no councilAdminEmail — it was written by a revision of " +
           "setup-cold-start-demo.ps1 that predates this field. Re-run setup-cold-start-demo.ps1.")
}
$councilSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $councilAdminEmail `
    -Password $secrets.DefaultPassword `
    -OrganizationId $councilOrgId

# ============================================================================
# Step 3: Publish the Blue Badge blueprint against the existing register
# ============================================================================
Write-WtStep "Step 3: Publish Blue Badge blueprint"

$walletMap = @{
    "licensing-officer" = $coldStartState.councilWalletAddress
}

$blueBadgeBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/strathcarron-blue-badge.json") `
    -WalletMap $walletMap `
    -Headers $councilSession.Headers `
    -IdPrefix "strathcarron-blue-badge" `
    -RegisterId $coldStartState.registerId

Write-WtSuccess "Blue Badge blueprint: $($blueBadgeBlueprint.BlueprintId)"

# ============================================================================
# Step 4: Best-effort check that the Tier 1 citizen holds an
#          AssuredIdentityCredential. We don't fail here — the council
#          page surfaces the no-credential error state with a link back
#          to the driving-licence form, which is itself part of the
#          Spec 4 demo (US3: no-credential cold-start citizen routed back).
# ============================================================================
$tier1Email = $coldStartState.citizens.fastPath.email
Write-WtInfo "Tier 1 citizen for the Blue Badge journey: $tier1Email"
Write-WtInfo "  If they haven't received an AssuredIdentityCredential yet, the Blue Badge page"
Write-WtInfo "  will route them back to /services/driving-licence — exercising the US3 path."

# ============================================================================
# Step 5: Persist the new blueprint id back into state.json
# ============================================================================
Write-WtStep "Step 5: Update state.json"

$coldStartState | Add-Member -NotePropertyName "blueBadgeBlueprintId" -NotePropertyValue $blueBadgeBlueprint.BlueprintId -Force
$coldStartState | Add-Member -NotePropertyName "blueBadgePage" -NotePropertyValue ($env:STRATHCARRON_PORTAL_URL ?? "http://localhost:5400/services/blue-badge") -Force

$coldStartState | ConvertTo-Json -Depth 10 | Set-Content -Path $coldStartStateFile
Write-WtSuccess "state.json updated with blueBadgeBlueprintId"

Write-Host ""
Write-WtInfo "Demo URLs:"
Write-WtInfo "  Blue Badge page:    $($coldStartState.blueBadgePage)"
Write-WtInfo "  Driving licence:    $($coldStartState.councilPage)  (gating credential is issued here)"
Write-WtInfo "  Wallet PWA:         $($sorchaEnv.GatewayUrl)/wallet/"
Write-Host ""
Write-WtInfo "Walks (Spec 4 — see specs/127-credential-gated-service/quickstart.md):"
Write-WtInfo "  Walk 1 — Returning Tier 1 happy path (SC-001, SC-002, SC-004)"
Write-WtInfo "  Walk 2 — No-credential citizen routed back (SC-003)"
Write-WtInfo "  Walk 3 — Friend scans QR by mistake (FR-019)"
Write-WtInfo "  Walk 4 — Revoked credential (SC-005)"
