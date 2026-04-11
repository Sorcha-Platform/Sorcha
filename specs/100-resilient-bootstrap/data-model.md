# Data Model: Resilient System Register Bootstrap

**Feature**: 100-resilient-bootstrap
**Date**: 2026-04-11

## Entities

### BootstrapMode (Enum)

Controls the system register bootstrap strategy.

| Value | Description |
|-------|-------------|
| `Auto` | Default. Brief peer sync window (3 retries, 14s), then fall back to genesis file. For local dev. |
| `SyncOnly` | Wait for peers indefinitely. Never ingest genesis file. For production nodes joining existing networks. |
| `GenesisFile` | Ingest genesis file immediately. No peer sync. For first node creating a new network. |

### SystemRegisterOptions (Extended Configuration)

Extends the existing `SystemRegisterOptions` class with bootstrap mode and retry timing.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GenesisFile` | `string?` | `null` | Path to genesis JSON file. Null = use embedded resource. *(existing)* |
| `BootstrapMode` | `BootstrapMode` | `Auto` | Bootstrap strategy selection. |
| `FastRetryIntervalSeconds` | `int` | `5` | Interval between retries during fast-retry phase. |
| `FastRetryDurationSeconds` | `int` | `120` | Total duration of fast-retry phase before switching to backoff. |
| `BackoffIntervalSeconds` | `int` | `300` | Interval between retries during backoff-polling phase. |

### Bootstrap Phase (Internal State)

Logical state tracked within the bootstrapper's retry loop. Not persisted.

| State | Entry Condition | Behaviour |
|-------|-----------------|-----------|
| `FastRetry` | Bootstrap starts (SyncOnly mode) | Poll every `FastRetryIntervalSeconds`. Log each attempt at `Information`. |
| `BackoffPolling` | Elapsed time exceeds `FastRetryDurationSeconds` | Poll every `BackoffIntervalSeconds`. Log transition at `Information`, subsequent at `Debug`. |

## State Transitions

```
[Service Start]
    │
    ▼
┌─────────────────┐
│ Read BootstrapMode │
└─────────┬───────┘
          │
    ┌─────┼──────────┐
    ▼     ▼          ▼
 [Auto] [SyncOnly] [GenesisFile]
    │     │          │
    │     │          ▼
    │     │    ┌──────────────┐
    │     │    │ Ingest Genesis│──► [Seed Blueprints] ──► DONE
    │     │    └──────────────┘
    │     │
    │     ▼
    │  ┌──────────┐  register found
    │  │ FastRetry │──────────────► [Seed Blueprints] ──► DONE
    │  └────┬─────┘
    │       │ 2 min elapsed
    │       ▼
    │  ┌──────────────┐  register found
    │  │ BackoffPolling│──────────────► [Seed Blueprints] ──► DONE
    │  └──────────────┘
    │       │ (indefinite)
    │
    ▼
 ┌──────────────┐
 │ Current flow │  register found
 │ (3 retries)  │──────────────► [Seed Blueprints] ──► DONE
 └──────┬───────┘
        │ 14s elapsed
        ▼
 ┌──────────────┐
 │ Ingest Genesis│──► [Seed Blueprints] ──► DONE
 └──────────────┘
```

## Configuration Binding

JSON path: `SystemRegister` section in `appsettings.json`

```json
{
  "SystemRegister": {
    "BootstrapMode": "Auto",
    "GenesisFile": null,
    "FastRetryIntervalSeconds": 5,
    "FastRetryDurationSeconds": 120,
    "BackoffIntervalSeconds": 300
  }
}
```

Environment variable overrides follow .NET convention:
- `SystemRegister__BootstrapMode=SyncOnly`
- `SystemRegister__FastRetryIntervalSeconds=10`
