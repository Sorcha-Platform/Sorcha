#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# TradeFinance — Soak Test (Persona Mode)
# Runs continuous procurement-to-pay + invoice finance flows for a specified
# duration with randomised persona-generated data. Each iteration creates a
# fresh workflow instance with plausible Scottish construction trade data.

param(
    [int]$DurationHours = 5,
    [int]$MaxRuns = 0,                # 0 = unlimited (use DurationHours); >0 = cap iterations
    [int]$MinIntervalSeconds = 180,   # 3 minutes
    [int]$MaxIntervalSeconds = 300,   # 5 minutes
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "TradeFinance — Soak Test (Persona Mode)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) { Write-WtFail "No state.json. Run setup.ps1 first."; exit 1 }
$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json

# ── Resolve environment ──────────────────────────────────────────────────────
$env = Initialize-SorchaEnvironment -Profile $state.profile -SkipHealthCheck
$blueprintUrl = $env.BlueprintUrl

$procurementBlueprintId = $state.blueprints.'procurement-to-pay'.id
$financeBlueprintId = $state.blueprints.'invoice-finance'.id
$tradeRegisterId = $state.registers.'sme-trade-register'.id
$financeRegisterId = $state.registers.'trade-finance-register'.id

$wallets = @{}
foreach ($prop in $state.wallets.PSObject.Properties) { $wallets[$prop.Name] = $prop.Value }

# ── Authenticate org admins ──────────────────────────────────────────────────
$orgAdminTokens = @{}
$participantTokens = @{}
foreach ($orgProp in $state.organizations.PSObject.Properties) {
    $orgKey = $orgProp.Name
    $orgId = $orgProp.Value
    $adminCtx = Connect-SorchaUser -TenantUrl $env.TenantUrl `
        -Email "admin@$orgKey.sorcha.dev" -Password "Wt-$orgKey-admin-2026!" -OrganizationId $orgId
    $orgAdminTokens[$orgKey] = $adminCtx.Token
}
foreach ($role in $state.roles.PSObject.Properties) {
    $participantTokens[$role.Name] = $orgAdminTokens[$role.Value.orgKey]
}

$cairngormOrgId = $state.organizations.cairngorm
$scottradeOrgId = $state.organizations.scottrade

# ── Sender maps ──────────────────────────────────────────────────────────────
$procurementSenderMap = @{
    1 = "procurement-mgr"; 2 = "sales-mgr"; 3 = "sales-mgr"
    4 = "site-mgr"; 5 = "sales-mgr"; 6 = "procurement-mgr"
}
$financeSenderMap = @{
    1 = "finance-director"; 2 = "assessment-svc"; 3 = "credit-analyst"; 4 = "credit-analyst"
}

# ── Persona data generators ──────────────────────────────────────────────────

$projects = @(
    @{ name = "Aviemore Heights Phase 2"; address = "Plot 14-18, Craig na Gower Road, Aviemore PH22 1RN" }
    @{ name = "Grantown Affordable Housing"; address = "Spey Valley Business Park, Grantown-on-Spey PH26 3HG" }
    @{ name = "Inverness Waterfront Apartments"; address = "Longman Drive, Inverness IV1 1SU" }
    @{ name = "Fort William Community Centre"; address = "High Street, Fort William PH33 6DH" }
    @{ name = "Nairn Coastal Cottages"; address = "Marine Road, Nairn IV12 4EA" }
    @{ name = "Dingwall Primary Extension"; address = "Tulloch Street, Dingwall IV15 9JZ" }
    @{ name = "Kingussie Sports Pavilion"; address = "Ardvonie Park, Kingussie PH21 1EX" }
    @{ name = "Loch Ness View Lodges"; address = "Drumnadrochit Road, Drumnadrochit IV63 6TX" }
    @{ name = "Cairngorm Mountain Base Lodge"; address = "Ski Road, Aviemore PH22 1RB" }
    @{ name = "Boat of Garten Village Hall"; address = "Deshar Road, Boat of Garten PH24 3BN" }
    @{ name = "Elgin Cathedral Close Flats"; address = "North College Street, Elgin IV30 1SD" }
    @{ name = "Forres Industrial Units"; address = "Greshop Industrial Estate, Forres IV36 2GU" }
)

$timberProducts = @(
    @{ desc = "Treated Structural Timber 47x200mm"; unit = "linear metre"; minPrice = 7.50; maxPrice = 9.50 }
    @{ desc = "Treated Structural Timber 47x150mm"; unit = "linear metre"; minPrice = 6.20; maxPrice = 8.00 }
    @{ desc = "C24 Graded Timber 47x225mm"; unit = "linear metre"; minPrice = 9.00; maxPrice = 12.00 }
    @{ desc = "OSB Sheathing Board 18mm"; unit = "sheet"; minPrice = 28.00; maxPrice = 36.00 }
    @{ desc = "Plywood Sheathing 12mm"; unit = "sheet"; minPrice = 24.00; maxPrice = 32.00 }
    @{ desc = "Roofing Battens 25x50mm"; unit = "linear metre"; minPrice = 3.20; maxPrice = 4.80 }
    @{ desc = "Timber Connectors Assorted"; unit = "box"; minPrice = 35.00; maxPrice = 55.00 }
    @{ desc = "Joist Hangers Assorted"; unit = "box"; minPrice = 32.00; maxPrice = 48.00 }
    @{ desc = "DPC Membrane 4m wide"; unit = "linear metre"; minPrice = 10.00; maxPrice = 15.00 }
    @{ desc = "Sarking Board 25mm"; unit = "sheet"; minPrice = 18.00; maxPrice = 26.00 }
    @{ desc = "Fence Posts 100x100mm Treated"; unit = "each"; minPrice = 8.00; maxPrice = 14.00 }
    @{ desc = "Decking Boards 32x150mm"; unit = "linear metre"; minPrice = 5.50; maxPrice = 8.50 }
)

function Get-RandomDecimal { param([double]$Min, [double]$Max) return [Math]::Round($Min + (Get-Random -Minimum 0 -Maximum 1000) / 1000.0 * ($Max - $Min), 2) }

function New-PersonaScenario {
    param([int]$RunNumber)

    $project = $projects[(Get-Random -Minimum 0 -Maximum $projects.Count)]
    $poRef = "PO-CAIRN-2026-{0:D5}" -f (Get-Random -Minimum 200 -Maximum 99999)
    $invoiceRef = "HTS-INV-2026-{0:D5}" -f (Get-Random -Minimum 4000 -Maximum 99999)
    $ackRef = "HTS-ACK-2026-{0:D4}" -f (Get-Random -Minimum 1000 -Maximum 9999)
    $dnRef = "HTS-DN-2026-{0:D4}" -f (Get-Random -Minimum 1300 -Maximum 9999)
    $grnRef = "CAIRN-GRN-2026-{0:D5}" -f (Get-Random -Minimum 500 -Maximum 99999)
    $finRef = "STF-FIN-2026-{0:D5}" -f (Get-Random -Minimum 300 -Maximum 99999)

    # Generate 2-4 random line items
    $numItems = Get-Random -Minimum 2 -Maximum 5
    $selectedProducts = $timberProducts | Get-Random -Count $numItems
    $poLineItems = @()
    $invoiceLineItems = @()
    $deliveredItems = @()
    $subtotal = 0.0

    foreach ($prod in $selectedProducts) {
        $qty = Get-Random -Minimum 10 -Maximum 600
        $price = Get-RandomDecimal -Min $prod.minPrice -Max $prod.maxPrice
        $lineTotal = [Math]::Round($qty * $price, 2)
        $subtotal += $lineTotal

        $poLineItems += @{
            description = $prod.desc; quantity = $qty; unit = $prod.unit; unitPrice = $price
        }
        $invoiceLineItems += @{
            description = $prod.desc; quantity = $qty; unitPrice = $price; lineTotal = $lineTotal
        }
        $deliveredItems += @{
            description = $prod.desc; quantityDelivered = $qty
        }
    }

    $vatRate = 0.20
    $vatAmount = [Math]::Round($subtotal * $vatRate, 2)
    $invoiceTotal = [Math]::Round($subtotal + $vatAmount, 2)
    $marginPct = Get-RandomDecimal -Min 15.0 -Max 28.0
    $materialCost = [Math]::Round($subtotal * (1 - $marginPct / 100), 2)
    $logistics = [Math]::Round((Get-RandomDecimal -Min 250 -Max 800), 2)
    $margin = [Math]::Round($subtotal - $materialCost - $logistics, 2)

    $today = Get-Date
    $deliveryDate = $today.AddDays(-(Get-Random -Minimum 1 -Maximum 5))
    $invoiceDate = $today.AddDays(-(Get-Random -Minimum 0 -Maximum 2))
    $paymentDueDate = $invoiceDate.AddDays(30)

    $paymentTerms = @("Net 15", "Net 30", "Net 45") | Get-Random
    $advancePct = @(80, 85, 90, 95) | Get-Random
    $feeRate = Get-RandomDecimal -Min 1.5 -Max 4.0
    $advanceAmount = [Math]::Round($invoiceTotal * $advancePct / 100, 2)
    $feeAmount = [Math]::Round($advanceAmount * $feeRate / 100, 2)
    $netAdvance = [Math]::Round($advanceAmount - $feeAmount, 2)

    $creditScore = Get-Random -Minimum 55 -Maximum 98
    $creditLimit = (Get-Random -Minimum 5 -Maximum 50) * 10000
    $riskRating = if ($creditScore -ge 70) { "low" } elseif ($creditScore -ge 50) { "medium" } else { "high" }

    return @{
        procurement = @{
            "1" = @{
                poReference = $poRef
                projectName = $project.name
                siteAddress = $project.address
                lineItems = $poLineItems
                deliveryAddress = "Site Compound, $($project.address)"
                paymentTerms = $paymentTerms
                requiredDeliveryDate = $deliveryDate.AddDays(2).ToString("yyyy-MM-dd")
            }
            "2" = @{
                accepted = $true
                estimatedDeliveryDate = $deliveryDate.ToString("yyyy-MM-dd")
                orderConfirmationRef = $ackRef
                notes = "Stock confirmed. Delivery by Highland Haulage."
            }
            "3" = @{
                deliveryNoteRef = $dnRef
                actualDeliveryDate = $deliveryDate.ToString("yyyy-MM-dd")
                deliveredItems = $deliveredItems
                deliveryCondition = @("Good — no damage noted", "Good — all items as ordered", "Minor surface marks — within tolerance", "Good — quantities verified on site") | Get-Random
            }
            "4" = @{
                grnReference = $grnRef
                receivedDate = $deliveryDate.ToString("yyyy-MM-dd")
                conditionNotes = "All items received in good condition, quantities verified against delivery note."
                discrepancyFlag = $false
            }
            "5" = @{
                invoiceNumber = $invoiceRef
                invoiceDate = $invoiceDate.ToString("yyyy-MM-dd")
                lineItems = $invoiceLineItems
                subtotal = $subtotal
                vatRate = $vatRate
                vatAmount = $vatAmount
                invoiceTotal = $invoiceTotal
                paymentTerms = $paymentTerms
                paymentDueDate = $paymentDueDate.ToString("yyyy-MM-dd")
                supplierCostBreakdown = @{
                    materialCost = $materialCost
                    logistics = $logistics
                    margin = $margin
                    marginPercentage = $marginPct
                }
            }
            "6" = @{
                decision = "approve"
                approvalNotes = "Invoice matches PO and GRN. Approved for payment. Run #$RunNumber."
                approvedAmount = $invoiceTotal
            }
        }
        finance = @{
            "1" = @{
                invoiceReference = $invoiceRef
                invoiceAmount = $invoiceTotal
                buyerName = "Cairngorm Construction Ltd"
                requestedAdvancePercentage = $advancePct
                urgency = @("standard", "express") | Get-Random
            }
            "2" = @{
                buyerCreditScore = $creditScore
                creditLimit = $creditLimit
                riskRating = $riskRating
                assessmentDate = $today.ToString("yyyy-MM-dd")
                paymentHistoryScore = Get-Random -Minimum 70 -Maximum 99
                yearsTrading = 14
                assessmentNotes = "Established Highland contractor. Score $creditScore/100."
            }
            "3" = @{
                evaluationNotes = "Verified invoice credential confirmed. Buyer credit score $creditScore/100. Run #$RunNumber."
                advancePercentage = $advancePct
                feeRate = $feeRate
            }
            "4" = @{
                decision = "approve"
                advanceAmount = $advanceAmount
                feeAmount = $feeAmount
                netAdvance = $netAdvance
                repaymentTerms = "$paymentTerms from original invoice due date"
                repaymentDate = $paymentDueDate.ToString("yyyy-MM-dd")
                financingReference = $finRef
            }
        }
    }
}

# ── Token refresh helper ──────────────────────────────────────────────────────
$lastTokenRefresh = Get-Date
$tokenRefreshIntervalMinutes = 45

function Refresh-Tokens {
    Write-WtInfo "  Refreshing auth tokens..."
    foreach ($orgProp in $state.organizations.PSObject.Properties) {
        $orgKey = $orgProp.Name
        $orgId = $orgProp.Value
        $ctx = Connect-SorchaUser -TenantUrl $env.TenantUrl `
            -Email "admin@$orgKey.sorcha.dev" -Password "Wt-$orgKey-admin-2026!" -OrganizationId $orgId
        $script:orgAdminTokens[$orgKey] = $ctx.Token
    }
    foreach ($role in $state.roles.PSObject.Properties) {
        $script:participantTokens[$role.Name] = $script:orgAdminTokens[$role.Value.orgKey]
    }
    $script:lastTokenRefresh = Get-Date
    Write-WtSuccess "  Tokens refreshed"
}

# ── Main soak loop ───────────────────────────────────────────────────────────

$soakStart = Get-Date
$soakEnd = $soakStart.AddHours($DurationHours)
$runNumber = 0
$passed = 0
$failed = 0

if ($MaxRuns -gt 0) {
    Write-WtInfo "Run cap: $MaxRuns iterations (duration cap also active: $DurationHours h)"
} else {
    Write-WtInfo "Duration: $DurationHours hours (until $($soakEnd.ToString('HH:mm')))"
}
Write-WtInfo "Interval: ${MinIntervalSeconds}s – ${MaxIntervalSeconds}s between runs"
Write-WtInfo "Blueprints: $procurementBlueprintId / $financeBlueprintId"
Write-Host ""

while ((Get-Date) -lt $soakEnd) {
    if ($MaxRuns -gt 0 -and $runNumber -ge $MaxRuns) { break }
    $runNumber++
    $runStart = Get-Date
    $elapsed = $runStart - $soakStart
    $remaining = $soakEnd - $runStart

    # Refresh tokens before they expire (every 45 minutes)
    if (((Get-Date) - $lastTokenRefresh).TotalMinutes -ge $tokenRefreshIntervalMinutes) {
        Refresh-Tokens
    }

    Write-WtStep "Run #$runNumber  [elapsed: $([Math]::Round($elapsed.TotalMinutes, 0))m, remaining: $([Math]::Round($remaining.TotalMinutes, 0))m]"

    # Generate persona data
    $scenario = New-PersonaScenario -RunNumber $runNumber
    $allOk = $true

    # ── Phase 1: Procurement ─────────────────────────────────────────────
    Write-WtInfo "  Procurement: $($scenario.procurement.'1'.projectName) ($($scenario.procurement.'1'.poReference))"

    $instanceBody = @{
        blueprintId = $procurementBlueprintId; registerId = $tradeRegisterId
        tenantId = $cairngormOrgId
        metadata = @{ source = "soak-test"; run = $runNumber; mode = "persona" }
    }
    try {
        $ir = Invoke-SorchaApi -Method POST -Uri "$blueprintUrl/instances/" -Body $instanceBody `
            -Headers @{ Authorization = "Bearer $($orgAdminTokens['cairngorm'])" } -ShowJson:$ShowJson
        $instanceId = $ir.id
    } catch {
        Write-WtFail "  Instance creation failed: $($_.Exception.Message)"
        $failed++
        Start-Sleep -Seconds (Get-Random -Minimum $MinIntervalSeconds -Maximum $MaxIntervalSeconds)
        continue
    }

    $procOk = 0
    foreach ($actionId in @(1, 2, 3, 4, 5, 6)) {
        $sender = $procurementSenderMap[$actionId]
        $senderWallet = $wallets[$sender]
        $senderToken = $participantTokens[$sender]

        $actionData = $scenario.procurement."$actionId"
        $payloadData = @{}
        foreach ($k in $actionData.Keys) { $payloadData[$k] = $actionData[$k] }

        try {
            $response = Invoke-SorchaAction `
                -BlueprintUrl $blueprintUrl -InstanceId $instanceId `
                -ActionId "$actionId" -BlueprintId $procurementBlueprintId `
                -SenderWallet $senderWallet -RegisterId $tradeRegisterId `
                -Token $senderToken -PayloadData $payloadData
            $procOk++

            if ($response.credentialIssued) {
                Write-WtInfo "    VC: $($response.credentialIssued.credentialType)"
            }
        } catch {
            Write-WtFail "    Action $actionId ($sender) failed: $($_.Exception.Message)"
            $allOk = $false
            break
        }
    }

    if ($procOk -eq 6) {
        Write-WtSuccess "  Procurement: 6/6 APPROVED (£$($scenario.procurement.'5'.invoiceTotal))"
    } else {
        Write-WtFail "  Procurement: $procOk/6 INCOMPLETE"
        $failed++
        $waitSecs = Get-Random -Minimum $MinIntervalSeconds -Maximum $MaxIntervalSeconds
        Write-WtInfo "  Next run in ${waitSecs}s..."
        Start-Sleep -Seconds $waitSecs
        continue
    }

    # ── Phase 2: Invoice Finance ─────────────────────────────────────────
    # Fetch credential from sales-mgr wallet
    $salesMgrWallet = $wallets["sales-mgr"]
    $credHeaders = @{ Authorization = "Bearer $($participantTokens['sales-mgr'])" }
    $credPresentations = @()

    try {
        # Accept any PendingAcceptance VerifiedInvoiceCredentials first (Feature 106 Wave C)
        $pending = Invoke-SorchaApi -Method GET `
            -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials?status=PendingAcceptance" `
            -Headers $credHeaders
        foreach ($p in ($pending | Where-Object { $_.type -eq "https://sorcha.dev/vc/verified-invoice/v1" })) {
            $null = Invoke-SorchaApi -Method PATCH `
                -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials/$($p.id)" `
                -Body @{ status = "Active" } `
                -Headers $credHeaders
        }

        $creds = Invoke-SorchaApi -Method GET `
            -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials" `
            -Headers $credHeaders
        $invoiceCred = $creds | Where-Object { $_.type -eq "https://sorcha.dev/vc/verified-invoice/v1" } |
            Sort-Object -Property issuedAt -Descending | Select-Object -First 1

        if ($invoiceCred) {
            $exported = Invoke-SorchaApi -Method GET `
                -Uri "$($env.WalletUrl)/v1/wallets/$salesMgrWallet/credentials/$($invoiceCred.id)/export" `
                -Headers $credHeaders
            $rawToken = if ($exported.sdJwt) { $exported.sdJwt } elseif ($exported.rawToken) { $exported.rawToken } else { $exported.token }

            $credPresentations = @(@{
                credentialId    = $invoiceCred.id
                disclosedClaims = @{
                    type           = "https://sorcha.dev/vc/verified-invoice/v1"
                    invoiceNumber  = $scenario.procurement.'5'.invoiceNumber
                    invoiceAmount  = $scenario.procurement.'5'.invoiceTotal
                    poReference    = $scenario.procurement.'1'.poReference
                    paymentDueDate = $scenario.procurement.'5'.paymentDueDate
                }
                rawPresentation = $rawToken
            })
        }
    } catch {
        Write-WtWarn "  Credential fetch failed: $($_.Exception.Message)"
    }

    # Create finance instance
    $finInstanceBody = @{
        blueprintId = $financeBlueprintId; registerId = $financeRegisterId
        tenantId = $scottradeOrgId
        metadata = @{ source = "soak-test"; run = $runNumber; mode = "persona" }
    }
    try {
        $fir = Invoke-SorchaApi -Method POST -Uri "$blueprintUrl/instances/" -Body $finInstanceBody `
            -Headers @{ Authorization = "Bearer $($orgAdminTokens['scottrade'])" } -ShowJson:$ShowJson
        $finInstanceId = $fir.id
    } catch {
        Write-WtFail "  Finance instance creation failed: $($_.Exception.Message)"
        $failed++
        $waitSecs = Get-Random -Minimum $MinIntervalSeconds -Maximum $MaxIntervalSeconds
        Start-Sleep -Seconds $waitSecs
        continue
    }

    $finOk = 0
    foreach ($actionId in @(1, 2, 3, 4)) {
        $sender = $financeSenderMap[$actionId]
        $senderWallet = $wallets[$sender]
        $senderToken = $participantTokens[$sender]

        $actionData = $scenario.finance."$actionId"
        $payloadData = @{}
        foreach ($k in $actionData.Keys) { $payloadData[$k] = $actionData[$k] }

        try {
            if ($actionId -eq 1 -and $credPresentations.Count -gt 0) {
                # First action needs credential presentation
                $actionBody = @{
                    blueprintId            = $financeBlueprintId
                    actionId               = "$actionId"
                    instanceId             = $finInstanceId
                    senderWallet           = $senderWallet
                    registerAddress        = $financeRegisterId
                    payloadData            = $payloadData
                    credentialPresentations = $credPresentations
                }
                $executeHeaders = @{
                    Authorization        = "Bearer $senderToken"
                    "X-Delegation-Token" = $senderToken
                }
                $response = Invoke-SorchaApi -Method POST `
                    -Uri "$blueprintUrl/instances/$finInstanceId/actions/$actionId/execute" `
                    -Body $actionBody -Headers $executeHeaders -ShowJson:$ShowJson
            } else {
                $response = Invoke-SorchaAction `
                    -BlueprintUrl $blueprintUrl -InstanceId $finInstanceId `
                    -ActionId "$actionId" -BlueprintId $financeBlueprintId `
                    -SenderWallet $senderWallet -RegisterId $financeRegisterId `
                    -Token $senderToken -PayloadData $payloadData
            }
            $finOk++

            if ($response.credentialIssued) {
                Write-WtInfo "    VC: $($response.credentialIssued.credentialType)"
            }
        } catch {
            Write-WtFail "    Finance action $actionId ($sender) failed: $($_.Exception.Message)"
            $allOk = $false
            break
        }
    }

    if ($finOk -eq 4) {
        Write-WtSuccess "  Finance: 4/4 COMPLETED (advance £$($scenario.finance.'4'.advanceAmount), fee £$($scenario.finance.'4'.feeAmount))"
    } else {
        Write-WtFail "  Finance: $finOk/4 INCOMPLETE"
    }

    $runDuration = (Get-Date) - $runStart
    if ($allOk -and $procOk -eq 6 -and $finOk -eq 4) {
        $passed++
        Write-WtSuccess "  Run #${runNumber}: PASS ($([Math]::Round($runDuration.TotalSeconds, 1))s) — passed: $passed, failed: $failed"
    } else {
        $failed++
        Write-WtFail "  Run #${runNumber}: FAIL ($([Math]::Round($runDuration.TotalSeconds, 1))s) — passed: $passed, failed: $failed"
    }

    # Wait before next run (skip if time/run cap is almost up)
    $isLastRun = ($MaxRuns -gt 0 -and $runNumber -ge $MaxRuns)
    if (-not $isLastRun -and (Get-Date) -lt $soakEnd.AddMinutes(-5)) {
        $waitSecs = Get-Random -Minimum $MinIntervalSeconds -Maximum $MaxIntervalSeconds
        Write-WtInfo "  Next run in ${waitSecs}s ($([Math]::Round($waitSecs / 60, 1))m)..."
        Start-Sleep -Seconds $waitSecs
    }
}

# ── Summary ──────────────────────────────────────────────────────────────────
$totalDuration = (Get-Date) - $soakStart
Write-Host ""
Write-WtBanner "TradeFinance — Soak Test Results"
Write-Host "  Total runs:    $runNumber" -ForegroundColor White
Write-Host "  Passed:        $passed" -ForegroundColor Green
Write-Host "  Failed:        $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "  Duration:      $([Math]::Round($totalDuration.TotalHours, 1)) hours" -ForegroundColor White
Write-Host "  Transactions:  $($passed * 10) actions across 2 registers" -ForegroundColor White
Write-Host ""

if ($failed -eq 0) { Write-Host "  RESULT: PASS" -ForegroundColor Green }
else { Write-Host "  RESULT: $passed/$runNumber passed ($([Math]::Round($passed / [Math]::Max($runNumber, 1) * 100, 0))%)" -ForegroundColor Yellow }
