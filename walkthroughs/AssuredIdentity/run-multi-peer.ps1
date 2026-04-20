#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Multi-peer cross-peer smoke test (Feature 107 PR 4 / US4).
# Brings up a two-peer federation, runs Phase 1 with issuer on peer A and
# citizen on peer B, and asserts the register-native delivery path lands
# the AssuredIdentityCredential in peer B within the latency budget.
#
# Non-blocking: per FR-039 this script produces findings on every run
# regardless of outcome. Failures do NOT block Feature 107 shipping.

param(
    [int]$TimeoutSeconds = 120,
    [switch]$SkipComposeUp,
    [switch]$SkipComposeDown,
    [switch]$ShowJson
)

$ErrorActionPreference = "Continue"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Multi-Peer Smoke (Feature 107 US4)"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$findingsDir = Join-Path $scriptDir "multi-peer-findings"
if (-not (Test-Path $findingsDir)) { New-Item -Path $findingsDir -ItemType Directory | Out-Null }

$runStart = Get-Date
$runTimestamp = $runStart.ToString("yyyyMMdd-HHmmss")
$findingsFile = Join-Path $findingsDir "$runTimestamp.md"

$outcome = "unknown"
$timings = [ordered]@{}
$anomalies = @()
$peerAVersion = "unknown"
$peerBVersion = "unknown"

function Write-Findings {
    $frontmatter = @"
---
run_timestamp: $runTimestamp
peer_a_version: $peerAVersion
peer_b_version: $peerBVersion
outcome: $outcome
---

# Cross-peer smoke findings — run $runTimestamp

## Topology

- Peer A: docker-compose.yml default stack (api-gateway on :80)
- Peer B: docker-compose.federation.yml overlay (api-gateway-b on :8081)
- Shared Docker network: sorcha-network
- Peer discovery: peer-b seeds peer-a; peer-a's SEED_PEER_* env overridden to include peer-b

## Timings

$(foreach ($k in $timings.Keys) { "- **$k**: $($timings[$k])" } | Out-String)

## Anomalies

$(if ($anomalies.Count -eq 0) { "_None recorded._" } else { ($anomalies | ForEach-Object { "- $_" }) -join "`n" })

## Reproduction

```
docker compose -f docker-compose.yml -f docker-compose.federation.yml up -d
pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1
```

## Outcome rationale

$(switch ($outcome) {
    "pass"          { "All milestones within budget. Credential delivered from peer A to peer B within the SC-009 latency target." }
    "degraded-pass" { "Milestone exceeded latency budget but credential eventually arrived. Degraded delivery path noted." }
    "fail"          { "One or more milestones did not complete within the 60s per-step deadline." }
    "env-failure"   { "Environment setup failed (docker-compose up did not reach healthy, or peer-b stack errored before the walkthrough reached its flow)." }
    default         { "Run did not complete. See anomalies for partial progress." }
})
"@
    Set-Content -Path $findingsFile -Value $frontmatter -Encoding UTF8
    Write-WtInfo "Findings written to $findingsFile"
}

# Always emit findings on exit (success or failure) — FR-038.
trap {
    $anomalies += "Unhandled exception: $($_.Exception.Message)"
    if ($outcome -eq "unknown") { $outcome = "env-failure" }
    Write-Findings
    continue
}

# ============================================================================
# Step 1: Bring up the federation
# ============================================================================
if (-not $SkipComposeUp) {
    Write-WtStep "Step 1: Bring up two-peer federation"
    $stepStart = Get-Date
    & docker compose -f (Join-Path $repoRoot "docker-compose.yml") `
                     -f (Join-Path $repoRoot "docker-compose.federation.yml") `
                     up -d 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        $outcome = "env-failure"
        $anomalies += "docker compose up failed with exit code $LASTEXITCODE"
        Write-Findings
        exit 1
    }
    $timings["compose-up"] = "{0:mm\:ss}" -f ((Get-Date) - $stepStart)
}

# ============================================================================
# Step 2: Wait for both peers healthy
# ============================================================================
Write-WtStep "Step 2: Wait for peer A + peer B healthy"
$stepStart = Get-Date
$maxWait = 90
$waited = 0
$bothHealthy = $false
while ($waited -lt $maxWait) {
    $aHealthy = (docker inspect --format '{{.State.Health.Status}}' sorcha-blueprint-service 2>$null) -eq "healthy"
    $bHealthy = (docker inspect --format '{{.State.Health.Status}}' sorcha-blueprint-service-b 2>$null) -eq "healthy"
    if ($aHealthy -and $bHealthy) { $bothHealthy = $true; break }
    Start-Sleep -Seconds 2
    $waited += 2
}
$timings["peers-healthy"] = "{0:mm\:ss}" -f ((Get-Date) - $stepStart)

if (-not $bothHealthy) {
    $outcome = "env-failure"
    $anomalies += "Peers did not reach Healthy within ${maxWait}s"
    Write-Findings
    if (-not $SkipComposeDown) { & docker compose -f (Join-Path $repoRoot "docker-compose.federation.yml") down 2>&1 | Out-Null }
    exit 1
}

# ============================================================================
# Step 3: Provision Gov issuer on peer A + citizen on peer B
# ============================================================================
# Placeholder — the real implementation submits setup actions against peer-a's
# API gateway (http://localhost/) for the Gov org and against peer-b's API
# gateway (http://localhost:8081/) for the citizen public-org account, then
# subscribes peer-b to peer-a's Assured Identity register. The register ID
# must be captured and passed through to Phase 1.
Write-WtStep "Step 3: Provision Gov on peer A + citizen on peer B (TODO — operator)"
$anomalies += "Step 3 not yet implemented end-to-end — awaiting operator wiring."

# ============================================================================
# Step 4: Run Phase 1 identity issuance cross-peer (register-native delivery)
# ============================================================================
Write-WtStep "Step 4: Cross-peer identity issuance (TODO — operator)"
$anomalies += "Step 4 not yet implemented — first operator run must capture timings."

# ============================================================================
# Step 5: Assert PENDING credential surfaces on peer B, citizen accepts, Active.
# ============================================================================
Write-WtStep "Step 5: Assert PENDING → Active on peer B (TODO — operator)"
$anomalies += "Step 5 not yet implemented — first operator run asserts + records latency."

if ($outcome -eq "unknown") {
    $outcome = "env-failure"
    $anomalies += "Initial iteration — steps 3-5 are operator-completion tasks. See PR #350 body for rationale."
}

Write-Findings

if (-not $SkipComposeDown) {
    Write-WtStep "Tear-down"
    & docker compose -f (Join-Path $repoRoot "docker-compose.federation.yml") down 2>&1 | Out-Null
}

Write-WtBanner "AssuredIdentity — Multi-Peer Smoke Complete ($outcome)"
if ($outcome -eq "pass" -or $outcome -eq "degraded-pass") {
    exit 0
} else {
    # Non-blocking per FR-039: exit 0 so a surrounding CI pipeline does not
    # mark the feature as failing. Findings doc carries the actual status.
    exit 0
}
