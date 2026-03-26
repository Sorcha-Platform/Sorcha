# Research: 070-ledger-recovery

## Decision 1: How to Query Blueprint-Publish Transactions

**Decision**: Use `GET /api/registers/{registerId}/query/blueprints/{blueprintId}/transactions` for targeted queries, OR iterate all Control transactions and filter by `MetaData.TransactionType == BlueprintPublish`.

**Rationale**: The Register Service already has `QueryManager.GetTransactionsByBlueprintAsync()` which filters by `MetaData.BlueprintId`. However, for recovery we need ALL blueprint-publish transactions on a register (not for a specific blueprintId). A new endpoint `GET /api/registers/{registerId}/transactions?type=Control&transactionType=BlueprintPublish` is needed — or we use OData filtering on the existing transactions endpoint.

**Chosen approach**: Add a dedicated endpoint `GET /api/registers/{registerId}/blueprints/published` that returns all blueprint-publish control transactions for a register. This is cleaner than abusing OData filters and gives a purpose-built contract.

**Alternatives considered**:
- OData filter on existing transactions endpoint — rejected: complex filter syntax, fragile
- Scan all transactions and filter client-side — rejected: O(n) over entire ledger, wasteful
- Use Redis as persistent store instead — rejected: doesn't align with ledger-first principle

## Decision 2: Recovery Trigger — Hosted Service vs Middleware

**Decision**: Use a `BackgroundService` (hosted service) that runs at startup, with a `IHostedServiceStartupFilter` to gate readiness.

**Rationale**: .NET hosted services run at startup before the HTTP pipeline accepts requests. Combined with the existing health check pattern, the recovery service can set a `_isReady` flag that the health endpoint reads. Aspire's health check dependency already waits for Blueprint Service to report healthy before routing via API Gateway.

**Alternatives considered**:
- Middleware that blocks first request — rejected: ugly, hard to test, race conditions
- Startup filter that delays app.Run() — rejected: blocks entire pipeline including health checks
- Lazy initialization on first API call — rejected: user sees empty state during recovery

## Decision 3: Register Discovery Source

**Decision**: Query Register Service's `GET /api/registers` endpoint at startup to get all known registers.

**Rationale**: The Register Service already persists all registers in MongoDB and exposes them via API. The Blueprint Service already has an `IRegisterServiceClient` for communicating with Register Service. No new discovery mechanism needed.

**Alternatives considered**:
- Query Tenant Service for subscriptions — rejected: subscriptions are org-scoped, we need ALL registers
- Hardcode register list in config — rejected: doesn't scale, requires config changes for new registers
- Read from local file cache — rejected: adds complexity, stale cache risk

## Decision 4: Blueprint Payload in Publish Transaction

**Decision**: The publish transaction's payload contains the serialized blueprint JSON. Recovery deserializes it to rebuild the `PublishedBlueprint` model.

**Rationale**: Confirmed from code — `_registerClient.PublishBlueprintToRegisterAsync(registerId, blueprintId, blueprintJson, "system")` sends the full blueprint JSON. The transaction payload on the ledger IS the complete blueprint definition. No additional fetch needed.

## Decision 5: Health Check Readiness Gating

**Decision**: Add a `_recoveryComplete` boolean to the health check. Return HTTP 503 (Service Unavailable) with `"status": "recovering"` until recovery finishes. Aspire/API Gateway will not route to the service until it reports 200.

**Rationale**: The existing health endpoint returns 200 with metrics. Adding a recovery gate is a minimal change — check the flag before returning 200. Docker Compose and Aspire health checks already poll this endpoint.

## Decision 6: Periodic Refresh Strategy

**Decision**: The same `BackgroundService` that handles startup recovery also runs a periodic timer (configurable, default 60s) to re-check registers and discover new publications.

**Rationale**: Single service handles both concerns (startup recovery + runtime refresh). The timer re-runs the same idempotent recovery logic. New blueprints are discovered, offline registers retried.

**Alternatives considered**:
- Separate hosted service for refresh — rejected: duplicates logic, harder to coordinate
- SignalR push from Register Service — rejected: Blueprint Service doesn't subscribe to Register events currently
- Redis pub/sub notification — rejected: adds coupling, not all deployments have Redis
