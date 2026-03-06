# Data Model: Operations & Monitoring Admin (Feature 051)

**Branch**: `051-operations-monitoring-admin` | **Date**: 2026-03-06

## Existing Entities (No Changes Required)

### DashboardStatistics (API Gateway)
Already implemented in `DashboardStatisticsService`.

| Field | Type | Notes |
|-------|------|-------|
| TotalBlueprints | int | From Blueprint Service |
| TotalBlueprintInstances | int | From Blueprint Service |
| ActiveBlueprintInstances | int | From Blueprint Service |
| TotalWallets | int | From Wallet Service |
| TotalRegisters | int | From Register Service |
| TotalTransactions | int | From Register Service |
| TotalTenants | int | From Tenant Service |
| ConnectedPeers | int | From Peer Service |
| Timestamp | DateTimeOffset | When stats were fetched |

### ServiceAlert (API Gateway → UI)
Already implemented in `AlertModels.cs`.

| Field | Type | Notes |
|-------|------|-------|
| Id | string | Unique alert identifier |
| Severity | AlertSeverity | Info, Warning, Error, Critical |
| Source | string | Service that generated alert |
| Message | string | Human-readable description |
| MetricName | string | Metric that triggered alert |
| CurrentValue | double | Current metric value |
| Threshold | double | Threshold that was exceeded |
| Timestamp | DateTimeOffset | When alert was generated |

### WalletAccessDto (Wallet Service)
Already implemented in `WalletAccessDto.cs`.

| Field | Type | Notes |
|-------|------|-------|
| Id | string | Grant identifier |
| Subject | string | Grantee identifier |
| AccessRight | AccessRight | Owner, ReadWrite, ReadOnly |
| GrantedBy | string | Grantor wallet address |
| Reason | string? | Optional reason |
| GrantedAt | DateTimeOffset | Grant timestamp |
| ExpiresAt | DateTimeOffset? | Optional expiry |
| IsActive | bool | Currently active |

### AccessRight (Wallet Core enum)
Already implemented in `Enums.cs`.

| Value | Spec Mapping | Description |
|-------|-------------|-------------|
| Owner | admin | Full control including delegation |
| ReadWrite | sign | Can view and sign transactions |
| ReadOnly | read | View only |

### SchemaProviderStatus (Blueprint Service)
Already implemented in `SchemaProviderStatus.cs`.

| Field | Type | Notes |
|-------|------|-------|
| ProviderName | string | Provider identifier |
| IsEnabled | bool | Active flag |
| BaseUri | string | Provider URL |
| ProviderType | string | Provider classification |
| RateLimitPerSecond | int | Rate limit |
| RefreshIntervalHours | int | Auto-refresh period |
| LastSuccessfulFetch | DateTimeOffset? | Last success |
| LastError | string? | Last error message |
| LastErrorAt | DateTimeOffset? | Last error time |
| SchemaCount | int | Number of schemas |
| HealthStatus | HealthStatus | Healthy, Degraded, Unavailable, Unknown |
| BackoffUntil | DateTimeOffset? | Backoff deadline |
| ConsecutiveFailures | int | Failure count |

### PushSubscription (Tenant Service)
Already implemented in Tenant DB context.

| Field | Type | Notes |
|-------|------|-------|
| Endpoint | string | Web Push endpoint URL |
| P256dh | string | Public key |
| Auth | string | Auth secret |
| UserId | string | Owning user |

## New Entities

### DashboardStatsViewModel (UI — extend existing)
Extend the existing `DashboardStatsViewModel` to support auto-refresh.

| New Field | Type | Notes |
|-----------|------|-------|
| RefreshIntervalSeconds | int | Default 30, configurable |
| IsRefreshing | bool | Loading state for refresh |

### AlertDismissal (UI — new, client-side)
Per-user alert dismissal tracking. Can be stored in browser localStorage since dismissals are per-user and don't need server persistence.

| Field | Type | Notes |
|-------|------|-------|
| AlertId | string | Dismissed alert ID |
| DismissedAt | DateTimeOffset | When dismissed |
| UserId | string | User who dismissed |

### WalletAccessViewModel (UI — new)
View model for the Access tab on WalletDetail page.

| Field | Type | Notes |
|-------|------|-------|
| Grants | List&lt;WalletAccessDto&gt; | Active grants |
| IsLoading | bool | Loading state |
| SelectedRight | AccessRight | For grant form |
| NewSubject | string | Subject identifier for grant |

### EventAdminViewModel (UI — new)
View model for the Events admin page.

| Field | Type | Notes |
|-------|------|-------|
| Events | List&lt;ActivityEventDto&gt; | Paginated events |
| TotalCount | int | Total matching events |
| Page | int | Current page |
| PageSize | int | Items per page |
| SeverityFilter | string? | Active severity filter |
| SinceFilter | DateTimeOffset? | Show events from this date forward (maps to API `since` param) |

### EncryptionOperationViewModel (UI — new)
View model for encryption progress display.

| Field | Type | Notes |
|-------|------|-------|
| OperationId | string | Operation identifier |
| Stage | string | Current stage (queued, processing, complete) |
| PercentComplete | int | 0-100 progress |
| RecipientCount | int | Total recipients |
| IsComplete | bool | Computed: Stage == "complete" |

### CredentialStatus (UI — new constants class)
Replace inline magic strings with shared constants.

| Constant | Value | Notes |
|----------|-------|-------|
| Active | "Active" | Credential is valid |
| Suspended | "Suspended" | Temporarily disabled |
| Revoked | "Revoked" | Permanently invalidated |
| Expired | "Expired" | Past validity period |
| Consumed | "Consumed" | Replaced via refresh |

## State Transitions

### Credential Lifecycle (existing, documented for reference)
```
Active → Suspended (via suspend)
Suspended → Active (via reinstate)
Active → Revoked (via revoke, irreversible)
Active → Expired (automatic, time-based)
Active → Consumed (via refresh, old credential)
```

### Alert Lifecycle
```
Generated (by AlertAggregationService threshold check)
  → Active (visible to all admins)
  → Dismissed (per-user, hidden for that admin only)
  → Resolved (metric returns below threshold, removed globally)
```

## Relationships

```
Wallet ──1:N──▶ WalletAccessDto (grants)
User ──1:N──▶ AlertDismissal (per-user tracking)
SystemAlert ──1:N──▶ AlertDismissal (many users can dismiss)
User ──0:1──▶ PushSubscription (one active subscription per user)
BlueprintAction ──0:1──▶ EncryptionOperation (when encryption is triggered)
```
