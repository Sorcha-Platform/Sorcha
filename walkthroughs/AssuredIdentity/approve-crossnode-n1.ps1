#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Feature 137 Tier-2 — analyst (on n1, the register OWNER) approves Action 2 of a
# cross-node instance whose mirror was materialised by InstanceMirrorReconstructor
# from the replica-origin docket. Reads crossnode-state.json for the instance id.
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force

$state   = Get-Content (Join-Path $PSScriptRoot "crossnode-state.json") -Raw | ConvertFrom-Json
$st      = Get-Content (Join-Path $PSScriptRoot "state.json") -Raw | ConvertFrom-Json
$n1      = "https://n1.sorcha.dev"
$api     = "$n1/api"
$instanceId = $state.instanceId
$registerId = $state.registerId
$blueprintId = $state.blueprintId
$analystEmail  = $st.roles.verificationAnalyst.email
$analystPw     = $st.roles.verificationAnalyst.password
$analystOrg    = $st.roles.verificationAnalyst.organizationId
$analystWallet = $st.roles.verificationAnalyst.walletAddress

Write-Host "== Analyst login on n1 ($analystEmail) =="
$analyst = Connect-SorchaUser -TenantUrl $api -Email $analystEmail -Password $analystPw -OrganizationId $analystOrg

Write-Host "== GET instance $instanceId from n1 (mirror) =="
try {
    $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$instanceId" -Headers $analyst.Headers
    Write-Host "  state=$($inst.state) currentActionIds=$($inst.currentActionIds -join ',') isReadOnlyMirror=$($inst.isReadOnlyMirror)"
} catch { Write-Host "  GET instance failed: $($_.Exception.Message)" }

Write-Host "== Approve Action 2 (decision=approved) against n1 =="
$resp = Invoke-SorchaAction `
    -BlueprintUrl $api -InstanceId $instanceId -ActionId "2" -BlueprintId $blueprintId `
    -SenderWallet $analystWallet -RegisterId $registerId -Token $analyst.Token `
    -PayloadData @{ decision = "approved"; verificationNotes = "Cross-node identity verified." }
Write-Host "  approve response: $($resp | ConvertTo-Json -Compress -Depth 5)"
