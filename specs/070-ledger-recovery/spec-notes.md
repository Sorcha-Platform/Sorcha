# 070: Blueprint Service Ledger Recovery & Register Status Sync

## Problem

`InMemoryPublishedBlueprintStore` is volatile — published blueprints are lost on Blueprint Service restart. The data IS on the ledger (publish transactions in Register Service/MongoDB) but the Blueprint Service never reads it back.

This also means register status (online/offline, height, health) is stale after restart.

## Approach: Recovery from Ledger (Option B)

On startup, Blueprint Service rebuilds its state from the authoritative ledger:

### Startup Recovery Flow

1. Blueprint Service starts
2. Query Tenant Service for all registers the platform knows about (via subscriptions or a known registers list)
3. For each register, query Register Service for blueprint-publish transactions (filter by transaction type)
4. Rebuild `InMemoryPublishedBlueprintStore` from those transactions
5. Update local register status (online, height, last activity) based on query success/failure
6. Service marked as "ready" only after recovery completes (health check gates on this)

### Register Status Sync

- Successful query → register is online, update height from response
- Failed query / timeout → register is offline or degraded
- This replaces any stale cached status from before restart
- Periodic refresh (background timer) keeps status current during runtime

### Key Design Points

- **Ledger is single source of truth** — no EF Core table for published blueprints
- **Startup latency** — recovery adds seconds to startup; health check should gate readiness
- **Idempotent** — re-reading the same transactions produces the same state
- **Peer replication** — handles blueprints published while service was down
- **Graceful degradation** — if a register is unreachable, skip it and retry later

## Affected Services

- **Blueprint Service**: Startup recovery logic, published store population, register status model
- **Register Service**: May need a "get transactions by type" query if not already available
- **Aspire AppHost**: Health check dependency — API Gateway should wait for Blueprint Service recovery

## Related Issues

- Discovered during 069-pending-actions-ux walkthrough testing
- Systemadmin subscribes to register, sees it in list, but New Submission shows no blueprints because InMemoryPublishedBlueprintStore is empty after restart
