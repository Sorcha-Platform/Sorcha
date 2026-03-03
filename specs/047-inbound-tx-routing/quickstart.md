# Quickstart: Inbound Transaction Routing & User Notification

**Feature**: 047-inbound-tx-routing
**Date**: 2026-03-02

## Prerequisites

- Docker Desktop running (for Redis, PostgreSQL, MongoDB)
- .NET 10 SDK installed
- Sorcha services running via `docker-compose up -d` or Aspire

## Integration Scenarios

### Scenario 1: Address Registration on Wallet Creation

**Flow**: User creates wallet → Wallet Service registers address with Register Service → Bloom filter updated

1. User creates a wallet via UI or API
2. Wallet Service POST `/api/wallets` creates wallet and derives initial address
3. Wallet Service calls Register Service gRPC `RegisterLocalAddress(address)`
4. Register Service adds address to bloom filter in Redis
5. Confirm: `redis-cli GETBIT register:bloom:{registerId} {position}` returns 1

**Verification**:
- Create a wallet
- Check Redis key `register:bloom:*` exists
- Verify address count incremented in `register:bloom:params:*`

### Scenario 2: Inbound Action Notification (Real-Time)

**Flow**: Transaction arrives → Register Service stores → Bloom filter match → Wallet Service resolves user → SignalR push

1. Submit a transaction targeting a local wallet address (via Blueprint action or direct)
2. Validator processes and seals into docket
3. Register Service stores docket + transactions in MongoDB
4. Register Service checks each recipient address against bloom filter
5. On match: Register Service calls Wallet Service gRPC `NotifyInboundTransaction`
6. Wallet Service resolves address → wallet → user, extracts blueprint metadata
7. Wallet Service checks user's notification preference (default: real-time)
8. Wallet Service publishes notification via SignalR EventsHub
9. UI receives notification within 5 seconds

**Verification**:
- Open browser, sign in, observe notification bell
- Submit transaction targeting local address from another wallet
- Notification appears with blueprint name, action description, and link

### Scenario 3: Digest Notification Batching

**Flow**: Multiple transactions arrive → Events queued → Timer fires → Single digest delivered

1. Configure user preference to `HourlyDigest` via Settings → Notifications
2. Submit 5 transactions targeting local addresses over 30 minutes
3. Each triggers bloom filter match → Wallet Service queues in Redis DigestQueue
4. At the next hour boundary, `NotificationDigestWorker` fires
5. Worker dequeues all events for user, groups by blueprint
6. Single consolidated notification delivered via SignalR
7. No empty digest if no events occurred

**Verification**:
- Change notification frequency in settings
- Submit multiple transactions
- Verify no immediate notifications appear
- Wait for digest interval → single consolidated notification arrives

### Scenario 4: Register Recovery After Offline

**Flow**: Node offline → Transactions happen on network → Node restarts → Detects gap → Recovers → Catch-up notifications

1. Stop Register Service (simulate offline)
2. Submit transactions to the network (other nodes process them)
3. Restart Register Service
4. On startup: Register Service compares local latest docket vs network head (via Peer Service)
5. Gap detected → enters recovery mode
6. Streams missing dockets from Peer Service via `SyncDockets`
7. Each recovered docket checked against bloom filter
8. Matching transactions generate catch-up notifications
9. Recovery completes → health endpoint reports "synced"

**Verification**:
- Check health endpoint: `GET /health/sync` returns `{ "status": "recovering", "progress": 45 }`
- After recovery: `GET /health/sync` returns `{ "status": "synced" }`
- User receives notifications for missed actions

### Scenario 5: Bloom Filter Rebuild

**Flow**: Admin triggers rebuild → Register Service fetches all addresses from Wallet Service → Rebuilds bloom filter

1. Admin calls rebuild endpoint or service restarts
2. Register Service calls Wallet Service gRPC `GetAllLocalAddresses`
3. Wallet Service streams all active wallet addresses
4. Register Service clears existing bloom filter in Redis
5. Register Service inserts all addresses into new bloom filter
6. Rebuild completes within 30 seconds for up to 100,000 addresses

**Verification**:
- Trigger rebuild via admin endpoint
- Verify bloom filter params show correct address count
- Test known addresses against bloom filter → all return positive

## Key Endpoints

### REST (via API Gateway)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/health/sync` | Recovery/sync status for all registers |
| PATCH | `/api/users/preferences` | Update notification preferences |
| GET | `/api/users/preferences` | Get current notification preferences |
| POST | `/api/admin/registers/{id}/rebuild-index` | Force rebuild bloom filter |

### gRPC (service-to-service)

| Service | Method | Direction |
|---------|--------|-----------|
| Register | RegisterLocalAddress | Wallet → Register |
| Register | RemoveLocalAddress | Wallet → Register |
| Wallet | GetAllLocalAddresses | Register → Wallet |
| Wallet | NotifyInboundTransaction | Register → Wallet |
| Peer | SyncDockets (streaming) | Register → Peer |
| Peer | GetLatestDocketNumber | Register → Peer |

## Configuration

### Register Service (appsettings.json)

```json
{
  "BloomFilter": {
    "ExpectedAddressCount": 100000,
    "FalsePositiveRate": 0.001,
    "RebuildOnStartup": true
  },
  "Recovery": {
    "EnableAutoRecovery": true,
    "MaxDocketsPerBatch": 100,
    "RetryDelaySeconds": 5,
    "MaxRetries": 3,
    "HealthCheckStalenessSeconds": 10
  }
}
```

### Wallet Service (appsettings.json)

```json
{
  "Notifications": {
    "RealTimeRateLimitPerMinute": 10,
    "DigestCheckIntervalMinutes": 5,
    "DigestHourlyMinute": 0,
    "DigestDailyHour": 8,
    "DigestDailyMinute": 0
  }
}
```
