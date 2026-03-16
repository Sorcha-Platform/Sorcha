# Data Model: Relay-Aware Peer Communication

**Phase 1 Output** | **Date**: 2026-03-16

## New Entities

### RelayMessages.cs — Request/Response POCOs

All POCOs are serialized as JSON into `PeerMessage.payload` bytes field. All include `CorrelationId` (GUID string) for request/response matching.

#### RegisterSyncRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CorrelationId | string | Yes | GUID for request/response correlation |
| RegisterId | string | Yes | Target register to sync |
| FromDocketVersion | long | No (default 0) | Start pulling from this version |
| MaxDockets | int | No (default 50) | Maximum dockets per response batch |

**Validation**: RegisterId must not be empty. MaxDockets must be 1-500.

#### RegisterSyncResponse

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CorrelationId | string | Yes | Matches the request CorrelationId |
| RegisterId | string | Yes | Register that was synced |
| Dockets | List\<DocketEntry\> | Yes | Batch of docket data |
| HasMore | bool | No (default false) | True if more dockets available beyond this batch |

#### DocketEntry

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Version | long | Yes | Docket version number |
| Data | byte[] | Yes | Serialized docket data |
| DocketHash | string | Yes | Hash of docket content |
| PreviousHash | string | Yes | Hash chain link to previous docket |
| TransactionIds | List\<string\> | Yes | 64-char hex SHA-256 transaction IDs in this docket |
| CreatedAt | long | Yes | Unix timestamp (milliseconds) |

#### TransactionDataRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CorrelationId | string | Yes | GUID for request/response correlation |
| RegisterId | string | Yes | Register containing the transactions |
| TransactionIds | List\<string\> | Yes | 64-char hex SHA-256 transaction IDs to retrieve |

**Validation**: TransactionIds must not be empty. Each ID must be 64-char hex.

#### TransactionDataResponse

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| CorrelationId | string | Yes | Matches the request CorrelationId |
| RegisterId | string | Yes | Register containing the transactions |
| Transactions | List\<TransactionEntry\> | Yes | Transaction data entries |

#### TransactionEntry

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| TransactionId | string | Yes | 64-char hex SHA-256 hash |
| Data | byte[] | Yes | Full transaction payload |
| Checksum | string | Yes | Integrity checksum |
| CreatedAt | long | Yes | Unix timestamp (milliseconds) |

## Modified Entities

### PeerServiceConfiguration — New Config Property

Add to `RegisterSyncConfiguration`:

| Field | Type | Default | Range | Description |
|-------|------|---------|-------|-------------|
| RelayPollIntervalSeconds | int | 60 | 10-300 | Interval for periodic relay sync poll |

### MessageType Proto Enum — New Values

| Value | Number | Description |
|-------|--------|-------------|
| REGISTER_SYNC_REQUEST | 8 | Request docket batch from peer |
| REGISTER_SYNC_RESPONSE | 9 | Response with docket batch |
| TRANSACTION_DATA_REQUEST | 10 | Request transaction data by IDs |
| TRANSACTION_DATA_RESPONSE | 11 | Response with transaction data |

## Internal State (Not Persisted)

### RelayCommunicationService — Correlation Dictionary

| Field | Type | Description |
|-------|------|-------------|
| _pendingCorrelations | ConcurrentDictionary\<string, TaskCompletionSource\<PeerMessage\>\> | Maps correlation GUID → pending response TCS |

Entries are added on `SendAndWaitAsync`, removed on completion or timeout. No persistence — in-flight only.

### RegisterSyncBackgroundService — Per-Register Semaphores

| Field | Type | Description |
|-------|------|-------------|
| _relaySyncSemaphores | ConcurrentDictionary\<string, SemaphoreSlim\> | Maps registerId → sync guard (initial count 1) |

Prevents concurrent relay sync operations on the same register. Shared between periodic poll and notification-triggered sync.

## Entity Relationships

```
PeerMessage (existing proto)
  ├── MessageType (extended with 4 new values)
  └── payload (bytes) ──JSON──▶ RegisterSyncRequest
                               RegisterSyncResponse
                                 └── List<DocketEntry>
                                       └── List<TransactionIds>
                               TransactionDataRequest
                                 └── List<TransactionIds>
                               TransactionDataResponse
                                 └── List<TransactionEntry>
```

## Mapping to Existing Cache

The relay sync POCOs map to existing `RegisterCache` types:

| Relay POCO | Cache Type | Mapping |
|------------|------------|---------|
| DocketEntry | CachedDocket | Version, Data, DocketHash, PreviousHash, TransactionIds, CreatedAt |
| TransactionEntry | CachedTransaction | TransactionId, Data, Checksum, CreatedAt |

The processing logic in `RegisterReplicationService` converts relay POCOs → cache types using the same logic as the streaming path.
