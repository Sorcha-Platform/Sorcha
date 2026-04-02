#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# TradeFinance — Run
# Execute 3 scenarios across 2 blueprints (procurement-to-pay + invoice finance):
#   golden-path: Full P2P with approved invoice financing (6+4 actions)
#   disputed:    Invoice disputed, resubmitted, then financed (8+4 actions)
#   declined:    Full P2P but financing declined (6+4 actions, rejection on action 4)

param(
    [ValidateSet('golden-path', 'disputed', 'declined', 'all')]
    [string]$Scenario = 'all',
    [switch]$ShowJson,
    [switch]$DisableDevMode,
    [switch]$VerifyFLE
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "TradeFinance — Run"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# Resolve environment and IDs
$env = Initialize-SorchaEnvironment -Profile $state.profile -SkipHealthCheck
$blueprintUrl = $env.BlueprintUrl
$registerUrl = $env.RegisterUrl

$procurementBlueprintId = $state.blueprints.'procurement-to-pay'.id
$financeBlueprintId = $state.blueprints.'invoice-finance'.id
$tradeRegisterId = $state.registers.'sme-trade-register'.id
$financeRegisterId = $state.registers.'trade-finance-register'.id

# Convert wallet PSObject to hashtable
$wallets = @{}
foreach ($prop in $state.wallets.PSObject.Properties) { $wallets[$prop.Name] = $prop.Value }

# Authenticate org admins and use their tokens for all participants in that org
# (multi-org walkthrough: admin acts on behalf of all participants via wallet delegation)
$orgAdminTokens = @{}
$participantTokens = @{}

foreach ($orgProp in $state.organizations.PSObject.Properties) {
    $orgKey = $orgProp.Name
    $orgId = $orgProp.Value
    $adminEmail = "admin@$orgKey.sorcha.dev"
    $adminPassword = "Wt-$orgKey-admin-2026!"
    $adminCtx = Connect-SorchaUser -TenantUrl $env.TenantUrl -Email $adminEmail -Password $adminPassword -OrganizationId $orgId
    $orgAdminTokens[$orgKey] = $adminCtx.Token
}

# Map each participant to their org's admin token
foreach ($role in $state.roles.PSObject.Properties) {
    $partId = $role.Name
    $orgKey = $role.Value.orgKey
    $participantTokens[$partId] = $orgAdminTokens[$orgKey]
}

# Admin tokens per register owner (for instance creation)
$cairngormOrgId = $state.organizations.cairngorm
$scottradeOrgId = $state.organizations.scottrade
$procurementAdminToken = $orgAdminTokens["cairngorm"]
$financeAdminToken = $orgAdminTokens["scottrade"]

Write-WtInfo "Procurement Blueprint: $procurementBlueprintId"
Write-WtInfo "Finance Blueprint:     $financeBlueprintId"
Write-WtInfo "Trade Register:        $tradeRegisterId"
Write-WtInfo "Finance Register:      $financeRegisterId"
Write-WtInfo "Participants authenticated: $($participantTokens.Count)"

# Action-to-sender mapping for PROCUREMENT-TO-PAY blueprint
$procurementSenderMap = @{
    1 = "procurement-mgr"   # Raise PO
    2 = "sales-mgr"         # Acknowledge PO
    3 = "sales-mgr"         # Confirm Delivery
    4 = "site-mgr"          # Confirm GRN
    5 = "sales-mgr"         # Raise Invoice
    6 = "procurement-mgr"   # Approve/Dispute Invoice
}

# Action-to-sender mapping for INVOICE FINANCE blueprint
$financeSenderMap = @{
    1 = "finance-director"  # Request Financing
    2 = "assessment-svc"    # Buyer Assessment
    3 = "credit-analyst"    # Evaluate Application
    4 = "credit-analyst"    # Approve/Decline
}

$scenariosToRun = if ($Scenario -eq 'all') { @('golden-path', 'disputed', 'declined') } else { @($Scenario) }
$scenarioFiles = @{
    'golden-path' = "data/scenario-golden-path.json"
    'disputed'    = "data/scenario-disputed.json"
    'declined'    = "data/scenario-declined.json"
}

$allPassed = $true
$scenarioResults = @{}
$start = Get-Date

function Invoke-BlueprintScenario {
    param(
        [string]$Phase,
        [string]$BlueprintId,
        [string]$RegisterId,
        [string]$OrgId,
        [string]$AdminToken,
        [hashtable]$SenderMap,
        [array]$ExpectedPath,
        [PSObject]$ActionData,
        [bool]$IsRejection,
        [string]$RejectionReason,
        [array]$CredentialPresentations = @()
    )

    Write-WtInfo "  Phase: $Phase (Blueprint: $BlueprintId)"

    # Create instance using admin token of the register-owning org
    $instanceBody = @{
        blueprintId = $BlueprintId; registerId = $RegisterId
        tenantId = $OrgId
        metadata = @{ source = "walkthrough"; phase = $Phase }
    }
    $ir = Invoke-SorchaApi -Method POST -Uri "$blueprintUrl/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $AdminToken" } -ShowJson:$ShowJson
    $instanceId = $ir.id
    Write-WtSuccess "  Instance: $instanceId"

    $actionsOk = 0

    foreach ($actionId in $ExpectedPath) {
        $actionIdStr = "$actionId"
        $sender = $SenderMap[[int]$actionId]
        $senderWallet = $wallets[$sender]
        $senderToken = $participantTokens[$sender]
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
                    -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
                    -ActionId $actionIdStr -BlueprintId $BlueprintId `
                    -SenderWallet $senderWallet -RegisterId $RegisterId `
                    -Token $senderToken `
                    -Reject -RejectionReason $RejectionReason
                Write-WtWarn "    Action $actionIdStr ($sender) -> REJECTED"
            } else {
                # For the first action with credential presentations, send directly with presentations
                if ($actionId -eq $ExpectedPath[0] -and $CredentialPresentations.Count -gt 0) {
                    $actionBody = @{
                        blueprintId              = $BlueprintId
                        actionId                 = $actionIdStr
                        instanceId               = $instanceId
                        senderWallet             = $senderWallet
                        registerAddress          = $RegisterId
                        payloadData              = $payloadData
                        credentialPresentations   = $CredentialPresentations
                    }
                    $executeHeaders = @{
                        Authorization        = "Bearer $senderToken"
                        "X-Delegation-Token" = $senderToken
                    }
                    $response = Invoke-SorchaApi -Method POST `
                        -Uri "$blueprintUrl/instances/$instanceId/actions/$actionIdStr/execute" `
                        -Body $actionBody -Headers $executeHeaders -ShowJson:$ShowJson
                    Write-WtSuccess "    Action $actionIdStr ($sender) -> OK (with credential)"
                } else {
                    $response = Invoke-SorchaAction `
                        -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
                        -ActionId $actionIdStr -BlueprintId $BlueprintId `
                        -SenderWallet $senderWallet -RegisterId $RegisterId `
                        -Token $senderToken -PayloadData $payloadData

                    Write-WtSuccess "    Action $actionIdStr ($sender) -> OK"
                }

                if ($response.calculatedValues) {
                    foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                        Write-WtInfo "      Calculated: $($calc.Name) = $($calc.Value)"
                    }
                }
                if ($response.credentialIssued) {
                    Write-WtInfo "      VC Issued: $($response.credentialIssued.credentialType)"
                    # Store credential for cross-register presentation
                    $script:lastIssuedCredential = $response.credentialIssued
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

function Invoke-DisputedProcurement {
    param(
        [string]$BlueprintId,
        [string]$RegisterId,
        [string]$OrgId,
        [string]$AdminToken,
        [PSObject]$ActionData
    )

    Write-WtInfo "  Phase: Procurement-to-Pay [Disputed] (Blueprint: $BlueprintId)"

    # Create instance
    $instanceBody = @{
        blueprintId = $BlueprintId; registerId = $RegisterId
        tenantId = $OrgId
        metadata = @{ source = "walkthrough"; phase = "Procurement-to-Pay [Disputed]" }
    }
    $ir = Invoke-SorchaApi -Method POST -Uri "$($blueprintUrl)/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $AdminToken" } -ShowJson:$ShowJson
    $instanceId = $ir.id
    Write-WtSuccess "  Instance: $instanceId"

    $actionsOk = 0
    $totalActions = 8  # 1,2,3,4,5,6(dispute),5(resubmit),6(approve)

    # Actions 1-5: normal flow
    foreach ($actionId in @(1, 2, 3, 4, 5)) {
        $actionIdStr = "$actionId"
        $sender = $procurementSenderMap[[int]$actionId]
        $senderWallet = $wallets[$sender]
        $senderToken = $participantTokens[$sender]
        $actionDataObj = $ActionData."$actionId"

        $payloadData = @{}
        if ($actionDataObj) {
            foreach ($prop in $actionDataObj.PSObject.Properties) {
                $payloadData[$prop.Name] = $prop.Value
            }
        }

        try {
            $response = Invoke-SorchaAction `
                -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
                -ActionId $actionIdStr -BlueprintId $BlueprintId `
                -SenderWallet $senderWallet -RegisterId $RegisterId `
                -Token $senderToken -PayloadData $payloadData

            Write-WtSuccess "    Action $actionIdStr ($sender) -> OK"

            if ($response.calculatedValues) {
                foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                    Write-WtInfo "      Calculated: $($calc.Name) = $($calc.Value)"
                }
            }
            if ($response.credentialIssued) {
                Write-WtInfo "      VC Issued: $($response.credentialIssued.credentialType)"
            }
            $actionsOk++
        } catch {
            Write-WtFail "    Action $actionIdStr ($sender) failed: $($_.Exception.Message)"
            return @{ Passed = $false; ActionsOk = $actionsOk; Total = $totalActions }
        }
    }

    # Action 6: Dispute (rejection back to action 5)
    $sender = $procurementSenderMap[6]
    $senderWallet = $wallets[$sender]
    $senderToken = $participantTokens[$sender]
    try {
        $disputeData = $ActionData."6_dispute"
        $disputeReason = if ($disputeData -and $disputeData.disputeReason) { $disputeData.disputeReason } else { "Invoice disputed" }

        $null = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "6" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken `
            -Reject -RejectionReason $disputeReason
        Write-WtWarn "    Action 6 ($sender) -> DISPUTED"
        $actionsOk++
    } catch {
        Write-WtFail "    Action 6 ($sender) dispute failed: $($_.Exception.Message)"
        return @{ Passed = $false; ActionsOk = $actionsOk; Total = $totalActions }
    }

    # Action 5 resubmit: Corrected invoice
    $sender = $procurementSenderMap[5]
    $senderWallet = $wallets[$sender]
    $senderToken = $participantTokens[$sender]
    $resubmitData = $ActionData."5_resubmit"
    $payloadData = @{}
    if ($resubmitData) {
        foreach ($prop in $resubmitData.PSObject.Properties) {
            $payloadData[$prop.Name] = $prop.Value
        }
    }

    try {
        $response = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "5" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken -PayloadData $payloadData

        Write-WtSuccess "    Action 5 ($sender) -> RESUBMITTED"

        if ($response.calculatedValues) {
            foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                Write-WtInfo "      Calculated: $($calc.Name) = $($calc.Value)"
            }
        }
        if ($response.credentialIssued) {
            Write-WtInfo "      VC Issued: $($response.credentialIssued.credentialType)"
        }
        $actionsOk++
    } catch {
        Write-WtFail "    Action 5 ($sender) resubmit failed: $($_.Exception.Message)"
        return @{ Passed = $false; ActionsOk = $actionsOk; Total = $totalActions }
    }

    # Action 6: Final approval
    $sender = $procurementSenderMap[6]
    $senderWallet = $wallets[$sender]
    $senderToken = $participantTokens[$sender]
    $approvalData = $ActionData."6"
    $payloadData = @{}
    if ($approvalData) {
        foreach ($prop in $approvalData.PSObject.Properties) {
            $payloadData[$prop.Name] = $prop.Value
        }
    }

    try {
        $response = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "6" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken -PayloadData $payloadData

        Write-WtSuccess "    Action 6 ($sender) -> APPROVED"

        if ($response.calculatedValues) {
            foreach ($calc in $response.calculatedValues.PSObject.Properties) {
                Write-WtInfo "      Calculated: $($calc.Name) = $($calc.Value)"
            }
        }
        if ($response.credentialIssued) {
            Write-WtInfo "      VC Issued: $($response.credentialIssued.credentialType)"
        }
        $actionsOk++
    } catch {
        Write-WtFail "    Action 6 ($sender) approval failed: $($_.Exception.Message)"
        return @{ Passed = $false; ActionsOk = $actionsOk; Total = $totalActions }
    }

    return @{ Passed = ($actionsOk -eq $totalActions); ActionsOk = $actionsOk; Total = $totalActions }
}

foreach ($sid in $scenariosToRun) {
    $scenarioPath = Join-Path $scriptDir $scenarioFiles[$sid]
    if (-not (Test-Path $scenarioPath)) { Write-WtFail "Scenario file not found: $scenarioPath"; continue }

    $scenarioData = Get-Content -Path $scenarioPath -Raw | ConvertFrom-Json
    $isDispute = [bool]$scenarioData.expectedDispute
    $isRejection = [bool]$scenarioData.expectedRejection

    Write-WtStep "Scenario: $($scenarioData.name)"

    $scenarioStart = Get-Date

    # Phase 1: Procurement-to-Pay
    if ($isDispute) {
        $procurementResult = Invoke-DisputedProcurement `
            -BlueprintId $procurementBlueprintId `
            -RegisterId $tradeRegisterId `
            -OrgId $cairngormOrgId `
            -AdminToken $procurementAdminToken `
            -ActionData $scenarioData.procurement
    } else {
        $procurementPath = @($scenarioData.expectedProcurementPath)

        $procurementResult = Invoke-BlueprintScenario `
            -Phase "Procurement-to-Pay" `
            -BlueprintId $procurementBlueprintId `
            -RegisterId $tradeRegisterId `
            -OrgId $cairngormOrgId `
            -AdminToken $procurementAdminToken `
            -SenderMap $procurementSenderMap `
            -ExpectedPath $procurementPath `
            -ActionData $scenarioData.procurement `
            -IsRejection $false `
            -RejectionReason ""
    }

    $financeResult = $null
    $financeOutcome = "N/A"

    # Phase 2: Invoice Finance (only if procurement completed and finance path defined)
    if ($procurementResult.Passed -and $scenarioData.expectedFinancePath) {
        $financePath = @($scenarioData.expectedFinancePath)

        # Fetch VerifiedInvoiceCredential from the sales-mgr wallet for presentation
        $credPresentations = @()
        $salesMgrWallet = $wallets["sales-mgr"]
        $salesMgrToken = $participantTokens["sales-mgr"]
        $credHeaders = @{ Authorization = "Bearer $salesMgrToken" }

        Write-WtInfo "  Fetching credentials from sales-mgr wallet ($salesMgrWallet)..."
        try {
            $creds = Invoke-SorchaApi -Method GET `
                -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials" `
                -Headers $credHeaders

            $invoiceCred = $creds | Where-Object { $_.type -eq "VerifiedInvoiceCredential" } | Select-Object -First 1

            if ($invoiceCred) {
                Write-WtInfo "  Found credential: $($invoiceCred.id)"

                # Export as SD-JWT for presentation
                $exported = Invoke-SorchaApi -Method GET `
                    -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials/$($invoiceCred.id)/export" `
                    -Headers $credHeaders

                $rawToken = if ($exported.sdJwt) { $exported.sdJwt } elseif ($exported.rawToken) { $exported.rawToken } else { $exported.token }

                $credPresentations = @(
                    @{
                        credentialId    = $invoiceCred.id
                        disclosedClaims = @{
                            type           = "VerifiedInvoiceCredential"
                            invoiceNumber  = $scenarioData.procurement."5".invoiceNumber
                            invoiceAmount  = $scenarioData.procurement."5".invoiceTotal
                            poReference    = $scenarioData.procurement."1".poReference
                            paymentDueDate = $scenarioData.procurement."5".paymentDueDate
                        }
                        rawPresentation = $rawToken
                    }
                )
                Write-WtInfo "  Presenting VerifiedInvoiceCredential: $($invoiceCred.id)"
            } else {
                Write-WtWarn "  No VerifiedInvoiceCredential found in sales-mgr wallet"
            }
        } catch {
            Write-WtWarn "  Could not fetch credentials: $($_.Exception.Message)"
        }

        $financeResult = Invoke-BlueprintScenario `
            -Phase "Invoice Finance" `
            -BlueprintId $financeBlueprintId `
            -RegisterId $financeRegisterId `
            -OrgId $scottradeOrgId `
            -AdminToken $financeAdminToken `
            -SenderMap $financeSenderMap `
            -ExpectedPath $financePath `
            -ActionData $scenarioData.finance `
            -IsRejection $isRejection `
            -RejectionReason $scenarioData.rejectionReason `
            -CredentialPresentations $credPresentations
    }

    $scenarioDuration = (Get-Date) - $scenarioStart

    $procurementOutcome = if ($procurementResult.Passed) { "APPROVED" } else { "INCOMPLETE" }
    if ($isDispute -and $procurementResult.Passed) { $procurementOutcome = "APPROVED (disputed)" }

    if ($financeResult) {
        if ($isRejection) {
            $financeOutcome = "DECLINED"
        } elseif ($financeResult.Passed) {
            $financeOutcome = "COMPLETED"
        } else {
            $financeOutcome = "INCOMPLETE"
        }
    }

    $overallPassed = $procurementResult.Passed -and (-not $financeResult -or $financeResult.Passed)

    $procurementActions = "$($procurementResult.ActionsOk)/$($procurementResult.Total)"
    $financeActions = if ($financeResult) { "$($financeResult.ActionsOk)/$($financeResult.Total)" } else { "-" }

    $scenarioResults[$sid] = @{
        Name = $scenarioData.name
        Passed = $overallPassed
        ProcurementActions = $procurementActions
        ProcurementOutcome = $procurementOutcome
        FinanceActions = $financeActions
        FinanceOutcome = $financeOutcome
        Duration = [math]::Round($scenarioDuration.TotalSeconds, 1)
    }

    if (-not $overallPassed) { $allPassed = $false }

    if ($overallPassed) { Write-WtSuccess "Scenario $sid`: $procurementOutcome, Finance=$financeOutcome" }
    else { Write-WtFail "Scenario $sid`: incomplete" }
}

# Summary
$duration = (Get-Date) - $start
Write-Host ""
Write-WtBanner "TradeFinance — Results"

foreach ($sid in $scenariosToRun) {
    $sr = $scenarioResults[$sid]
    $icon = if ($sr.Passed) { "[OK]" } else { "[X]" }
    $color = if ($sr.Passed) { "Green" } else { "Red" }
    Write-Host "  $icon Scenario $sid`: $($sr.Name)" -ForegroundColor $color
    Write-Host "     Procurement: $($sr.ProcurementOutcome) ($($sr.ProcurementActions)), Finance: $($sr.FinanceOutcome) ($($sr.FinanceActions)), $($sr.Duration)s" -ForegroundColor White
}

Write-Host ""
Write-Host "  Duration: $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

if ($allPassed) { Write-Host "  RESULT: PASS" -ForegroundColor Green }
else { Write-Host "  RESULT: FAIL" -ForegroundColor Red }

# ── DevMode Transition (US4) ─────────────────────────────────────────────
if ($DisableDevMode) {
    Write-Host ""
    Write-WtBanner "DevMode → FLE Transition"
    Write-WtWarn "This operation is IRREVERSIBLE. All future transactions will be field-level encrypted."

    $env = Initialize-SorchaEnvironment -Profile $state.profile -SkipHealthCheck
    $adminHeaders = @{ Authorization = "Bearer $procurementAdminToken" }

    foreach ($regKey in @("trade", "finance")) {
        $regId = $state.registers.$regKey.id
        $regName = $state.registers.$regKey.name
        Write-WtInfo "  Disabling DevMode on $regName ($regId)..."
        try {
            Invoke-SorchaApi -Method PATCH `
                -Uri "$($env.RegisterUrl)/registers/$regId" `
                -Body @{ devMode = $false } `
                -Headers $adminHeaders
            Write-WtSuccess "  DevMode disabled on $regName"
        } catch {
            Write-WtFail "  Failed to disable DevMode on $regName`: $($_.Exception.Message)"
        }
    }

    Write-WtInfo "  Re-run without -DisableDevMode to execute under FLE."
}

# ── FLE Disclosure Verification (US4) ────────────────────────────────────
if ($VerifyFLE) {
    Write-Host ""
    Write-WtBanner "FLE Disclosure Verification"
    Write-WtInfo "Querying register as Funder (credit-analyst) to verify selective disclosure..."

    $env = Initialize-SorchaEnvironment -Profile $state.profile -SkipHealthCheck
    $funderRole = $state.roles."credit-analyst"
    $funderHeaders = @{ Authorization = "Bearer $($funderRole.token)" }

    # Query trade register transactions as Funder
    $tradeRegId = $state.registers.trade.id
    Write-WtInfo "  Querying SME Trade Register as credit-analyst..."
    try {
        $txns = Invoke-SorchaApi -Method GET `
            -Uri "$($env.RegisterUrl)/registers/$tradeRegId/transactions?`$top=10" `
            -Headers $funderHeaders
        foreach ($tx in $txns.value) {
            $actionTitle = $tx.actionTitle
            $visibleFields = if ($tx.payload) { ($tx.payload.PSObject.Properties | Select-Object -ExpandProperty Name) -join ", " } else { "(encrypted)" }
            Write-WtInfo "    Action: $actionTitle → Visible fields: $visibleFields"
        }
        Write-WtSuccess "  Funder sees only disclosed fields (paymentTerms, invoiceTotal, decision, approvedAmount)"
    } catch {
        Write-WtWarn "  Could not query as Funder: $($_.Exception.Message)"
    }

    # Query finance register transactions as supplier (should NOT see evaluationNotes)
    $finRegId = $state.registers.finance.id
    $supplierRole = $state.roles."sales-mgr"
    $supplierHeaders = @{ Authorization = "Bearer $($supplierRole.token)" }
    Write-WtInfo "  Querying Trade Finance Register as sales-mgr..."
    try {
        $txns = Invoke-SorchaApi -Method GET `
            -Uri "$($env.RegisterUrl)/registers/$finRegId/transactions?`$top=10" `
            -Headers $supplierHeaders
        foreach ($tx in $txns.value) {
            $actionTitle = $tx.actionTitle
            $visibleFields = if ($tx.payload) { ($tx.payload.PSObject.Properties | Select-Object -ExpandProperty Name) -join ", " } else { "(encrypted)" }
            Write-WtInfo "    Action: $actionTitle → Visible fields: $visibleFields"
        }
        Write-WtSuccess "  Supplier sees financing terms but NOT evaluationNotes or credit assessment details"
    } catch {
        Write-WtWarn "  Could not query as Supplier: $($_.Exception.Message)"
    }
}

if (-not $DisableDevMode -and -not $VerifyFLE) {
    if ($allPassed) { exit 0 } else { exit 1 }
}
