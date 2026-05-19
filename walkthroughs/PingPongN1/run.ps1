#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PingPongN1 — Run
#
# Register owner: Pong Corp on n1 (public). Subscriber: Ping Labs on local
# (NAT'd). Pulls flow local → n1, i.e. the NAT-friendly direction — this is
# the workaround while there's no push-on-seal or PeerRouter relay to carry
# dockets the other way.
#
# Flow per round:
#   1. Pong Corp executes action 0 (starting) on n1 → sealed in a docket.
#   2. Local's register-service pulls the new docket from n1.
#   3. Local's InstanceMirrorReconstructor materialises a read-only instance
#      row for Ping Labs (wallet matches a local participant).
#   4. Ping Labs executes action 1 against the mirror on local. The submission
#      becomes a transaction that local pushes back to n1 over its existing
#      outbound channel (NAT-friendly direction).
#   5. Pong Corp pulls the reply from local? — NO: still blocked by the same
#      gap in the other direction. Step 5 is documented as a finding, not a
#      PASS, so the runner prints PARTIAL rather than faking success.

param(
    [int]$Rounds = 2,
    [int]$ReplicationTimeoutSec = 120,
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PingPongN1 — Run ($Rounds rounds, register on n1)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "No state.json — run setup.ps1 first"
    exit 1
}

$state   = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
$secrets = Get-SorchaSecrets -WalkthroughName "ping-pong-n1"

$start = Get-Date
$stepsPassed = 0
$stepsFailed = 0
$findings = @()

function Step($name, $block) {
    Write-WtStep $name
    try {
        & $block
        $script:stepsPassed++
    } catch {
        Write-WtFail "$name — $($_.Exception.Message)"
        $script:stepsFailed++
        throw
    }
}

function Wait-Until {
    param([scriptblock]$Predicate, [int]$TimeoutSec = 60, [int]$PollSec = 3, [string]$Description)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    Write-WtInfo "Waiting up to ${TimeoutSec}s for $Description..."
    while ((Get-Date) -lt $deadline) {
        try { if (& $Predicate) { return $true } } catch { }
        Start-Sleep -Seconds $PollSec
    }
    return $false
}

function Get-Txs($gateway, $headers, $regId) {
    return Invoke-RestMethod -Uri "$gateway/api/registers/$regId/transactions" -Headers $headers
}

# ============================================================================
# Step 1: Login + switch into Pong Corp (n1) and Ping Labs (local)
# ============================================================================
Step "Step 1: Login as Pong Corp (n1) and Ping Labs (local)" {
    $script:pongSession = Connect-SorchaUser `
        -TenantUrl $state.n1.tenantUrl `
        -Email $secrets.adminEmail `
        -Password $secrets.adminPassword `
        -OrganizationId $state.n1.pongOrgId

    $script:pingSession = Connect-SorchaUser `
        -TenantUrl $state.local.tenantUrl `
        -Email $secrets.adminEmail `
        -Password $secrets.adminPassword `
        -OrganizationId $state.local.pingOrgId
}

# ============================================================================
# Step 2: Pong Corp creates public register on n1 (NOT local!)
# ============================================================================
Step "Step 2: Pong Corp creates public register on n1" {
    $name = "PingPong-N1-$(Get-Date -Format 'yyyyMMddHHmmss')"
    $script:register = New-SorchaRegister `
        -RegisterUrl $state.n1.registerUrl `
        -WalletUrl   $state.n1.walletUrl `
        -Name $name `
        -Description "n1-hosted register, Ping Labs on local pulls subscribes" `
        -TenantId $state.n1.pongOrgId `
        -OwnerUserId $pongSession.UserId `
        -OwnerWalletAddress $state.n1.pongWallet `
        -Headers $pongSession.Headers

    Invoke-RestMethod `
        -Uri "$($state.n1.gatewayUrl)/api/registers/$($register.RegisterId)/advertise" `
        -Method POST -Headers $pongSession.Headers `
        -ContentType 'application/json' -Body '{"isPublic":true}' -TimeoutSec 10 | Out-Null
    Write-WtSuccess "Advertised on n1's peer network"
}

# ============================================================================
# Step 3: Local (Ping Labs) discovers advertisement + subscribes
# ============================================================================
Step "Step 3: Local discovers advertisement + Ping Labs subscribes (Public)" {
    $ok = Wait-Until -TimeoutSec 120 -PollSec 5 -Description "register advertisement on local" {
        $available = Invoke-RestMethod `
            -Uri "$($state.local.gatewayUrl)/api/registers/available" `
            -Headers $pingSession.Headers
        return ($available | Where-Object { $_.registerId -eq $register.RegisterId }) -ne $null
    }
    if (-not $ok) { throw "n1's register never advertised to local" }

    $null = New-SorchaRegisterSubscription `
        -TenantUrl $state.local.tenantUrl `
        -OrganizationId $state.local.pingOrgId `
        -RegisterId $register.RegisterId `
        -Headers $pingSession.Headers `
        -RegisterName "PingPong (subscribed from n1)" `
        -SubscriptionType "Public"
}

# ============================================================================
# Step 4: Verify register record + genesis docket pulls through to local
# ============================================================================
Step "Step 4: Verify register record replicates to local" {
    $ok = Wait-Until -TimeoutSec $ReplicationTimeoutSec -PollSec 5 -Description "register record on local" {
        try {
            $r = Invoke-RestMethod `
                -Uri "$($state.local.gatewayUrl)/api/registers/$($register.RegisterId)" `
                -Headers $pingSession.Headers
            return $r -and $r.id -eq $register.RegisterId
        } catch { return $false }
    }
    if (-not $ok) { throw "Register record never appeared on local" }
}

# ============================================================================
# Step 5: Pong Corp publishes blueprint on n1 (both wallets mapped)
# ============================================================================
Step "Step 5: Pong Corp publishes blueprint on n1 (ping=local, pong=n1)" {
    $walletMap = @{
        "pong" = $state.n1.pongWallet
        "ping" = $state.local.pingWallet
    }
    $templatePath = Join-Path $scriptDir "blueprints/ping-pong.json"
    $script:blueprint = Publish-SorchaBlueprint `
        -BlueprintUrl $state.n1.blueprintUrl `
        -TemplatePath $templatePath `
        -WalletMap $walletMap `
        -Headers $pongSession.Headers `
        -IdPrefix "ping-pong-n1" `
        -RegisterId $register.RegisterId
}

# ============================================================================
# Step 6: Publish participants on each side
# ============================================================================
Step "Step 6: Publish Pong (n1) + Ping (local) participants" {
    $script:pongPublishTx = (Publish-SorchaParticipant `
        -TenantUrl $state.n1.tenantUrl `
        -OrganizationId $state.n1.pongOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Pong" `
        -OrganizationName $state.n1.pongOrgName `
        -WalletAddress $state.n1.pongWallet `
        -PublicKey $state.n1.pongPublicKey `
        -Headers $pongSession.Headers).transactionId

    $script:pingPublishTx = (Publish-SorchaParticipant `
        -TenantUrl $state.local.tenantUrl `
        -OrganizationId $state.local.pingOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Ping" `
        -OrganizationName $state.local.pingOrgName `
        -WalletAddress $state.local.pingWallet `
        -PublicKey $state.local.pingPublicKey `
        -Headers $pingSession.Headers).transactionId
}

# ============================================================================
# Step 7: Pong Corp creates blueprint instance on n1
# ============================================================================
Step "Step 7: Create blueprint instance on n1" {
    $body = @{ blueprintId = $blueprint.BlueprintId; registerId = $register.RegisterId }
    $script:instance = Invoke-SorchaApi -Method POST `
        -Uri "$($state.n1.blueprintUrl)/instances" `
        -Body $body -Headers $pongSession.Headers
    Write-WtSuccess "Instance on n1: $($instance.id)"
}

# ============================================================================
# Step 8: Round-trips
# ============================================================================
Write-WtStep "Step 8: $Rounds round-trip(s)"

$roundResults = @()

for ($round = 1; $round -le $Rounds; $round++) {
    Write-Host ""
    Write-WtInfo "Round $round / $Rounds"

    $roundState = @{ Round = $round; PongSent = $false; PongReplicated = $false; PingSent = $false; PingReplicated = $false }

    # --- Pong Corp (n1) sends the ping (action 0 = starting) ---
    $pongTx = $null
    try {
        $pongResp = Invoke-SorchaAction `
            -BlueprintUrl $state.n1.blueprintUrl `
            -InstanceId $instance.id `
            -ActionId "0" `
            -BlueprintId $blueprint.BlueprintId `
            -SenderWallet $state.n1.pongWallet `
            -RegisterId $register.RegisterId `
            -PayloadData @{ message = "pong #$round (n1 initiating)"; counter = $round; fromHost = "n1" } `
            -Token $pongSession.Token `
            -WaitForSeal
        $pongTx = $pongResp.transactionId
        $roundState.PongSent = $true
        Write-WtSuccess "Round ${round}: action 0 sent on n1 (tx=$pongTx)"
    } catch {
        Write-WtFail "Round ${round}: action 0 rejected on n1 — $($_.Exception.Message)"
        $findings += "Round ${round}: action 0 rejected on n1"
        $roundResults += $roundState
        # Skip the rest of this round but keep probing later rounds so a transient
        # failure doesn't silently mask regressions in rounds 2+.
        continue
    }

    if ($pongTx) {
        $roundState.PongReplicated = Wait-Until -TimeoutSec $ReplicationTimeoutSec -PollSec 5 `
            -Description "tx $pongTx on local (pulled from n1)" {
            $txs = Get-Txs $state.local.gatewayUrl $pingSession.Headers $register.RegisterId
            return ($txs | Where-Object { $_.id -eq $pongTx }) -ne $null
        }
        if ($roundState.PongReplicated) {
            Write-WtSuccess "Round ${round}: action 0 tx replicated to local"
        } else {
            Write-WtFail "Round ${round}: action 0 tx never appeared on local"
            $findings += "Round ${round}: action-0 tx $pongTx not pulled to local within ${ReplicationTimeoutSec}s"
        }
    }

    # Wait for the instance mirror to materialise on local so Ping Labs can
    # execute action 1 without a CreateInstance call it can't authorise. Uses the
    # same budget as the tx replication wait so a slow replication doesn't get
    # mis-reported as a mirror-reconstructor failure.
    $mirrorReady = Wait-Until -TimeoutSec $ReplicationTimeoutSec -PollSec 5 -Description "instance mirror on local" {
        try {
            $inst = Invoke-RestMethod `
                -Uri "$($state.local.blueprintUrl)/instances/$($instance.id)" `
                -Headers $pingSession.Headers
            return $inst.id -eq $instance.id
        } catch { return $false }
    }

    if (-not $mirrorReady) {
        Write-WtWarn "Round ${round}: instance mirror not ready on local — skipping action 1"
        $findings += "Round ${round}: InstanceMirrorReconstructor did not materialise instance $($instance.id) on local"
        $roundResults += $roundState
        continue
    }

    # --- Ping Labs (local) replies (action 1) ---
    $pingTx = $null
    try {
        $pingResp = Invoke-SorchaAction `
            -BlueprintUrl $state.local.blueprintUrl `
            -InstanceId $instance.id `
            -ActionId "1" `
            -BlueprintId $blueprint.BlueprintId `
            -SenderWallet $state.local.pingWallet `
            -RegisterId $register.RegisterId `
            -PayloadData @{ message = "ping #$round (local replying)"; counter = $round; fromHost = "local" } `
            -Token $pingSession.Token `
            -WaitForSeal
        $pingTx = $pingResp.transactionId
        $roundState.PingSent = $true
        Write-WtSuccess "Round ${round}: action 1 sent on local (tx=$pingTx)"
    } catch {
        Write-WtWarn "Round ${round}: action 1 rejected on local — $($_.Exception.Message.Split([Environment]::NewLine)[0])"
        $findings += "Round ${round}: action 1 rejected — $($_.Exception.Message.Split([Environment]::NewLine)[0])"
        $roundResults += $roundState
        continue
    }

    if ($pingTx) {
        $roundState.PingReplicated = Wait-Until -TimeoutSec $ReplicationTimeoutSec -PollSec 5 `
            -Description "tx $pingTx on n1 (reverse direction)" {
            $txs = Get-Txs $state.n1.gatewayUrl $pongSession.Headers $register.RegisterId
            return ($txs | Where-Object { $_.id -eq $pingTx }) -ne $null
        }
        if ($roundState.PingReplicated) {
            Write-WtSuccess "Round ${round}: action 1 tx replicated to n1"
        } else {
            Write-WtFail "Round ${round}: action 1 tx never appeared on n1"
            $findings += "Round ${round}: action-1 tx $pingTx not visible on n1 within ${ReplicationTimeoutSec}s (reverse-direction push gap)"
        }
    }

    $roundResults += $roundState
}

# ============================================================================
# Summary
# ============================================================================
$duration = (Get-Date) - $start
Write-Host ""
Write-WtBanner "PingPongN1 — Results"
Write-Host "  Register:  $($register.RegisterId)  (owner: n1)" -ForegroundColor White
Write-Host "  Blueprint: $($blueprint.BlueprintId)" -ForegroundColor White
Write-Host "  Instance:  $($instance.id)" -ForegroundColor White
Write-Host "  Duration:  $([math]::Round($duration.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""
Write-Host "  Infra (steps 1-7): $stepsPassed/$($stepsPassed + $stepsFailed) passed" -ForegroundColor $(if ($stepsFailed -eq 0) { "Green" } else { "Red" })

if ($roundResults.Count -gt 0) {
    Write-Host ""
    Write-Host "  Round  Pong-n1-sent  Pong→local  Ping-local-sent  Ping→n1" -ForegroundColor White
    foreach ($r in $roundResults) {
        $ps = if ($r.PongSent)       { "     ✓     " } else { "     ✗     " }
        $pr = if ($r.PongReplicated) { "   ✓    " } else { "   ✗    " }
        $qs = if ($r.PingSent)       { "       ✓       " } else { "       ✗       " }
        $qr = if ($r.PingReplicated) { "  ✓   " } else { "  ✗   " }
        Write-Host ("    {0}    {1}  {2} {3}  {4}" -f $r.Round, $ps, $pr, $qs, $qr)
    }
}

if ($findings.Count -gt 0) {
    Write-Host ""
    Write-Host "  Findings:" -ForegroundColor Yellow
    foreach ($f in $findings) { Write-Host "    • $f" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "  See walkthroughs/PingPongN1/README.md → 'Known limitation: reverse push'" -ForegroundColor Yellow
}

if ($stepsFailed -eq 0 -and $findings.Count -eq 0) {
    Write-Host "  RESULT: PASS" -ForegroundColor Green
    exit 0
} elseif ($stepsFailed -eq 0) {
    Write-Host "  RESULT: PARTIAL (infra PASS, see findings)" -ForegroundColor Yellow
    # Exit 2 distinguishes "known-gap PARTIAL" from "PASS" (0) and "infra-FAIL" (1)
    # so a CI runner can treat this as a neutral/warning signal rather than a pass.
    exit 2
} else {
    Write-Host "  RESULT: FAIL" -ForegroundColor Red
    exit 1
}
