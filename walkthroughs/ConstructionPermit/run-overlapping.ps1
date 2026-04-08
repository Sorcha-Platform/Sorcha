#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# ConstructionPermit — Overlapping Run (10 instances)
# Starts 10 instances with staggered timing, advancing actions across
# multiple instances in an interleaved pattern with random delays.

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "ConstructionPermit — Overlapping Run (10 Instances)"

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

# Action-to-sender mapping
$actionSenderMap = @{
    1 = "contractor"
    2 = "structural-engineer"
    3 = "planning-officer"
    4 = "environmental-assessor"
    5 = "building-control"
    6 = "planning-officer"
}

# Login each user
Write-WtStep "Authenticating all participants"
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
Write-WtSuccess "All 5 participants authenticated"

# Define 10 instances with varied scenarios and custom project data
$instances = @(
    @{ id=1;  scenario="A"; label="Harbour View Apartments";      site="22 Marina Drive, Riverside, RS1 2CD";        type="residential"; value=450000;  area=650;  storeys=2; existing=$false }
    @{ id=2;  scenario="B"; label="Riverside Tech Campus";        site="100 Innovation Park, Riverside, RS2 8JK";    type="commercial";  value=8000000; area=5000; storeys=10; existing=$true }
    @{ id=3;  scenario="A"; label="Oak Meadow Cottages";          site="7 Meadow Lane, Riverside, RS4 3FG";          type="residential"; value=350000;  area=500;  storeys=2; existing=$false }
    @{ id=4;  scenario="C"; label="Greenbelt Retail Park";        site="15 Country Road, Riverside, RS3 9MN";        type="commercial";  value=3000000; area=2000; storeys=5; existing=$false }
    @{ id=5;  scenario="B"; label="Waterfront Office Complex";    site="88 Quayside Blvd, Riverside, RS2 1AB";       type="commercial";  value=6500000; area=4200; storeys=9; existing=$true }
    @{ id=6;  scenario="A"; label="Cherry Blossom Terrace";       site="3 Garden Close, Riverside, RS1 7PQ";         type="residential"; value=600000;  area=900;  storeys=3; existing=$false }
    @{ id=7;  scenario="C"; label="Parkside Warehouse Conversion";site="40 Industrial Estate, Riverside, RS3 4ST";   type="commercial";  value=1800000; area=1500; storeys=4; existing=$true }
    @{ id=8;  scenario="B"; label="Central Library Extension";    site="1 Civic Square, Riverside, RS1 1AA";         type="commercial";  value=4500000; area=3000; storeys=7; existing=$true }
    @{ id=9;  scenario="A"; label="Sunflower Court Bungalows";    site="12 Hilltop Avenue, Riverside, RS4 6UV";      type="residential"; value=280000;  area=400;  storeys=1; existing=$false }
    @{ id=10; scenario="C"; label="Eastgate Shopping Centre";     site="50 Bypass Road, Riverside, RS3 2WX";         type="commercial";  value=2500000; area=1800; storeys=3; existing=$false }
)

# Scenario definitions: expected action paths
$scenarioPaths = @{
    "A" = @(1, 2, 3, 5, 6)
    "B" = @(1, 2, 3, 4, 5, 6)
    "C" = @(1, 2, 3)
}

# Per-scenario payload templates for actions beyond action 1
$scenarioPayloads = @{
    "A" = @{
        "2" = @{ loadRating=4.5; foundationType="strip"; structuralGrade="B"; structuralNotes="Standard residential construction. Strip foundations adequate." }
        "3" = @{ zoningCompliant=$true; planningNotes="Within residential zone. Height within limits. Approved." }
        "5" = @{ structuralApproved=$true; fireCompliant=$true; accessCompliant=$true; inspectionNotes="All building regulations met." }
        "6" = @{ approved=$true; permitNumber="RBC-2026-{0:D5}"; validUntil="2029-03-30"; conditions="Standard conditions apply." }
    }
    "B" = @{
        "2" = @{ loadRating=8.2; foundationType="piled"; structuralGrade="A"; structuralNotes="Piled foundations for high-rise commercial. Steel frame with RC core." }
        "3" = @{ zoningCompliant=$true; planningNotes="Commercial zone approved. Risk score exceeds threshold — routing to environmental." }
        "4" = @{ environmentalImpact="medium"; mitigationRequired=$true; environmentalConditions="Noise mitigation and dust suppression required. SuDS attenuation." }
        "5" = @{ structuralApproved=$true; fireCompliant=$true; accessCompliant=$true; inspectionNotes="Exceeds minimums. Dual staircase, sprinklers, smoke ventilation." }
        "6" = @{ approved=$true; permitNumber="RBC-2026-{0:D5}"; validUntil="2029-03-30"; conditions="Subject to environmental conditions." }
    }
    "C" = @{
        "2" = @{ loadRating=5.0; foundationType="raft"; structuralGrade="B"; structuralNotes="Raft foundation suitable for commercial loading." }
        "3" = @{ zoningCompliant=$false; planningNotes="REJECTED: Green Belt zone. Commercial development not permitted at this scale." }
    }
}

$rejectionReason = "Site falls within designated Green Belt zone. Commercial development not permitted under current planning policy."

# Track instance state
$tracker = @{}
foreach ($inst in $instances) {
    $tracker[$inst.id] = @{
        Config       = $inst
        InstanceId   = $null
        Path         = $scenarioPaths[$inst.scenario]
        NextStep     = 0       # index into Path
        Status       = "pending"  # pending, active, complete, failed
        StartTime    = $null
        EndTime      = $null
        ActionsOk    = 0
    }
}

$globalStart = Get-Date
$rng = [System.Random]::new()
$permitBase = $rng.Next(1000, 90000)  # unique permit number base per run

# Helper: create an instance
function Start-Instance([int]$id) {
    $t = $tracker[$id]
    $c = $t.Config
    $contractorToken = $roleTokenCache["contractor"]
    $contractorOrgId = $roles["contractor"].organizationId

    $instanceBody = @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $contractorOrgId
        metadata    = @{ source = "overlapping-walkthrough"; instance = $id; scenario = $c.scenario; project = $c.label }
    }

    $ir = Invoke-SorchaApi -Method POST -Uri "$($state.blueprintUrl)/instances/" -Body $instanceBody `
        -Headers @{ Authorization = "Bearer $contractorToken" }
    $t.InstanceId = $ir.id
    $t.Status = "active"
    $t.StartTime = Get-Date

    Write-Host "  [+$id] " -NoNewline -ForegroundColor Cyan
    Write-Host "CREATED " -NoNewline -ForegroundColor Green
    Write-Host "$($c.label) " -NoNewline -ForegroundColor White
    Write-Host "(Scenario $($c.scenario), $($t.Path.Count) actions)" -ForegroundColor DarkGray
}

# Helper: advance one action on an instance
function Step-Instance([int]$id) {
    $t = $tracker[$id]
    if ($t.Status -ne "active") { return $false }

    $stepIdx = $t.NextStep
    $path = $t.Path
    if ($stepIdx -ge $path.Count) { return $false }

    $actionId = $path[$stepIdx]
    $actionIdStr = "$actionId"
    $sender = $actionSenderMap[[int]$actionId]
    $senderWallet = $wallets[$sender]
    $senderToken = $roleTokenCache[$sender]
    $contractorToken = $roleTokenCache["contractor"]
    $c = $t.Config
    $scenario = $c.scenario

    # Wait for action to be current
    if ($stepIdx -gt 0) {
        $maxWait = 45
        $waited = 0
        $ready = $false
        while ($waited -lt $maxWait) {
            $inst = Invoke-SorchaApi -Method GET `
                -Uri "$($state.blueprintUrl)/instances/$($t.InstanceId)" `
                -Headers @{ Authorization = "Bearer $contractorToken" }
            if ($inst.currentActionIds -contains [int]$actionId) {
                $ready = $true
                break
            }
            Start-Sleep -Seconds 1
            $waited++
        }
        if (-not $ready) {
            Write-Host "  [!$id] " -NoNewline -ForegroundColor Red
            Write-Host "TIMEOUT waiting for action $actionIdStr (${maxWait}s)" -ForegroundColor Red
            $t.Status = "failed"
            $t.EndTime = Get-Date
            return $false
        }
    }

    # Build payload
    $isLastAction = ($stepIdx -eq ($path.Count - 1))
    $isRejection = ($scenario -eq "C") -and $isLastAction

    $executeHeaders = @{
        Authorization        = "Bearer $senderToken"
        "X-Delegation-Token" = $senderToken
    }

    try {
        if ($isRejection) {
            $rejectBody = @{
                reason          = $rejectionReason
                senderWallet    = $senderWallet
                registerAddress = $state.registerId
            }
            $null = Invoke-SorchaApi -Method POST `
                -Uri "$($state.blueprintUrl)/instances/$($t.InstanceId)/actions/$actionIdStr/reject" `
                -Body $rejectBody -Headers $executeHeaders

            Write-Host "  [#$id] " -NoNewline -ForegroundColor Yellow
            Write-Host "Action $actionIdStr REJECTED " -NoNewline -ForegroundColor Red
            Write-Host "($sender)" -ForegroundColor DarkGray
        } else {
            # Get payload for this action
            if ($actionIdStr -eq "1") {
                $payloadData = @{
                    projectName       = $c.label
                    siteAddress       = $c.site
                    buildingType      = $c.type
                    estimatedValue    = $c.value
                    grossFloorArea    = $c.area
                    storeys           = $c.storeys
                    existingStructure = $c.existing
                }
            } else {
                $payloadData = @{}
                $tpl = $scenarioPayloads[$scenario][$actionIdStr]
                if ($tpl) {
                    foreach ($k in $tpl.Keys) {
                        $v = $tpl[$k]
                        # Interpolate permit number with instance id
                        if ($v -is [string] -and $v.Contains("{0")) { $v = $v -f ($permitBase + $id) }
                        $payloadData[$k] = $v
                    }
                }
            }

            $actionBody = @{
                blueprintId     = $state.blueprintId
                actionId        = $actionIdStr
                instanceId      = $t.InstanceId
                senderWallet    = $senderWallet
                registerAddress = $state.registerId
                payloadData     = $payloadData
            }

            $null = Invoke-SorchaApi -Method POST `
                -Uri "$($state.blueprintUrl)/instances/$($t.InstanceId)/actions/$actionIdStr/execute" `
                -Body $actionBody -Headers $executeHeaders

            Write-Host "  [#$id] " -NoNewline -ForegroundColor Cyan
            Write-Host "Action $actionIdStr " -NoNewline -ForegroundColor Green
            Write-Host "($sender)" -ForegroundColor DarkGray
        }

        $t.ActionsOk++
        $t.NextStep++

        if ($t.NextStep -ge $path.Count) {
            $t.Status = "complete"
            $t.EndTime = Get-Date
            $outcome = if ($scenario -eq "C") { "REJECTED" } else { "APPROVED" }
            $dur = [math]::Round(($t.EndTime - $t.StartTime).TotalSeconds, 1)
            Write-Host "  [=$id] " -NoNewline -ForegroundColor Magenta
            Write-Host "$($c.label): $outcome " -NoNewline -ForegroundColor $(if ($outcome -eq "APPROVED") { "Green" } else { "Yellow" })
            Write-Host "($($t.ActionsOk)/$($path.Count) in ${dur}s)" -ForegroundColor DarkGray
        }

        return $true
    } catch {
        Write-Host "  [!$id] " -NoNewline -ForegroundColor Red
        Write-Host "Action $actionIdStr FAILED: $($_.Exception.Message)" -ForegroundColor Red
        $t.Status = "failed"
        $t.EndTime = Get-Date
        return $false
    }
}

# ============================================================================
# Main orchestration loop — staggered starts with interleaved execution
# ============================================================================

Write-Host ""
Write-WtStep "Starting overlapping execution (10 instances, staggered)"
Write-Host ""

# Schedule: which instances to start at each "wave"
# Wave 1: start 1,2  |  Wave 2: advance 1,2 + start 3  |  etc.
$startOrder = @(
    @(1, 2),        # Wave 1: start two
    @(3),           # Wave 2: start another
    @(4, 5),        # Wave 3: start two more
    @(6),           # Wave 4
    @(7, 8),        # Wave 5
    @(9),           # Wave 6
    @(10)           # Wave 7
)

$nextWave = 0

while ($true) {
    # Start new instances if we have a wave pending
    if ($nextWave -lt $startOrder.Count) {
        foreach ($id in $startOrder[$nextWave]) {
            Start-Instance $id
        }
        $nextWave++
    }

    # Find all active instances that have steps remaining
    $activeIds = @($tracker.Keys | Where-Object { $tracker[$_].Status -eq "active" } | Sort-Object)

    if ($activeIds.Count -eq 0) {
        # Check if we still have waves to start
        if ($nextWave -lt $startOrder.Count) {
            continue
        }
        break
    }

    # Advance 1-3 active instances per round (random selection)
    $advanceCount = [math]::Min($activeIds.Count, $rng.Next(1, 4))
    $toAdvance = $activeIds | Get-Random -Count $advanceCount

    foreach ($id in $toAdvance) {
        Step-Instance $id | Out-Null
    }

    # Random delay 2-25 seconds between rounds
    $delay = $rng.Next(2, 26)
    $active = @($tracker.Keys | Where-Object { $tracker[$_].Status -eq "active" }).Count
    $complete = @($tracker.Keys | Where-Object { $tracker[$_].Status -eq "complete" }).Count
    Write-Host "  --- " -NoNewline -ForegroundColor DarkGray
    Write-Host "delay ${delay}s " -NoNewline -ForegroundColor DarkYellow
    Write-Host "(active: $active, complete: $complete/10)" -ForegroundColor DarkGray
    Start-Sleep -Seconds $delay
}

# ============================================================================
# Summary
# ============================================================================

$globalDuration = (Get-Date) - $globalStart

Write-Host ""
Write-WtBanner "ConstructionPermit — Overlapping Results"

$passed = 0
$failed = 0

foreach ($id in 1..10) {
    $t = $tracker[$id]
    $c = $t.Config
    $path = $t.Path
    $dur = if ($t.EndTime -and $t.StartTime) { [math]::Round(($t.EndTime - $t.StartTime).TotalSeconds, 1) } else { "?" }
    $outcome = switch ($t.Status) {
        "complete" { if ($c.scenario -eq "C") { "REJECTED" } else { "APPROVED" } }
        "failed"   { "FAILED" }
        default    { "INCOMPLETE" }
    }
    $ok = ($t.Status -eq "complete")
    if ($ok) { $passed++ } else { $failed++ }
    $icon = if ($ok) { "[OK]" } else { "[X]" }
    $color = if ($ok) { "Green" } else { "Red" }

    Write-Host "  $icon #$($id.ToString().PadLeft(2)) " -NoNewline -ForegroundColor $color
    Write-Host "Scenario $($c.scenario) " -NoNewline -ForegroundColor White
    Write-Host "$($c.label.PadRight(32)) " -NoNewline -ForegroundColor Cyan
    Write-Host "$outcome $($t.ActionsOk)/$($path.Count) " -NoNewline -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
    Write-Host "(${dur}s)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  Total Duration: $([math]::Round($globalDuration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host "  Passed: $passed / 10" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Yellow" })
Write-Host ""

if ($failed -eq 0) {
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  RESULT: FAIL ($failed failed)" -ForegroundColor Red
    exit 1
}
