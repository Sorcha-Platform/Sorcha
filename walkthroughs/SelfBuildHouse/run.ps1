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

# Convert roles PSObject to hashtable
$roles = @{}
foreach ($prop in $state.roles.PSObject.Properties) {
    $r = $prop.Value
    $roles[$prop.Name] = @{
        email          = $r.email
        password       = $r.password
        organizationId = $r.organizationId
        orgKey         = $r.orgKey
        walletAddress  = $r.walletAddress
    }
}

# Login each role up-front and cache per-role tokens. Every subsequent action
# submission goes out under the sending role's own user token — NOT a shared
# admin token — so the audit trail ties every transaction back to the real
# user who performed it.
Write-WtStep "Authenticating (per-role login)"
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

    # Create the instance under the token of the first-action sender (the
    # self-builder in both phases). That user's org becomes the tenant on
    # the instance, and instance metadata reflects them — not the sysadmin.
    $starterRole = $SenderMap[[int]$ExpectedPath[0]]
    $starterToken = $roleTokenCache[$starterRole]
    $starterOrgId = $roles[$starterRole].organizationId

    $instanceBody = @{
        blueprintId = $BlueprintId; registerId = $RegisterId
        tenantId = $starterOrgId
        metadata = @{ source = "walkthrough"; phase = $Phase }
    }
    $ir = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $starterToken" } -ShowJson:$ShowJson
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

        # Use the sender's own token — their user JWT — for this action so
        # the signed submission matches the participant identity on the
        # register.
        $senderToken = $roleTokenCache[$sender]

        # Feature 145: submission is single-async — the previous action's /execute returned 202 and
        # the instance advances only when the InstanceProjector folds the sealed docket. Gate each
        # subsequent action on the projection surfacing it as current, else we race the projector and
        # get a 400 ("Action N is not a current action"). See walkthrough-builder skill, Cadence.
        if ($actionId -ne $ExpectedPath[0]) {
            Wait-SorchaActorReady -Mode AwaitingInbox `
                -InstanceId $instanceId -ActionId ([int]$actionId) -RegisterId $RegisterId `
                -Headers @{ Authorization = "Bearer $senderToken" } `
                -GatewayUrl ($state.blueprintUrl -replace '/api$', '')
        }

        try {
            if ($isRejectionAction) {
                $null = Invoke-SorchaAction `
                    -BlueprintUrl $state.blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $BlueprintId `
                    -SenderWallet $senderWallet -RegisterId $RegisterId `
                    -Token $senderToken `
                    -Reject -RejectionReason $RejectionReason `
                    -WaitForSeal
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
                    -Token $senderToken -PayloadData $payloadData `
                    -CredentialPresentations $presentations `
                    -WaitForSeal

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

    # Snapshot the self-builder's credentials of each type BEFORE the phase that issues them.
    # This wallet accumulates one Planning and one Warrant credential per run, so selecting
    # "first of this type" presents whichever is OLDEST — a credential from an earlier run,
    # carrying an earlier run's claims, against this run's scenario data. That is #1503's
    # failure shape (and #1477 defect 2, and #1483): the platform then refuses it, or accepts
    # data nobody asserted, and the walkthrough blames the platform.
    $sbWallet   = $wallets["self-builder"]
    $sbToken    = $roleTokenCache["self-builder"]
    $sbListUri  = Get-SorchaWalletCredentialUri -WalletUrl $state.walletUrl -WalletAddress $sbWallet
    $sbHeaders  = @{ Authorization = "Bearer $sbToken" }
    $planningBefore = Get-SorchaCredentialIdSnapshot -ListUri $sbListUri -Headers $sbHeaders `
        -CredentialType "https://sorcha.dev/vc/planning-permission/v1"

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

        # The warrant credential is issued by warrant action 4, so snapshot BEFORE the phase
        # runs — anything of that type already present belongs to an earlier run.
        $warrantBefore = Get-SorchaCredentialIdSnapshot -ListUri $sbListUri -Headers $sbHeaders `
            -CredentialType "https://sorcha.dev/vc/building-warrant/v1"

        # Lazy credential fetcher: pulls the correct VC just-in-time per
        # action. Action 1 needs PlanningPermissionCredential; actions 5-7
        # (staged inspections) need BuildingWarrantCredential, which isn't
        # issued until warrant action 4 completes — so fetch on demand
        # rather than up-front.
        # The credentials are held in the self-builder's wallet, so we hit
        # the wallet API with the self-builder's own token rather than a
        # shared admin token. Authorisation stays scoped to the holder.
        #
        # Each type is resolved to the credential THIS run issued (absent from the pre-phase
        # snapshot) and then pinned by id. Resolved ids are cached because actions 5-7 all ask
        # for the warrant credential and the delivery poll should run once, not three times.
        $selfBuilderWallet = $sbWallet
        $walletUrl = $state.walletUrl
        $selfBuilderToken = $sbToken
        $resolvedCredIds = @{}
        $warrantFetcher = {
            param($actionId)
            $aid = [int]$actionId

            $credType = if ($aid -eq 1) { "https://sorcha.dev/vc/planning-permission/v1" }
                        elseif ($aid -ge 5) { "https://sorcha.dev/vc/building-warrant/v1" }
                        else { $null }
            if (-not $credType) { return $null }

            $exclude = if ($aid -eq 1) { $planningBefore } else { $warrantBefore }

            if (-not $resolvedCredIds.ContainsKey($credType)) {
                $fresh = Wait-SorchaNewCredential -ListUri $sbListUri -Headers $sbHeaders `
                    -CredentialType $credType -ExcludeIds $exclude -TimeoutSeconds 60
                if (-not $fresh) {
                    Write-WtWarn "    No NEW $credType reached the self-builder wallet — not presenting a credential from an earlier run."
                    return $null
                }
                $resolvedCredIds[$credType] = $fresh.id
                Write-WtInfo "    Pinned $credType issued by this run: $($fresh.id)"
            }

            $p = Get-SorchaCredentialPresentation -WalletUrl $walletUrl `
                -WalletAddress $selfBuilderWallet `
                -CredentialType $credType `
                -CredentialId $resolvedCredIds[$credType] `
                -Token $selfBuilderToken
            if ($p) { return @($p) }
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
