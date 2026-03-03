# Research: Inbound Transaction Routing & User Notification

**Feature**: 047-inbound-tx-routing
**Date**: 2026-03-02
**Status**: Complete

## R1: Redis Bloom Filter for Local Address Detection

**Decision**: Use StackExchange.Redis bit-array operations to implement a bloom filter in Redis, stored in Register Service's Redis instance.

**Rationale**: No Redis-backed bloom filter exists in the codebase. The existing bloom filter in `GossipProtocolEngine` is in-memory and used for gossip dedup — different purpose. Redis Stack's `BF.ADD`/`BF.EXISTS` commands would be ideal but Redis Stack is not currently deployed. Using raw `SETBIT`/`GETBIT` with multiple hash functions provides the same functionality without requiring Redis Stack modules.

**Alternatives Considered**:
- **Redis Stack BF.* commands**: Cleaner API but requires deploying Redis Stack instead of standard Redis. Rejected to avoid infrastructure changes.
- **In-memory bloom filter**: Simpler but lost on restart, requires rebuild from scratch. Rejected because Redis already provides persistence.
- **Redis SET with full addresses**: Exact matching via `SISMEMBER`. Rejected because bloom filter provides O(1) probabilistic check at much lower memory cost for large address sets.
- **ConcurrentDictionary in-process**: Fast but not shared across Register Service instances. Rejected for scaling reasons.

**Key Findings**:
- Redis key namespace will be `register:bloom:{registerId}` (bit-array) + `register:bloom:params:{registerId}` (hash count, size)
- 7 services already reference Redis via Aspire — pattern well-established
- All services use Polly resilience + fail-open patterns for Redis
- Redis connection string resolved via Aspire service discovery

## R2: gRPC Contract Patterns

**Decision**: Follow existing proto conventions — snake_case file names, PascalCase C# namespaces, unary RPCs for simple operations, server streaming for bulk data transfer (SyncDockets).

**Rationale**: 14 proto files already exist with established patterns. Server streaming is used for PullDocketChain, PullDocketTransactions, and SubscribeToRegister. The new SyncDockets endpoint follows the same streaming pattern for docket recovery.

**Alternatives Considered**:
- **REST endpoints instead of gRPC**: Rejected because all inter-service communication in Sorcha uses gRPC. REST is only for external/UI-facing APIs.
- **Bidirectional streaming for SyncDockets**: Rejected because recovery is a one-way pull — Register Service requests, Peer Service streams back. No need for bidirectional.
- **Message queue (Redis Streams) for notifications**: Rejected for the primary notification path because gRPC provides direct acknowledgment. Redis Streams already used in Register Storage for event publishing — could complement but not replace.

**Key Findings**:
- Wallet proto has 4 unary RPCs (GetWalletDetails, SignData, VerifySignature, GetDerivedKey)
- Tenant proto exists but has NO server implementation — not blocking since we'll add preference fields to existing REST endpoints
- Auth interceptor on Peer Service validates peer JWT — new SyncDockets endpoint needs same auth
- Proto files live in each service's `Protos/` directory

## R3: SignalR/EventsHub Notification Delivery

**Decision**: Wire the existing EventsHub push mechanism in Blueprint Service's NotificationService to deliver inbound action notifications. Register Service triggers Wallet Service via gRPC, Wallet Service resolves user and publishes to EventsHub via existing SignalR infrastructure.

**Rationale**: EventsHub (`/hubs/events`) exists but its push side is NOT wired — `CreateEventAsync` saves to PostgreSQL but never pushes to the hub. ActionsHub + NotificationService in Blueprint Service already demonstrates the working pattern: `IHubContext<ActionsHub>` sends typed messages. We need to replicate this pattern for EventsHub or route through ActionsHub.

**Alternatives Considered**:
- **New dedicated NotificationHub**: Rejected to avoid hub proliferation. EventsHub already exists for this purpose.
- **Route through ActionsHub**: Considered because ActionsHub already works and sends action notifications. However, ActionsHub is owned by Blueprint Service and tightly coupled to blueprint actions. Better to wire EventsHub properly.
- **Wallet Service hosts its own hub**: Rejected because SignalR hubs are proxied through API Gateway to the UI. Wallet Service is internal-only.

**Key Findings**:
- 4 SignalR hubs exist: EventsHub (/hubs/events), ActionsHub (/actionshub), ChatHub (/hubs/chat), RegisterHub (/hubs/register)
- RegisterHub has typed `IRegisterHubClient` with `TransactionConfirmed`, `DocketSealed` — could be extended
- ActivityEvent stored in PostgreSQL via `BlueprintEventsDbContext`
- Unread count is REST-polled; SignalR push handler exists on client but server never pushes
- All hubs proxied through API Gateway YARP routes
- SignalR Redis backplane NOT yet configured (TODO in Blueprint Service) — needed for multi-instance

## R4: Wallet Address Ownership Resolution

**Decision**: Add new gRPC endpoints on Wallet Service: `GetAllLocalAddresses` (for bloom filter rebuild) and `NotifyInboundTransaction` (for routing matched transactions). Use existing `IWalletRepository.GetByAddressAsync` for address-to-user resolution.

**Rationale**: Wallet Service already tracks ownership via `Owner` (user sub from JWT) and `Tenant` fields on the Wallet entity. `WalletAddress` entity has `Address`, `DerivationPath`, `PublicKey`, `IsUsed`. The `GetByAddressAsync` repository method returns the full Wallet with Owner — perfect for address-to-user mapping. No new storage needed.

**Alternatives Considered**:
- **Duplicate address index in Register Service**: Rejected because it violates single source of truth — Wallet Service owns address data.
- **REST endpoint for address lookup**: Rejected because this is service-to-service communication (gRPC pattern).
- **Event-driven address registration (publish/subscribe)**: Considered for loose coupling. Rejected because address registration needs confirmation (bloom filter updated) before the wallet creation flow completes.

**Key Findings**:
- WalletAddress PK is `Guid Id` with unique index on `Address`
- Server-side address generation disabled — client derives, then registers via POST
- Wallet Service has no background/hosted services currently — NotificationDigestWorker will be the first
- Transaction flow: Blueprint/External → Validator → Unverified Pool → ValidationEngine → VerifiedQueue → DocketBuilder → Register Service MongoDB
- Register Service stores transactions after docket sealing — this is where bloom filter check happens

## R5: Notification Preferences in Tenant Service

**Decision**: Extend `UserPreferences` entity with two new fields: `NotificationMethod` (enum: InApp, InAppPlusEmail, InAppPlusPush) and `NotificationFrequency` (enum: RealTime, HourlyDigest, DailyDigest). Use existing PATCH partial update pattern on `UserPreferenceEndpoints`.

**Rationale**: `UserPreferences` currently has only `NotificationsEnabled` (bool toggle), plus Theme, Language, TimeFormat, DefaultWalletAddress, TwoFactorEnabled. The existing PATCH endpoint already supports partial updates — adding two new fields is straightforward. The `PushSubscription` entity already exists for Web Push (endpoint, P256dhKey, AuthKey).

**Alternatives Considered**:
- **Separate NotificationPreference entity**: Rejected as over-engineering — two additional fields on an existing entity is simpler.
- **Store preferences in Wallet Service**: Rejected because Tenant Service owns user profiles and preferences.
- **Store preferences in Redis for fast lookup**: Considered for performance. Rejected because preferences change rarely and can be cached locally. Wallet Service can cache preferences after first lookup.

**Key Findings**:
- Database: PostgreSQL via EF Core, single migration exists (20260226230555_InitialCreate)
- New migration needed for the two additional columns
- JWT issued by Tenant Service, validated by all others — no auth changes needed
- Token revocation backed by Redis — not relevant here
- Only background service is `DatabaseInitializerHostedService` (migrations + seeding)

## R6: Register Recovery Mode

**Decision**: Create a `RegisterRecoveryService` (IHostedService) in Register Service that detects docket gaps on startup, requests missing dockets via new `Peer.SyncDockets` streaming endpoint, and processes bloom filter matches during recovery for catch-up notifications.

**Rationale**: Register Service already has `AdvertisementResyncService` and `RegisterEventBridgeService` as background services — the pattern is established. The existing `register_sync.proto` has `PullDocketChain` (server streaming) and `GetRegisterSyncStatus` but no dedicated recovery endpoint. A new `SyncDockets` streaming RPC on Peer Service provides sequential docket delivery from a starting point to head.

**Alternatives Considered**:
- **Reuse PullDocketChain**: Considered since it already streams dockets. Rejected because PullDocketChain is designed for chain verification (sends full chain), not targeted recovery (send from docket N to head).
- **REST polling for recovery**: Rejected because streaming is more efficient for potentially thousands of dockets.
- **Push-based recovery (peer pushes to recovering node)**: Rejected because the recovering node needs to control the pace and order of recovery.

**Key Findings**:
- DocketBuilder in Validator Service calls `WriteDocketAndTransactionsAsync` which writes to Register Service
- Dockets are sequentially numbered per register — gap detection is trivial (compare local latest vs network head)
- Health check endpoints follow `/health` and `/alive` pattern per constitution
- New endpoint `/health/sync` or extend existing health check with sync status
- Recovery state should be tracked in Redis for persistence across service restarts
