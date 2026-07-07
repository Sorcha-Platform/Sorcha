# Quickstart: Verifying Feature 119 — Presentation Seal-Aware Ordering

**Feature**: 119-presentation-seal-ordering
**Date**: 2026-05-08
**Audience**: Developer or operator validating the chain-race fix end-to-end.

---

## Prerequisites

- .NET 10 SDK installed
- Docker Desktop running
- PowerShell 7+ (`pwsh`)
- Repository checked out at `master` with branch `119-presentation-seal-ordering` merged (or rebased on top of master)

---

## Step 1 — Reset state and bring up the stack

```powershell
# Wipe walkthrough state and citizen wallet
Remove-Item -Recurse -Force walkthroughs/AssuredIdentity/wallet -ErrorAction SilentlyContinue
Get-ChildItem walkthroughs -Recurse -Filter state.json | Remove-Item -Force

# Clean Docker state (optional — only if state has gone sideways)
docker compose down -v

# Bring services up
docker compose up -d

# Wait for health
$timeout = (Get-Date).AddMinutes(2)
while ((Get-Date) -lt $timeout) {
    $unhealthy = docker compose ps --format json | ConvertFrom-Json |
        Where-Object { $_.Health -ne 'healthy' -and $_.State -eq 'running' }
    if (-not $unhealthy) { break }
    Start-Sleep 3
}
docker compose ps
```

All Sorcha services should report `healthy`.

---

## Step 2 — Run the AssuredIdentity walkthrough (success criterion SC-119-001)

```powershell
1..10 | ForEach-Object {
    Write-Host "=== Run $_ of 10 ==="
    .\walkthroughs\AssuredIdentity\run.ps1 -Profile gateway
    if ($LASTEXITCODE -ne 0) {
        throw "Run $_ failed — feature is not delivered."
    }
}
Write-Host "All 10 runs passed. SC-119-001 met."
```

Expected: 10 of 10 runs complete with Phase 2 step 7 succeeding. No retries, no `VAL_CHAIN_001` or `VAL_BP_003` rejections in logs.

---

## Step 3 — Inspect the seal-coordinator metrics (SC-119-006)

```powershell
# Hit the Blueprint Service metrics endpoint via the gateway
$metrics = curl -s http://localhost/metrics

# Look for seal-wait metrics
$metrics | Select-String -Pattern 'sorcha_presentation_seal_'
```

Expected output (after a clean walkthrough run):

- `sorcha_presentation_seal_wait_seconds_count{site="outcome"}` ≥ 1
- `sorcha_presentation_seal_wait_seconds_count{site="advance"}` ≥ 1
- `sorcha_presentation_seal_queue_depth{site="..."}` = 0 (queues drained)
- `sorcha_presentation_seal_timeout_total{site="..."}` = 0 (no failures)
- `sorcha_presentation_seal_recovered_via_sweeper_total{site="..."}` = 0 (events arrived normally)

---

## Step 4 — Force a never-seals failure (SC-119-005)

```powershell
# Inject a fake validator that admits to mempool but consensus rejects.
# (Test fixture only — not a production path.)
$env:SORCHA_TEST_FAKE_CONSENSUS_REJECT = "presentation-initiated"
docker compose restart blueprint-service validator-service

# Trigger a presentation
.\walkthroughs\AssuredIdentity\run.ps1 -Profile gateway -StopAfterPhase 2

# Wait the validity window (default 600s)
Start-Sleep 605

# Verify failure recorded
docker logs sorcha-blueprint-service --since 11m |
    Select-String -Pattern 'failed-predecessor-not-sealed'

curl -s http://localhost/metrics |
    Select-String -Pattern 'sorcha_presentation_seal_timeout_total'
```

Expected:
- A structured-log entry at `LogError` with `failed-predecessor-not-sealed` and the presentation request id.
- `sorcha_presentation_seal_timeout_total{site="outcome"}` = 1.
- The presentation does not silently disappear and is not re-attempted infinitely.

Reset:
```powershell
Remove-Item Env:SORCHA_TEST_FAKE_CONSENSUS_REJECT
docker compose restart blueprint-service validator-service
```

---

## Step 5 — Restart-safety check (SC-119-007)

```powershell
# Run AssuredIdentity Phase 1, pause, restart Blueprint Service mid-flight
.\walkthroughs\AssuredIdentity\run.ps1 -Profile gateway -StopAfterPhase 1

# Wait for a presentation to be queued (run Phase 2 step 5 then kill blueprint mid-callback)
# Restart-while-pending durability (SC-119-007) is now covered automatically by the real-Redis
# integration test PresentationSealCoordinatorIntegrationTests.RestartSafety_DrainsAfterReconnect,
# so no manual instrumentation is required. (Out of scope for the standard quickstart.)
```

For the formal SC-119-007 verification (5 consecutive restart-while-pending tests), see the integration test `PresentationSealCoordinatorIntegrationTests.RestartSafety_DrainsAfterReconnect`.

---

## Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| `VAL_CHAIN_001` in Blueprint logs | Feature not deployed; old code path active | Rebuild blueprint-service image: `docker compose build --no-cache blueprint-service && docker compose up -d --force-recreate blueprint-service` |
| `VAL_BP_003` in Blueprint logs | Same as above | Same |
| `seal-wait queue depth growing without bound` | Subscriber not consuming events | Check `transaction:confirmed` Redis Streams subscription health: `docker compose logs blueprint-service \| Select-String 'PresentationSealSubscriber'` |
| `failed-validator-reject` sentinel observed in clean run | Should not happen — investigate | Capture full Blueprint + Validator logs and file an issue |

---

## Success criteria checklist

- [ ] SC-119-001 — 10 of 10 walkthrough runs pass
- [ ] SC-119-002 — Fast-citizen success rate 100% (proxy: walkthrough Phase 2 of step 2 above)
- [ ] SC-119-003 — Callback-to-next-action-ready ≤ 30 s (visible in `seal_wait_seconds` histogram p95)
- [ ] SC-119-005 — Never-seals failure recorded within validity window (step 4)
- [ ] SC-119-006 — Operator metrics populated (step 3)
- [ ] SC-119-008 — `dotnet test --filter "FullyQualifiedName~PresentationLifecycle"` all green
- [ ] SC-119-009 — Validator fork-resistance preserved (existing tests in `Sorcha.Validator.Service.Tests`)
