#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ForestryCertification — Run
# Executes a single 2-action scenario (golden-path or decline).
#   golden-path: Sales Manager applies, Auditor approves, ForestProductDPPCredential issued.
#   decline:     Sales Manager applies, Auditor declines, no credential issued.

param(
    [ValidateSet('golden-path', 'decline')]
    [string]$Scenario = 'golden-path',
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ForestryCertification — Run ($Scenario)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

$scenarioFile = Join-Path $scriptDir "data/scenario-$Scenario.json"
if (-not (Test-Path $scenarioFile)) { Write-WtFail "Scenario file not found: $scenarioFile"; exit 1 }
$data = Get-Content -Path $scenarioFile -Raw | ConvertFrom-Json -AsHashtable

# ============================================================================
# Step 1: Authenticate both roles
# ============================================================================
Write-WtStep "Step 1: Authenticate Sales Manager and Auditor"

$salesSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.salesMgr.email `
    -Password $state.roles.salesMgr.password `
    -OrganizationId $state.roles.salesMgr.organizationId
Write-WtSuccess "Authenticated as Sales Manager"

$auditorSession = Connect-SorchaUser `
    -TenantUrl $state.tenantUrl `
    -Email $state.roles.auditor.email `
    -Password $state.roles.auditor.password `
    -OrganizationId $state.roles.auditor.organizationId
Write-WtSuccess "Authenticated as Auditor"

# ============================================================================
# Step 2: Sales Manager creates instance + submits Action 1
# ============================================================================
Write-WtStep "Step 2: Sales Manager submits batch for certification (Action 1)"

$instanceBody = @{
    blueprintId = $state.blueprintId
    registerId  = $state.registerId
    tenantId    = $state.organizations.highlandTimber
    metadata    = @{ source = "walkthrough"; walkthrough = "ForestryCertification"; scenario = $Scenario }
}

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Body $instanceBody `
    -Headers $salesSession.Headers
$instanceId = $instance.id
Write-WtSuccess "Instance created: $instanceId"
if ($ShowJson) { $instance | ConvertTo-Json -Depth 5 | Write-Host }

$action1Payload = $data.actions['1']

$null = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "1" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.wallets.salesMgr `
    -RegisterId $state.registerId `
    -Token $salesSession.Token `
    -PayloadData $action1Payload `
    -WaitForSeal
Write-WtSuccess "Action 1 submitted (batch $($action1Payload.batchId))"

# ============================================================================
# Step 3: Auditor reviews + decides
# ============================================================================
Write-WtStep "Step 3: Auditor performs audit (Action 2 — $($data.expectedDecision))"

$action2Payload = $data.actions['2']

$action2Response = Invoke-SorchaAction `
    -BlueprintUrl $state.blueprintUrl `
    -InstanceId $instanceId `
    -ActionId "2" `
    -BlueprintId $state.blueprintId `
    -SenderWallet $state.wallets.auditor `
    -RegisterId $state.registerId `
    -Token $auditorSession.Token `
    -PayloadData $action2Payload `
    -WaitForSeal

if ($ShowJson) { $action2Response | ConvertTo-Json -Depth 5 | Write-Host }

# ============================================================================
# Step 4: Verify outcome
# ============================================================================
Write-WtStep "Step 4: Verify outcome"

if ($data.expectedDecision -eq "approve") {
    Write-WtSuccess "Audit approved — ForestProductDPPCredential issued to Sales Manager wallet $($state.wallets.salesMgr)"
    Write-WtInfo "  Certification scheme: $($action2Payload.certificationScheme)"
    Write-WtInfo "  Sustainability score: $($action2Payload.sustainabilityScore)/100"
    Write-WtInfo "  Verified embodied carbon: $($action2Payload.verifiedEmbodiedCarbonKgCO2e) kg CO2e/m^3"
    Write-WtInfo "  Expires: $($action2Payload.expiryDate)"
} else {
    Write-WtSuccess "Audit declined — no credential issued"
    Write-WtInfo "  Reason: $($action2Payload.declineReason)"
}

# Persist instanceId so subsequent runs can reference it
$state | Add-Member -NotePropertyName "lastInstanceId" -NotePropertyValue $instanceId -Force
$state | Add-Member -NotePropertyName "lastScenario" -NotePropertyValue $Scenario -Force
$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile

Write-WtBanner "ForestryCertification — Run Complete ($Scenario)"
