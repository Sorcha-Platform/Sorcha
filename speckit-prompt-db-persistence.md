# Speckit Prompt: Blueprint Service Persistence & Validator Crash Recovery

## Context

Database persistence audit (2026-03-24) found three gaps across Sorcha services. Two are now resolved:
- **Peer Service**: Fixed — PostgreSQL wiring added to AppHost and docker-compose (PR #124)
- **Register, Tenant, Wallet**: Already fully durable (MongoDB, PostgreSQL, PostgreSQL respectively)

Two gaps remain:
1. **Blueprint Service** — all data stored in ConcurrentDictionary (lost on restart)
2. **Validator Service** — verified transaction queue is in-memory by design, but no crash recovery to re-validate from last confirmed docket

## Feature Description

### Part 1: Blueprint Service Persistence

The Blueprint Service currently uses in-memory stores (`InMemoryBlueprintStore`, `InMemoryPublishedBlueprintStore`, `InMemoryActionStore`, `InMemoryInstanceStore`). This data is lost on every service restart.

The persistence model must respect the register as the single source of truth:
- **Published blueprints** are transactions on the system register — the Blueprint Service should cache them in Redis, not own them in a local database
- **Instance execution state** is reconstructable from register transactions (each action produces a transaction) — the Blueprint Service should cache current state in Redis, rebuildable on cache miss
- **Drafts** are work-in-progress blueprints that have not been published to a register — these need durable local storage (PostgreSQL)
- **Templates** are the reusable blueprint template library — these need durable local storage (PostgreSQL), currently seeded from JSON files on startup

#### Draft Ownership & Access

Each draft has a local owner (the user who created it). The initial implementation should support single-owner access. The data model should include an `OwnerId` field and be designed so that shared or delegated access can be added later (e.g., a `BlueprintDraftAccess` table for collaborating designers), but do NOT implement shared access in this feature — just ensure the schema doesn't prevent it.

#### Published Blueprint & Instance Caching

Published blueprints retrieved from the register should be cached in Redis with appropriate TTL. Cache keys must include blueprint version to support the edge case where a blueprint is upgraded while existing instances are still running on the previous version. Two instances running different versions of the same blueprint must each resolve to their correct blueprint version.

Instance state should be cached in Redis, keyed by instance ID. On cache miss, the service reconstructs state by replaying the instance's transactions from the register. This is the existing `Instance.AccumulatedData` pattern — it just needs a Redis backing store instead of in-memory.

#### PostgreSQL Integration

Follow the same pattern as Tenant Service and Wallet Service:
- EF Core DbContext with auto-migrations on startup
- Add `sorcha_blueprint` database to AppHost, docker-compose, and postgres-init.sql
- Fallback to InMemory EF Core database when no connection string is configured
- Repository pattern with interface abstraction

### Part 2: Validator Crash Recovery

The Validator Service's verified transaction queue (`VerifiedTransactionQueue`) is intentionally in-memory — this is correct by design. However, after a crash or restart, the validator should re-check from the last confirmed docket rather than starting with an empty queue and waiting for new submissions.

On startup, the Validator should:
1. Query the register for the current docket height
2. Check Redis unverified pool for any pending transactions that arrived during downtime
3. Re-validate and process those transactions through the normal pipeline
4. Resume normal polling

This is a reconciliation step, not a persistence change — the validator's in-memory queue remains ephemeral.

## Requirements Summary

### Blueprint Service
- Migrate drafts and templates to PostgreSQL via EF Core
- Cache published blueprints and instance state in Redis (register is source of truth)
- Redis cache keys include blueprint version for concurrent version support
- Draft ownership model with single owner (schema supports future delegation)
- InMemory fallback when no connection string configured
- Aspire AppHost + docker-compose + postgres-init.sql updates

### Validator Service
- Add startup reconciliation: re-check unverified pool against last confirmed docket
- No persistence changes to verified queue (in-memory by design)

## Notes
- Blueprint Service currently has a code comment "later: replace with EF Core + PostgreSQL"
- Schema library index already uses durable MongoDB — no change needed there
- The `InMemoryBlueprintStore` etc. interfaces are already abstracted — implementations can be swapped
- Existing `IRepository<T>` pattern from `Sorcha.Storage.Abstractions` may be applicable for drafts/templates
