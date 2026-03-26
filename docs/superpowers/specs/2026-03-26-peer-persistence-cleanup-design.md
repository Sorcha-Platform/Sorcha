# Peer Service Persistence Fix + Stale Data Cleanup

**Date:** 2026-03-26
**Status:** Approved
**Scope:** Sorcha.Peer.Service — PostgreSQL migration, docker-compose fix, periodic data cleanup

---

## Problem

The peer service's PostgreSQL migration has never been applied. The `peer.*` schema and tables (`Peers`, `RegisterSubscriptions`, `SyncCheckpoints`, `queued_transactions`) do not exist in the `sorcha_peer` database. Every write to `PeerListManager.PersistPeerAsync()` fails with:

```
42P01: relation "peer.Peers" does not exist
```

Additionally, `docker-compose.yml` does not declare a `postgres` dependency for `peer-service`, so the service can start before PostgreSQL is ready.

Beyond the missing migration, there is no mechanism to clean stale transitory data (dead peers, expired queue items, orphaned checkpoints), leading to unbounded table growth over time.

## Design

### 1. Auto-Migrate on Startup

Add `Database.MigrateAsync()` after app build, matching the pattern used by Blueprint, Wallet, and Tenant services:

```csharp
// In Program.cs, after var app = builder.Build(); and before app.Run()
if (!string.IsNullOrEmpty(peerDbConnectionString))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

### 2. Docker-Compose Dependency

Add `postgres` with `condition: service_healthy` to the peer-service `depends_on` block.

### 3. BanExpiresAt Field

Add an optional `BanExpiresAt` property to `PeerNodeEntity` and `PeerNode`:

- `DateTimeOffset? BanExpiresAt` — if set, ban auto-expires at this time; if null, ban is permanent
- `BanPeerAsync` gains an optional `TimeSpan? duration` parameter
- When duration is provided: `BanExpiresAt = DateTimeOffset.UtcNow + duration`
- When duration is null: `BanExpiresAt = null` (permanent)

Requires a new EF Core migration to add the column.

### 4. PeerDataCleanupService

A new `BackgroundService` that runs periodic DELETE sweeps against PostgreSQL:

| Table | Cleanup Rule | Interval |
|-------|-------------|----------|
| `Peers` | Remove non-seed peers where `LastSeen < (now - 30 min)` | 5 min |
| `Peers` | Remove peers where `IsBanned = true AND BanExpiresAt IS NOT NULL AND BanExpiresAt < now` — unban (set `IsBanned = false`, clear ban fields) | 5 min |
| `queued_transactions` | Remove rows where `Status IN ('Completed', 'Failed') AND EnqueuedAt < (now - 1 hour)` | 5 min |
| `queued_transactions` | Remove rows where `EnqueuedAt + TTL seconds < now` (TTL expired regardless of status) | 5 min |
| `SyncCheckpoints` | Remove rows where `PeerId` not in current `Peers` table | 15 min |

The service also evicts stale peers from the in-memory `ConcurrentDictionary` in `PeerListManager` to keep memory and database in sync.

#### Cleanup Flow

```
PeerDataCleanupService (BackgroundService)
  │
  ├── Every 5 min:
  │   ├── Evict stale peers (non-seed, LastSeen > 30 min)
  │   │   ├── DELETE FROM peer."Peers" WHERE ...
  │   │   └── Remove from PeerListManager in-memory dictionary
  │   ├── Unban expired bans
  │   │   ├── UPDATE peer."Peers" SET IsBanned=false, BanReason=NULL, BannedAt=NULL, BanExpiresAt=NULL WHERE ...
  │   │   └── Update PeerListManager in-memory state
  │   └── Purge completed/failed/expired queued transactions
  │       └── DELETE FROM peer."queued_transactions" WHERE ...
  │
  └── Every 15 min:
      └── Purge orphaned sync checkpoints
          └── DELETE FROM peer."SyncCheckpoints" WHERE PeerId NOT IN (SELECT PeerId FROM peer."Peers")
```

#### Configuration

Cleanup intervals and thresholds are configurable via `PeerServiceConfiguration`:

```csharp
public class DataCleanupConfiguration
{
    public int CleanupIntervalMinutes { get; set; } = 5;
    public int StalePeerMinutes { get; set; } = 30;
    public int CompletedTransactionRetentionMinutes { get; set; } = 60;
    public int CheckpointCleanupIntervalMinutes { get; set; } = 15;
}
```

### 5. Healthy Peer Check Update

`PeerListManager.GetHealthyPeers()` already filters by `LastSeen` and `FailureCount`. After this change, it should also exclude peers where `IsBanned = true AND (BanExpiresAt IS NULL OR BanExpiresAt > now)` — i.e., still-active bans.

Currently `GetHealthyPeers()` already filters `!p.IsBanned`, so no change needed for the basic case. The expired-ban cleanup will clear `IsBanned` before the next health check cycle.

## Files Changed

| File | Change |
|------|--------|
| `src/Services/Sorcha.Peer.Service/Program.cs` | Add `MigrateAsync()` call |
| `docker-compose.yml` | Add `postgres` dependency for peer-service |
| `src/Services/Sorcha.Peer.Service/Core/PeerNode.cs` | Add `BanExpiresAt` property |
| `src/Services/Sorcha.Peer.Service/Data/PeerDbContext.cs` | Add `BanExpiresAt` to entity + model config |
| `src/Services/Sorcha.Peer.Service/Data/Migrations/` | New migration for `BanExpiresAt` column |
| `src/Services/Sorcha.Peer.Service/Discovery/PeerListManager.cs` | Add `BanExpiresAt` to `BanPeerAsync`, add `EvictStalePeersAsync` method |
| `src/Services/Sorcha.Peer.Service/Services/PeerDataCleanupService.cs` | New background service |
| `src/Services/Sorcha.Peer.Service/Core/PeerServiceConfiguration.cs` | Add `DataCleanupConfiguration` section |

## Testing

- Unit test: `PeerDataCleanupService` correctly identifies stale peers, expired bans, expired transactions
- Unit test: `BanPeerAsync` with duration sets `BanExpiresAt`; without duration leaves it null
- Integration test: migration applies cleanly to empty `sorcha_peer` database
- Manual: `docker-compose up -d` — peer service starts, tables created, peers persist and clean up
