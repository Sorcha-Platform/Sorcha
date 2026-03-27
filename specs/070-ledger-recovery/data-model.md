# Data Model: 070-ledger-recovery

## New Entity: RecoveryState

Tracks the overall recovery progress at service startup.

| Field | Type | Description |
|-------|------|-------------|
| IsComplete | bool | True when initial recovery finishes for all reachable registers |
| StartedAt | DateTimeOffset | When recovery began |
| CompletedAt | DateTimeOffset? | When recovery finished (null if in progress) |
| RegisterStates | Dictionary\<string, RegisterRecoveryState\> | Per-register recovery status |

## New Entity: RegisterRecoveryState

Per-register recovery tracking.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register identifier |
| RegisterName | string | Human-readable name |
| Status | RegisterHealthStatus | Online, Offline, Degraded |
| Height | int | Transaction count from last successful query |
| LastCheckedAt | DateTimeOffset | When last health check ran |
| LastSuccessAt | DateTimeOffset? | When last successful query completed |
| ConsecutiveFailures | int | Number of consecutive failed checks (0 if healthy) |
| RecoveredBlueprintCount | int | Number of published blueprints recovered from this register |
| ErrorMessage | string? | Last error if offline |

## Enum: RegisterHealthStatus

| Value | Description |
|-------|-------------|
| Unknown | Not yet checked (initial state) |
| Online | Reachable, queries succeed |
| Offline | Unreachable, queries fail |
| Degraded | Reachable but slow or partially failing |

## Modified Entity: InMemoryPublishedBlueprintStore

No structural changes. Populated by the recovery service on startup instead of only by publish operations.

## Existing Entity: PublishedBlueprint (unchanged)

| Field | Type | Description |
|-------|------|-------------|
| BlueprintId | string | Blueprint identifier |
| Version | int | Published version number |
| Blueprint | Blueprint | Full blueprint definition |
| PublishedAt | DateTimeOffset | When published |
| RegisterId | string? | Which register it's published to |

## State Transitions

### Recovery Lifecycle

```
[Service Starting]
    │
    ├── Query Register Service for all registers
    │
    ├── For each register:
    │   ├── Query for blueprint-publish transactions
    │   ├── Success → RegisterHealthStatus.Online, populate published store
    │   └── Failure → RegisterHealthStatus.Offline, schedule retry
    │
    ├── All reachable registers processed
    │   └── RecoveryState.IsComplete = true
    │   └── Health check returns 200
    │
    └── Background timer starts (60s default)
        └── Re-runs recovery for offline registers + discovers new publications
```

### Register Health Transitions

```
Unknown ──(check succeeds)──> Online
Unknown ──(check fails)────> Offline
Online ──(check fails)─────> Offline (if consecutiveFailures > 0)
Offline ──(check succeeds)──> Online (recovers blueprints)
Online ──(slow response)───> Degraded
Degraded ──(normal)────────> Online
```
