# Contracts: Resilient System Register Bootstrap

**Feature**: 100-resilient-bootstrap
**Date**: 2026-04-11

## Overview

This feature has **no new API endpoints**. All changes are internal to the `SystemRegisterBootstrapper` background service and the `SystemRegisterOptions` configuration model.

## Configuration Contract

The only external-facing contract is the configuration schema, consumed via `appsettings.json` or environment variables.

### SystemRegister Configuration Section

```json
{
  "SystemRegister": {
    "BootstrapMode": "Auto | SyncOnly | GenesisFile",
    "GenesisFile": "string | null",
    "FastRetryIntervalSeconds": "integer (default: 5)",
    "FastRetryDurationSeconds": "integer (default: 120)",
    "BackoffIntervalSeconds": "integer (default: 300)"
  }
}
```

### Validation Rules

| Property | Rule |
|----------|------|
| `BootstrapMode` | Must be one of: `Auto`, `SyncOnly`, `GenesisFile`. Case-insensitive. Invalid values cause startup failure. |
| `GenesisFile` | When `BootstrapMode: GenesisFile` and this is non-null, file must exist at the path. When null with GenesisFile mode, embedded resource is used. |
| `FastRetryIntervalSeconds` | Must be > 0. Default: 5. |
| `FastRetryDurationSeconds` | Must be > 0. Default: 120. |
| `BackoffIntervalSeconds` | Must be > 0. Default: 300. |

### Environment Variable Overrides

```bash
# Docker / Kubernetes
SystemRegister__BootstrapMode=SyncOnly
SystemRegister__GenesisFile=/etc/sorcha/system-register-genesis.json
SystemRegister__FastRetryIntervalSeconds=10
SystemRegister__BackoffIntervalSeconds=600
```

## Internal Contracts (No External Surface)

### SystemRegisterBootstrapper

- **Type**: `BackgroundService` (unchanged)
- **Registration**: `AddHostedService<SystemRegisterBootstrapper>()` (unchanged)
- **Behaviour change**: Reads `BootstrapMode` from options and branches accordingly

### Log Event Contracts

Structured log fields emitted during bootstrap (for log query / alerting):

| Event | Level | Fields |
|-------|-------|--------|
| Bootstrap started | Information | `BootstrapMode`, `FastRetryInterval`, `BackoffInterval` |
| Retry attempt (fast) | Information | `Attempt`, `ElapsedSeconds`, `NextRetrySeconds`, `Phase: FastRetry` |
| Phase transition | Information | `Phase: BackoffPolling`, `BackoffIntervalSeconds` |
| Retry attempt (backoff) | Debug | `Attempt`, `ElapsedMinutes`, `NextRetrySeconds`, `Phase: BackoffPolling` |
| Register found | Information | `Source: PeerSync | GenesisIngestion`, `Height`, `Status` |
| Genesis ingestion (Auto) | Warning | `Mode: Auto`, message: "Creating new local network from embedded genesis" |
| Bootstrap complete | Information | `DurationMs`, `Mode`, `Source` |
| Bootstrap stopped | Critical | `Mode`, `Reason` (GenesisFile mode failures only) |
