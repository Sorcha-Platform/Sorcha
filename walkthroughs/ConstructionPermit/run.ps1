#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ConstructionPermit — Run (Multi-Org)
# Execute 3 scenarios: A (low-risk), B (high-risk), C (rejection).
# Each action is executed using the per-user token for that participant.

param(
    [ValidateSet('A', 'B', 'C', 'all')]
    [string]$Scenario = 'all',
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ConstructionPermit — Run (Multi-Org)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# Convert PSObject maps to hashtables
$wallets = @{}
foreach ($prop in $state.wallets.PSObject.Properties) { $wallets[$prop.Name] = $prop.Value }

$roles = @{}
foreach ($prop in $state.roles.PSObject.Properties) {
    $r = $prop.Value
    $roles[$prop.Name] = @{
        organizationId = $r.organizationId
        walletAddress  = $r.walletAddress
        orgKey         = $r.orgKey
        email          = $r.email
        password       = $r.password
    }
}

$orgs = @{}
foreach ($prop in $state.organizations.PSObject.Properties) { $orgs[$prop.Name] = $prop.Value }

# Login each user and cache their tokens (per-role, not per-org)
Write-WtStep "Authenticating (per-user login)"
$roleTokenCache = @{}

foreach ($role in $roles.Keys) {
    $r = $roles[$role]
    $session = Connect-SorchaUser `
        -TenantUrl $state.tenantUrl `
        -Email $r.email `
        -Password $r.password `
        -OrganizationId $r.organizationId
    $roleTokenCache[$role] = $session.Token
}

# Action-to-sender mapping (matches blueprint action IDs to participant roles)
$actionSenderMap = @{
    1 = "contractor"
    2 = "structural-engineer"
    3 = "planning-officer"
    4 = "environmental-assessor"
    5 = "building-control"
    6 = "planning-officer"
}

$scenariosToRun = if ($Scenario -eq 'all') { @('A', 'B', 'C') } else { @($Scenario) }
$scenarioFiles = @{
    'A' = "data/scenario-a-low-risk.json"
    'B' = "data/scenario-b-high-risk.json"
    'C' = "data/scenario-c-rejection.json"
}

$allPassed = $true
$scenarioResults = @{}
$start = Get-Date

foreach ($sid in $scenariosToRun) {
    $scenarioPath = Join-Path $scriptDir $scenarioFiles[$sid]
    if (-not (Test-Path $scenarioPath)) { Write-WtFail "Scenario file not found: $scenarioPath"; continue }

    $scenarioData = Get-Content -Path $scenarioPath -Raw | ConvertFrom-Json
    $expectedPath = @($scenarioData.expectedPath)
    $isRejection = [bool]$scenarioData.expectedRejection

    Write-WtStep "Scenario $sid`: $($scenarioData.name)"

    # Use contractor's token for instance creation
    $contractorToken = $roleTokenCache["contractor"]
    $contractorOrgId = $roles["contractor"].organizationId

    $instanceBody = @{
        blueprintId = $state.blueprintId; registerId = $state.registerId
        tenantId = $contractorOrgId
        metadata = @{ source = "walkthrough"; scenario = $sid; scenarioName = $scenarioData.name }
    }
    $ir = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $contractorToken" } -ShowJson:$ShowJson
    $instanceId = $ir.id
    Write-WtSuccess "Instance: $instanceId"

    $actionsOk = 0
    $scenarioStart = Get-Date

    $prevActionId = $null
    foreach ($actionId in $expectedPath) {
        $actionIdStr = "$actionId"
        $sender = $actionSenderMap[[int]$actionId]
        $senderWallet = $wallets[$sender]
        $senderToken = $roleTokenCache[$sender]

        $actionData = $scenarioData.actions."$actionId"

        # Convert PSObject to hashtable
        $payloadData = @{}
        if ($actionData) {
            foreach ($prop in $actionData.PSObject.Properties) {
                $payloadData[$prop.Name] = $prop.Value
            }
        }

        # Wait for this action to become current (transaction pipeline is async)
        if ($prevActionId) {
            $maxWait = 30
            $waited = 0
            $ready = $false
            while ($waited -lt $maxWait) {
                $inst = Invoke-SorchaApi -Method GET `
                    -Uri "$($state.blueprintUrl)/instances/$instanceId" `
                    -Headers @{ Authorization = "Bearer $contractorToken" }
                if ($inst.currentActionIds -contains [int]$actionId) {
                    $ready = $true
                    break
                }
                Start-Sleep -Seconds 1
                $waited++
            }
            if (-not $ready) {
                Write-WtFail "Timeout waiting for action $actionIdStr to become current (waited ${maxWait}s)"
                $allPassed = $false
                break
            }
            Write-WtInfo "  Action $actionIdStr ready (waited ${waited}s)"
        }

        $isLastAction = ($actionId -eq $expectedPath[-1])
        $isRejectionAction = $isRejection -and $isLastAction

        try {
            if ($isRejectionAction) {
                $null = Invoke-SorchaAction `
                    -BlueprintUrl $state.blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $state.blueprintId `
                    -SenderWallet $senderWallet -RegisterId $state.registerId `
                    -Token $senderToken `
                    -Reject -RejectionReason $scenarioData.rejectionReason
            } else {
                $response = Invoke-SorchaAction `
                    -BlueprintUrl $state.blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $state.blueprintId `
                    -SenderWallet $senderWallet -RegisterId $state.registerId `
                    -Token $senderToken -PayloadData $payloadData

                if ($response.calculatedValues) {
                    foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                        Write-WtInfo "  Calculated: $($calc.Name) = $($calc.Value)"
                    }
                }
            }
            $actionsOk++
            $prevActionId = $actionId
            Write-WtInfo "  ($sender via per-user token)"
        } catch {
            Write-WtFail "Action $actionIdStr ($sender) failed: $($_.Exception.Message)"
            # Try to get more detail
            try {
                $errBody = Get-SorchaErrorBody -Exception $_.Exception
                if ($errBody) { Write-WtWarn "  Detail: $errBody" }
            } catch {}
            $allPassed = $false
            break
        }
    }

    $scenarioDuration = (Get-Date) - $scenarioStart
    $scenarioPassed = ($actionsOk -eq $expectedPath.Count)
    $outcome = if ($isRejection) { "REJECTED" } else { "APPROVED" }

    $scenarioResults[$sid] = @{
        Name = $scenarioData.name; Passed = $scenarioPassed
        Actions = "$actionsOk/$($expectedPath.Count)"; Outcome = $outcome
        Duration = [math]::Round($scenarioDuration.TotalSeconds, 1)
    }

    if ($scenarioPassed) { Write-WtSuccess "Scenario $sid`: $outcome ($actionsOk actions)" }
    else { Write-WtFail "Scenario $sid`: incomplete"; $allPassed = $false }
}

# Summary
$duration = (Get-Date) - $start
Write-Host ""
Write-WtBanner "ConstructionPermit — Results"

foreach ($sid in $scenariosToRun) {
    $sr = $scenarioResults[$sid]
    $icon = if ($sr.Passed) { "[OK]" } else { "[X]" }
    $color = if ($sr.Passed) { "Green" } else { "Red" }
    Write-Host "  $icon Scenario $sid`: $($sr.Name) ($($sr.Outcome), $($sr.Actions), $($sr.Duration)s)" -ForegroundColor $color
}

Write-Host ""
Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

if ($allPassed) { Write-Host "  RESULT: PASS" -ForegroundColor Green; exit 0 }
else { Write-Host "  RESULT: FAIL" -ForegroundColor Red; exit 1 }
