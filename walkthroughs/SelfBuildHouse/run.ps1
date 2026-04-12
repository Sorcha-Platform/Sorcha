#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# SelfBuildHouse — Run
# Execute 3 scenarios across 2 blueprints (planning permission + building warrant):
#   A: Happy path (bungalow, 6+7 actions, 3 VCs)
#   B: Protected species (woodland, 7+7 actions, 3 VCs)
#   C: Refused (conservation area, 5 actions, 0 VCs)

param(
    [ValidateSet('A', 'B', 'C', 'all')]
    [string]$Scenario = 'all',
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "SelfBuildHouse — Run"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# Convert wallet PSObject to hashtable
$wallets = @{}
foreach ($prop in $state.wallets.PSObject.Properties) { $wallets[$prop.Name] = $prop.Value }

# Action-to-sender mapping for PLANNING PERMISSION blueprint
$planningSenderMap = @{
    1 = "self-builder"
    2 = "structural-engineer"
    3 = "ecologist"
    4 = "ecologist"
    5 = "utilities-officer"
    6 = "planning-officer"
    7 = "planning-officer"
}

# Action-to-sender mapping for BUILDING WARRANT blueprint
$warrantSenderMap = @{
    1 = "self-builder"
    2 = "structural-engineer"
    3 = "building-standards-officer"
    4 = "building-standards-officer"
    5 = "building-inspector"
    6 = "building-inspector"
    7 = "building-inspector"
}

$scenariosToRun = if ($Scenario -eq 'all') { @('A', 'B', 'C') } else { @($Scenario) }
$scenarioFiles = @{
    'A' = "data/scenario-a-happy-path.json"
    'B' = "data/scenario-b-protected-species.json"
    'C' = "data/scenario-c-refused.json"
}

$allPassed = $true
$scenarioResults = @{}
$start = Get-Date

function Invoke-BlueprintScenario {
    param(
        [string]$Phase,
        [string]$BlueprintId,
        [string]$RegisterId,
        [hashtable]$SenderMap,
        [array]$ExpectedPath,
        [PSObject]$ActionData,
        [bool]$IsRejection,
        [string]$RejectionReason,
        [scriptblock]$PresentationFetcher = $null
    )

    Write-WtInfo "  Phase: $Phase (Blueprint: $BlueprintId)"

    # Create instance
    $instanceBody = @{
        blueprintId = $BlueprintId; registerId = $RegisterId
        tenantId = $state.organizationId
        metadata = @{ source = "walkthrough"; phase = $Phase }
    }
    $ir = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $($state.adminToken)" } -ShowJson:$ShowJson
    $instanceId = $ir.id
    Write-WtSuccess "  Instance: $instanceId"

    $actionsOk = 0

    foreach ($actionId in $ExpectedPath) {
        $actionIdStr = "$actionId"
        $sender = $SenderMap[[int]$actionId]
        $senderWallet = $wallets[$sender]
        $actionDataObj = $ActionData."$actionId"

        # Convert PSObject to hashtable
        $payloadData = @{}
        if ($actionDataObj) {
            foreach ($prop in $actionDataObj.PSObject.Properties) {
                $payloadData[$prop.Name] = $prop.Value
            }
        }

        $isLastAction = ($actionId -eq $ExpectedPath[-1])
        $isRejectionAction = $IsRejection -and $isLastAction

        try {
            if ($isRejectionAction) {
                $null = Invoke-SorchaAction `
                    -BlueprintUrl $state.blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $BlueprintId `
                    -SenderWallet $senderWallet -RegisterId $RegisterId `
                    -Token $state.adminToken `
                    -Reject -RejectionReason $RejectionReason
                Write-WtWarn "    Action $actionIdStr ($sender) -> REJECTED"
            } else {
                $presentations = @()
                if ($PresentationFetcher) {
                    $fetched = & $PresentationFetcher $actionId
                    if ($fetched) { $presentations = @($fetched) }
                }

                $response = Invoke-SorchaAction `
                    -BlueprintUrl $state.blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $BlueprintId `
                    -SenderWallet $senderWallet -RegisterId $RegisterId `
                    -Token $state.adminToken -PayloadData $payloadData `
                    -CredentialPresentations $presentations

                Write-WtSuccess "    Action $actionIdStr ($sender) -> OK"

                if ($response.calculatedValues) {
                    foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                        Write-WtInfo "      Calculated: $($calc.Name) = $($calc.Value)"
                    }
                }
                if ($response.credentialIssued) {
                    Write-WtInfo "      VC Issued: $($response.credentialIssued.credentialType)"
                }
            }
            $actionsOk++
        } catch {
            Write-WtFail "    Action $actionIdStr ($sender) failed: $($_.Exception.Message)"
            return @{ Passed = $false; ActionsOk = $actionsOk; Total = $ExpectedPath.Count }
        }
    }

    return @{ Passed = ($actionsOk -eq $ExpectedPath.Count); ActionsOk = $actionsOk; Total = $ExpectedPath.Count }
}

foreach ($sid in $scenariosToRun) {
    $scenarioPath = Join-Path $scriptDir $scenarioFiles[$sid]
    if (-not (Test-Path $scenarioPath)) { Write-WtFail "Scenario file not found: $scenarioPath"; continue }

    $scenarioData = Get-Content -Path $scenarioPath -Raw | ConvertFrom-Json
    $isRejection = [bool]$scenarioData.expectedRejection

    Write-WtStep "Scenario $sid`: $($scenarioData.name)"

    $scenarioStart = Get-Date
    $planningPath = @($scenarioData.expectedPlanningPath)

    # Phase 1: Planning Permission
    $planningResult = Invoke-BlueprintScenario `
        -Phase "Planning Permission" `
        -BlueprintId $state.planningBlueprintId `
        -RegisterId $state.planningRegisterId `
        -SenderMap $planningSenderMap `
        -ExpectedPath $planningPath `
        -ActionData $scenarioData.planning `
        -IsRejection $isRejection `
        -RejectionReason $scenarioData.rejectionReason

    $warrantResult = $null
    $warrantOutcome = "N/A"

    # Phase 2: Building Warrant (only if planning was approved)
    if ($planningResult.Passed -and -not $isRejection -and $scenarioData.expectedWarrantPath) {
        $warrantPath = @($scenarioData.expectedWarrantPath)

        # Lazy credential fetcher: pulls the correct VC just-in-time per action.
        # Action 1 needs PlanningPermissionCredential; actions 5-7 (staged inspections)
        # need BuildingWarrantCredential, which isn't issued until warrant action 4
        # completes — so fetch-on-demand instead of up-front.
        $selfBuilderWallet = $wallets["self-builder"]
        $walletUrl = $state.walletUrl
        $adminToken = $state.adminToken
        $warrantFetcher = {
            param($actionId)
            $aid = [int]$actionId
            if ($aid -eq 1) {
                $p = Get-SorchaCredentialPresentation -WalletUrl $walletUrl `
                    -WalletAddress $selfBuilderWallet `
                    -CredentialType "PlanningPermissionCredential" `
                    -Token $adminToken
                if ($p) { return @($p) }
            } elseif ($aid -ge 5) {
                $p = Get-SorchaCredentialPresentation -WalletUrl $walletUrl `
                    -WalletAddress $selfBuilderWallet `
                    -CredentialType "BuildingWarrantCredential" `
                    -Token $adminToken
                if ($p) { return @($p) }
            }
            return $null
        }.GetNewClosure()

        $warrantResult = Invoke-BlueprintScenario `
            -Phase "Building Warrant" `
            -BlueprintId $state.warrantBlueprintId `
            -RegisterId $state.buildingRegisterId `
            -SenderMap $warrantSenderMap `
            -ExpectedPath $warrantPath `
            -ActionData $scenarioData.warrant `
            -IsRejection $false `
            -RejectionReason "" `
            -PresentationFetcher $warrantFetcher

        $warrantOutcome = if ($warrantResult.Passed) { "COMPLETED" } else { "INCOMPLETE" }
    }

    $scenarioDuration = (Get-Date) - $scenarioStart
    $planningOutcome = if ($isRejection) { "REFUSED" } elseif ($planningResult.Passed) { "APPROVED" } else { "INCOMPLETE" }
    $overallPassed = $planningResult.Passed -and (-not $warrantResult -or $warrantResult.Passed)

    $planningActions = "$($planningResult.ActionsOk)/$($planningResult.Total)"
    $warrantActions = if ($warrantResult) { "$($warrantResult.ActionsOk)/$($warrantResult.Total)" } else { "-" }

    $scenarioResults[$sid] = @{
        Name = $scenarioData.name
        Passed = $overallPassed
        PlanningActions = $planningActions
        PlanningOutcome = $planningOutcome
        WarrantActions = $warrantActions
        WarrantOutcome = $warrantOutcome
        Duration = [math]::Round($scenarioDuration.TotalSeconds, 1)
    }

    if (-not $overallPassed) { $allPassed = $false }

    if ($overallPassed) { Write-WtSuccess "Scenario $sid`: Planning=$planningOutcome, Warrant=$warrantOutcome" }
    else { Write-WtFail "Scenario $sid`: incomplete" }
}

# Summary
$duration = (Get-Date) - $start
Write-Host ""
Write-WtBanner "SelfBuildHouse — Results"

foreach ($sid in $scenariosToRun) {
    $sr = $scenarioResults[$sid]
    $icon = if ($sr.Passed) { "[OK]" } else { "[X]" }
    $color = if ($sr.Passed) { "Green" } else { "Red" }
    Write-Host "  $icon Scenario $sid`: $($sr.Name)" -ForegroundColor $color
    Write-Host "     Planning: $($sr.PlanningOutcome) ($($sr.PlanningActions)), Warrant: $($sr.WarrantOutcome) ($($sr.WarrantActions)), $($sr.Duration)s" -ForegroundColor White
}

Write-Host ""
Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

if ($allPassed) { Write-Host "  RESULT: PASS" -ForegroundColor Green; exit 0 }
else { Write-Host "  RESULT: FAIL" -ForegroundColor Red; exit 1 }
