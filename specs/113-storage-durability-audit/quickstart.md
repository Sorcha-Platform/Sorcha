# Quickstart: Storage Provider Audit and Validator Mempool Durability

**Feature**: 113-storage-durability-audit
**Audience**: Sorcha developers verifying the feature locally before review.

This walkthrough confirms the four headline behaviours from the spec
end-to-end on a developer machine. Each scenario maps to a P1 user story.

---

## Prerequisites

- .NET 10 SDK
- Docker Desktop running
- Sorcha repository checked out at the `113-storage-durability-audit` branch
  (or any branch where the eight rollout PRs have all merged)

```bash
git checkout 113-storage-durability-audit
docker-compose down -v   # clean slate
dotnet restore && dotnet build
```

---

## Scenario A — Misconfigured Production deploy fails fast (US1)

Goal: prove that a service refuses to start in Production with an audited
interface on in-memory.

```bash
# Run Wallet Service in Production env without a Wallet DB connection string.
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__wallet-db= \
dotnet run --project src/Services/Sorcha.Wallet.Service
```

**Expected output:**

```
[STORAGE-FALLBACK] IWalletRepository → InMemoryWalletRepository — DATA WILL NOT SURVIVE RESTART. Reason: no Postgres connection string configured
…
fail: Sorcha.ServiceDefaults.Storage.StorageRegistrationEnforcement[0]
      Service refused to start: 1 audited storage interface(s) on in-memory backends in Production:
        - IWalletRepository → InMemoryWalletRepository
      Set Storage:AllowInMemoryInProduction=true to bypass (not recommended).
Unhandled exception. System.InvalidOperationException: Audited storage interface(s) on in-memory in Production
```

**Verify the bypass:**

```bash
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__wallet-db= \
Storage__AllowInMemoryInProduction=true \
dotnet run --project src/Services/Sorcha.Wallet.Service
```

Expected: service starts, with a `LogCritical` recording the bypass. Curl
`http://localhost:7001/health` and confirm the `storage-providers` check
reports `Degraded`.

**Verify the dev-mode warning:**

```bash
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__wallet-db= \
dotnet run --project src/Services/Sorcha.Wallet.Service
```

Expected: service starts, log line shows `[STORAGE-FALLBACK]` warning,
`/health` reports `Degraded`, no fail-fast.

---

## Scenario B — Validator mempool survives restart (US2)

Goal: prove that verified-but-not-yet-sealed transactions persist across a
validator restart.

```bash
# Stand up the full stack with Redis and Validator wired up.
docker-compose up -d redis postgres
dotnet run --project src/Apps/Sorcha.AppHost
```

In a second terminal, enqueue verified transactions for a test register and
confirm they land in Redis:

```bash
# Use the existing sorcha CLI to publish a blueprint and submit transactions.
dotnet run --project src/Apps/Sorcha.Cli -- \
  walkthrough run --name validator-restart-smoke

# Inspect the mempool directly:
docker exec -it $(docker ps -qf name=redis) redis-cli \
  ZRANGE 'sorcha:vtq:{your-register-id}:available' 0 -1 WITHSCORES
```

You should see the enqueued transaction IDs.

Now kill the validator process:

```bash
# In the AppHost terminal, Ctrl-C once. Or:
docker stop $(docker ps -qf name=sorcha-validator)
```

Re-inspect Redis — the keys are still there. Restart the validator:

```bash
docker start $(docker ps -aqf name=sorcha-validator)
# Or relaunch via AppHost.
```

Within one docket-build cycle (default ~3s), the validator claims and seals
the transactions. Confirm via the existing register query:

```bash
dotnet run --project src/Apps/Sorcha.Cli -- \
  register list-transactions --register-id <your-register-id>
```

The transactions appear in a sealed docket. None were re-validated — the
validator's logs show `Claimed N transactions for register R from mempool`,
not `Verifying transaction T`.

---

## Scenario C — HAIP nonces cannot be replayed under concurrent consume (US3)

Goal: prove that exactly one of N concurrent nonce-consume calls succeeds.

This scenario is verified by automated test rather than manual
walkthrough — the race window is too narrow to hit reliably by hand.

```bash
dotnet test tests/Sorcha.Haip.Service.Tests \
  --filter "FullyQualifiedName~ConcurrentConsume"
```

**Expected output:**

```
Passed!  - Failed:    0, Passed: N, Skipped: 0
```

Including:
- `NonceStoreTests.ConcurrentConsume_ExactlyOneSucceeds` — 100 tasks,
  exactly one returns true.
- `PreAuthCodeStoreTests.ConcurrentConsume_ExactlyOneSucceeds` — same.
- `PresentationRequestStoreTests.ConcurrentTerminalTransition_ExactlyOneWins` —
  CAS-backed terminal-state race resolves to one winner.

If you want manual verification, instrument `NonceStore.ConsumeAsync` with a
counter and post 100 simultaneous credential-issuance requests via `curl &`.
The counter shows exactly one success.

---

## Scenario D — Cross-backend contract tests catch implementation drift (US4)

Goal: prove that the contract-test suite fails when one implementation drifts
from the other.

```bash
# Baseline: all contract tests pass.
dotnet test tests/Sorcha.Blueprint.Service.Tests \
  --filter "FullyQualifiedName~ContractTests"
```

Expected: `Passed!`

Now simulate drift. Edit `InMemoryInstanceStore.UpdateAsync` to skip the
read-only-mirror check:

```csharp
// In src/Services/Sorcha.Blueprint.Service/Storage/InMemoryInstanceStore.cs
// Comment out the IsReadOnlyMirror guard at line 55-59.
```

Re-run:

```bash
dotnet test tests/Sorcha.Blueprint.Service.Tests \
  --filter "FullyQualifiedName~InMemoryInstanceStoreContractTests"
```

Expected: `UpdateAsync_OnReadOnlyMirror_ThrowsInvalidOperation` fails with
a clear FluentAssertions message naming the contract violation.

Revert the edit:

```bash
git checkout src/Services/Sorcha.Blueprint.Service/Storage/InMemoryInstanceStore.cs
```

The same drift-then-fail experiment works for `IActionStore`,
`IWalletRepository`, `IVerifiedTransactionQueue`, and
`IAtomicDistributedCache`.

---

## Scenario E — Operator metrics are observable via Aspire (US5)

Goal: prove that the new OpenTelemetry metrics flow to the Aspire dashboard.

With the AppHost running, open the Aspire dashboard:

```
http://localhost:18888
```

Navigate to **Metrics** in the left nav, then select any service from the
resource dropdown. In the metric explorer, search for `sorcha_storage` —
both `sorcha_storage_provider_info` and `sorcha_storage_fallback_active`
should appear with one observation per audited interface registered by that
service.

**Expected observations** (Wallet service, healthy config):

| Instrument                       | Tags                                                                                                | Value |
| -------------------------------- | --------------------------------------------------------------------------------------------------- | ----- |
| `sorcha_storage_provider_info`   | `service=wallet, interface=IWalletRepository, implementation=EfCoreWalletRepository, backend=postgres` | 1     |
| `sorcha_storage_provider_info`   | `service=wallet, interface=IAtomicDistributedCache, implementation=RedisAtomicDistributedCache, backend=redis` | 1     |
| `sorcha_storage_fallback_active` | `service=wallet, interface=IWalletRepository`                                                       | 0     |

Switch to the Validator service and search for `sorcha_validator_mempool` —
the size gauge updates as transactions are claimed and confirmed:

| Instrument                                       | Tags                                          | Value      |
| ------------------------------------------------ | --------------------------------------------- | ---------- |
| `sorcha_validator_mempool_size`                  | `register_id=reg-abc, state=available`        | (live)     |
| `sorcha_validator_mempool_size`                  | `register_id=reg-abc, state=claimed`          | (live)     |
| `sorcha_validator_mempool_lease_expired_total`   | `register_id=reg-abc`                         | counter    |

Run the validator-restart smoke from Scenario B and watch
`sorcha_validator_mempool_lease_expired_total` increment if you kill the
validator mid-claim.

If your environment has an OTLP collector forwarding to a long-term metrics
backend, the same instruments are queryable there — the alert rule for
"any audited interface on in-memory in Staging or Production" goes against
that backend.

---

## Cleanup

```bash
docker-compose down -v
git checkout master
```

---

## Failure modes worth knowing

- **Redis unavailable**: validator service refuses to start in Production
  with the same fail-fast pattern as Scenario A. In Development, falls back
  to in-memory with the warning. Mempool data is not shared across
  development restarts in this case.
- **MockRedis Lua-script divergence**: caught by the dedicated
  Testcontainers test (`RedisVerifiedTransactionQueueLuaSmokeTests.cs`). If
  this test fails after a Lua script change, the change is real-Redis-only.
- **Pre-existing flaky tests**: `Blueprint.Service.Tests` constructor NRE
  and `Validator.Service.Tests` compile errors (per MEMORY.md) are not
  caused by this feature. Filter around them: `--filter
  "FullyQualifiedName!~ProblemTests"`.
