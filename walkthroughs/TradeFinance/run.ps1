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

# Authenticate per-participant — each action submission must run under the
# participant's own user token so the participant-identity lookup in the
# action-execution service finds a real record. The legacy shape used the
# org-admin token for every participant, which 403s now because admins
# aren't registered participants (ActionExecutionService.cs:2232 — "No
# participant profile found for user ... in org ...").
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

# Login each participant user with their own credentials. state.json carries
# email + password per role from setup.ps1.
foreach ($role in $state.roles.PSObject.Properties) {
    $partId = $role.Name
    $r = $role.Value
    $session = Connect-SorchaUser `
        -TenantUrl $env.TenantUrl `
        -Email $r.email `
        -Password $r.password `
        -OrganizationId $r.organizationId
    $participantTokens[$partId] = $session.Token
}

# Admin tokens per register owner (for instance creation — register-owning
# org's admin is still the right token for creating a workflow instance on
# their register).
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

# Action-to-sender mapping for INVOICE FINANCE blueprint.
# Sales Manager submits Action 1 because they hold both the VerifiedInvoiceCredential
# (issued by procurement R1 to sales-mgr) and the optional ForestProductDPPCredential
# (issued by ForestryCertification to sales-mgr). Keeping presenter and credential
# holder on the same wallet lets the UI's New Submissions / CredentialGatePanel
# render the financing workflow without a delegation step.
$financeSenderMap = @{
    1 = "sales-mgr"         # Request Financing — presents VerifiedInvoiceCredential + optional DPP
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

        # Feature 145: submission is single-async — the previous action's /execute returned 202 and
        # the instance advances only when the InstanceProjector folds the sealed docket. Gate each
        # subsequent action on the projection surfacing it as current, else we race the projector and
        # get a 400 ("Action N is not a current action"). See walkthrough-builder skill, Cadence.
        if ($actionId -ne $ExpectedPath[0]) {
            Wait-SorchaActorReady -Mode AwaitingInbox `
                -InstanceId $instanceId -ActionId ([int]$actionId) -RegisterId $RegisterId `
                -Headers @{ Authorization = "Bearer $senderToken" } -GatewayUrl $env.GatewayUrl
        }

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
                    -Reject -RejectionReason $RejectionReason `
                    -WaitForSeal
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
                        -Token $senderToken -PayloadData $payloadData `
                        -WaitForSeal

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

        # Feature 145: gate each subsequent action on the projector surfacing it as current (the
        # async-submit cadence) — otherwise we race the projector and get a 400. See run loop above.
        if ($actionId -ne 1) {
            Wait-SorchaActorReady -Mode AwaitingInbox `
                -InstanceId $instanceId -ActionId ([int]$actionId) -RegisterId $RegisterId `
                -Headers @{ Authorization = "Bearer $senderToken" } -GatewayUrl $env.GatewayUrl
        }

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
                -Token $senderToken -PayloadData $payloadData `
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
            $actionsOk++
        } catch {
            Write-WtFail "    Action $actionIdStr ($sender) failed: $($_.Exception.Message)"
            return @{ Passed = $false; ActionsOk = $actionsOk; Total = $totalActions }
        }
    }

    # Action 6: Dispute (send as normal action with decision=dispute in payload)
    $sender = $procurementSenderMap[6]
    $senderWallet = $wallets[$sender]
    $senderToken = $participantTokens[$sender]

    # Feature 145: action 5 (last in the loop above) advances to action 6 only once the projector
    # folds its sealed docket. Gate on AwaitingInbox before the dispute, else we race the projector
    # and get a 400 ("Action 6 is not a current action").
    Wait-SorchaActorReady -Mode AwaitingInbox `
        -InstanceId $instanceId -ActionId 6 -RegisterId $RegisterId `
        -Headers @{ Authorization = "Bearer $senderToken" } -GatewayUrl $env.GatewayUrl

    try {
        $disputeData = $ActionData."6_dispute"
        $payloadData = @{}
        if ($disputeData) {
            foreach ($prop in $disputeData.PSObject.Properties) {
                $payloadData[$prop.Name] = $prop.Value
            }
        }

        $null = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "6" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken -PayloadData $payloadData `
            -WaitForSeal
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

    # Feature 145: the dispute (action 6) routes back to action 5. The instance only re-surfaces
    # action 5 as current once the InstanceProjector folds the sealed dispute docket — a beat after
    # the seal. Gate on AwaitingInbox before resubmitting, else we race the projector and get a 400
    # ("Action 5 is not a current action"). See walkthrough-builder skill, Cadence.
    Wait-SorchaActorReady -Mode AwaitingInbox `
        -InstanceId $instanceId -ActionId 5 -RegisterId $RegisterId `
        -Headers @{ Authorization = "Bearer $senderToken" } -GatewayUrl $env.GatewayUrl

    try {
        $response = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "5" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken -PayloadData $payloadData `
            -WaitForSeal

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

    # Feature 145: gate on the resubmitted action 5 surfacing action 6 as current again before
    # the final approval, same async-projection cadence as above.
    Wait-SorchaActorReady -Mode AwaitingInbox `
        -InstanceId $instanceId -ActionId 6 -RegisterId $RegisterId `
        -Headers @{ Authorization = "Bearer $senderToken" } -GatewayUrl $env.GatewayUrl

    try {
        $response = Invoke-SorchaAction `
            -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
            -ActionId "6" -BlueprintId $BlueprintId `
            -SenderWallet $senderWallet -RegisterId $RegisterId `
            -Token $senderToken -PayloadData $payloadData `
            -WaitForSeal

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

    # Snapshot the sales-mgr's invoice credentials BEFORE the phase that issues one. This
    # wallet accumulates one per run, so "first of this type" is the OLDEST — an invoice
    # credential carrying an earlier run's invoice number, presented against this run's
    # scenario data. Same defect as #1503 (and #1477 defect 2, #1483).
    $invoiceVct       = "https://sorcha.dev/vc/verified-invoice/v1"
    $salesMgrListUri  = Get-SorchaWalletCredentialUri -WalletUrl $env.WalletUrl -WalletAddress $wallets["sales-mgr"]
    $salesMgrHeaders  = @{ Authorization = "Bearer $($participantTokens['sales-mgr'])" }
    $invoiceBefore    = Get-SorchaCredentialIdSnapshot -ListUri $salesMgrListUri -Headers $salesMgrHeaders `
        -CredentialType $invoiceVct

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
            # Feature 106 Wave C: register-native credential delivery persists as
            # Status=PendingAcceptance. The holder (sales-mgr) must accept before it
            # becomes Active and usable for presentation. Do that here.
            $pending = Invoke-SorchaApi -Method GET `
                -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials?status=PendingAcceptance" `
                -Headers $credHeaders

            # Auto-accept any pending credentials we plan to present (invoice + DPP)
            # before fetching the active list, so they appear as Active downstream.
            $autoAcceptTypes = @("https://sorcha.dev/vc/verified-invoice/v1", "https://sorcha.dev/vc/forest-product-dpp/v1")
            foreach ($p in ($pending | Where-Object { $autoAcceptTypes -contains $_.type })) {
                Write-WtInfo "  Accepting pending $($p.type) $($p.id)..."
                $null = Invoke-SorchaApi -Method PATCH `
                    -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials/$($p.id)" `
                    -Body @{ status = "Active" } `
                    -Headers $credHeaders
            }

            $creds = Invoke-SorchaApi -Method GET `
                -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials" `
                -Headers $credHeaders

            # The invoice credential THIS run issued — absent from the pre-phase snapshot.
            $invoiceCred = Wait-SorchaNewCredential -ListUri $salesMgrListUri -Headers $salesMgrHeaders `
                -CredentialType $invoiceVct -ExcludeIds $invoiceBefore -TimeoutSeconds 60

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
                            type           = "https://sorcha.dev/vc/verified-invoice/v1"
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

            # Optional ForestProductDPPCredential — issued by the ForestryCertification
            # walkthrough to sales-mgr. When present, the financing template's calc
            # applies a +10% advance-rate uplift if sustainabilityScore >= 70.
            # The DPP credential comes from the ForestryCertification walkthrough, not from this
            # run, so there is no "new since we started" to require — the snapshot rule that
            # applies to the invoice above cannot apply here. Take the MOST RECENTLY ISSUED one
            # rather than whichever the listing happens to return first: after Forestry has run
            # more than once, first-of-type is the oldest. ($creds is the Active-only listing, so
            # a revoked DPP is already excluded.)
            $dppCred = $creds |
                Where-Object { $_.type -eq "https://sorcha.dev/vc/forest-product-dpp/v1" } |
                Sort-Object -Property issuedAt -Descending |
                Select-Object -First 1
            if ($dppCred) {
                Write-WtInfo "  Found DPP credential: $($dppCred.id)"

                $dppExported = Invoke-SorchaApi -Method GET `
                    -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials/$($dppCred.id)/export" `
                    -Headers $credHeaders

                $dppToken = if ($dppExported.sdJwt) { $dppExported.sdJwt } elseif ($dppExported.rawToken) { $dppExported.rawToken } else { $dppExported.token }

                # The platform verifies against rawPresentation (the signed SD-JWT).
                # The disclosedClaims hash is informational for the audit log; pin
                # the values to what ForestryCertification's golden-path issues so
                # logs read cleanly. If you change the Forestry scenario, update here.
                $credPresentations += @{
                    credentialId    = $dppCred.id
                    disclosedClaims = @{
                        type                  = "https://sorcha.dev/vc/forest-product-dpp/v1"
                        certificationScheme   = "FSC"
                        sustainabilityScore   = 87
                        embodiedCarbonKgCO2e  = 36.4
                        expiryDate            = "2027-04-15"
                    }
                    rawPresentation = $dppToken
                }
                Write-WtInfo "  Presenting ForestProductDPPCredential: $($dppCred.id) (sustainabilityScore=87 — uplift will apply)"
            } else {
                Write-WtInfo "  No ForestProductDPPCredential in sales-mgr wallet — skipping DPP presentation (sustainability uplift will not apply). Run ForestryCertification golden-path before TradeFinance to enable the cross-register uplift demo."
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
            -IsRejection $false `
            -RejectionReason "" `
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
