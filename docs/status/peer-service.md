# Peer Service Status

**Overall Status:** 100% MVD COMPLETE
**Location:** `src/Services/Sorcha.Peer.Service/`
**Last Updated:** 2026-03-01

---

## Summary

| Component | Status | Notes |
|-----------|--------|-------|
| Phase 1-2: Foundation | 100% | Setup, entities, config |
| Phase 3: Core (Hub) | 100% | Connection, replication, heartbeat, push notifications |
| PEER-023: P2P Topology Refactor | 100% | 10 phases, replaced hub model with equal-peer architecture |
| PEER-024: Management & Observability | 100% | REST endpoints, CLI commands, Blazor UI |
| PR #110 Review Fixes | 100% | 12 issues (3 critical, 4 high, 5 medium) |
| Observability | 100% | 7 metrics, 6 traces |
| gRPC Service | 100% | 7 RPCs implemented |
| Register Replication | 100% | Full replication pipeline |
| Live Subscriptions | 100% | Real-time register subscriptions |
| Tests | 504 passing | Unit tests comprehensive |

---

## P2P Architecture (PEER-023 / PEER-024) - COMPLETE

The Peer Service was refactored from a 3-hardcoded-hub-node model to a true P2P topology:

- **Equal-peer architecture** — all nodes equivalent; seed nodes serve only as bootstrap
- **PeerConnectionPool** — multi-peer gRPC channel management with idle cleanup
- **PeerExchangeService** — gossip-style mesh network discovery
- **RegisterAdvertisementService** — register-aware peering
- **RegisterCache + RegisterReplicationService** — per-register sync with ForwardOnly/FullReplica modes
- **P2P heartbeat** — PeerHeartbeatBackgroundService with per-register version exchange
- **PeerListManager** — migrated from SQLite to PostgreSQL (EF Core)

---

## PR #110 Review Fixes (2026-02-08) - COMPLETE

12 issues from two detailed code reviews resolved:

### Critical (3)
1. **Race condition** — `Dictionary<>` replaced with `ConcurrentDictionary<>` in `RegisterSyncBackgroundService`
2. **EF Core migration** — `InitialPeerSchema` migration generated for `peer` schema (PeerNodes, RegisterSubscriptions, SyncCheckpoints)
3. **Hardcoded password** — design-time factory now reads `PEER_DB_CONNECTION` env var with dev-only fallback

### High (4)
4. **gRPC channel idle timeout** — changed from `Timeout.InfiniteTimeSpan` to 5 minutes
5. **JWT authentication** — added `AuthenticationExtensions.cs` with `RequireAuthenticated`, `CanManagePeers`, `RequireService` policies; middleware wired in `Program.cs`
6. **RegisterCache eviction** — bounded cache with configurable `MaxCachedTransactionsPerRegister` (100K) and `MaxCachedDocketsPerRegister` (10K); oldest entries evicted by version
7. **Replication timeouts** — overall `ReplicationTimeoutMinutes` (30 min default); batched docket pulls using `DocketPullBatchSize` (100 default) instead of unlimited

### Medium (5)
8. **Magic numbers** — replaced with `PeerServiceConstants.MaxConsecutiveFailuresBeforeDisconnect` (5), `MaxConsecutiveFailuresBeforeError` (10), `GossipExchangePeerCount` (3)
9. **Seed node reconnection** — `ReconnectDisconnectedSeedNodesAsync()` added to `PeerConnectionPool`, called every heartbeat cycle
10. **gRPC message size limits** — 16 MB send/receive limits configured
11. **EnableDetailedErrors** — scoped to development environment only
12. **Idle connection cleanup** — wired into heartbeat loop (every 10th cycle, ~5 min), cleaning connections idle >15 min

---

## Authentication

JWT Bearer authentication via shared `ServiceDefaults.AddJwtAuthentication()`:

| Endpoint | Policy |
|----------|--------|
| Ban/unban/reset peers | `CanManagePeers` |
| Subscribe/unsubscribe/purge registers | `RequireAuthenticated` |
| Monitoring (peer list, health, stats) | Anonymous |
| Connected peers (detailed) | Authenticated (count-only for anonymous) |

---

## Completed Features

1. P2P topology with seed node bootstrap
2. Gossip-style peer exchange for mesh discovery
3. Per-register replication (ForwardOnly / FullReplica)
4. Register-aware peering with version advertisements
5. P2P heartbeat with version lag detection
6. Peer ban/unban/reset management (REST + CLI + Blazor UI)
7. JWT authentication with authorization policies
8. EF Core PostgreSQL persistence with initial migration
9. Bounded register cache with size-based eviction
10. gRPC hardening (message limits, idle timeout, replication timeout)
11. Seed node reconnection and idle connection cleanup
12. Comprehensive observability (7 metrics, 6 traces)

---

## Completed (Phase E - 2026-03-01)

13. gRPC service with 7 RPCs
14. Register replication pipeline
15. Live register subscriptions

## Deferred (Post-MVD)

1. BLS threshold coordination
2. TLS support for gRPC in production
3. Performance tests (replication throughput)
4. Integration tests (P2P multi-node scenarios)

---

**Back to:** [Development Status](../development-status.md)
