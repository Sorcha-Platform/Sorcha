# Data Model: 080 — CLI Modernisation and Feature Completion

## Key Entities

### OutputFormat (existing enum — extend)

Current values: Table, Json, Csv
Add: Yaml

### MachineReadableEnvelope (new)

| Field | Type | Purpose |
|-------|------|---------|
| Status | string | "success" or "error" |
| Command | string | Full command name (e.g., "register list") |
| Data | object? | Command output (array or object) |
| Errors | string[] | Error messages if any |
| Timestamp | string | ISO 8601 timestamp |
| ExitCode | int | Process exit code |

### EventStreamMessage (new)

| Field | Type | Purpose |
|-------|------|---------|
| EventType | string | "TransactionConfirmed", "DocketSealed", "RegisterStatusChanged", etc. |
| RegisterId | string? | Register the event relates to |
| Timestamp | string | ISO 8601 when event occurred |
| Data | object | Event-specific payload |

### BulkOperationResult (new)

| Field | Type | Purpose |
|-------|------|---------|
| TotalItems | int | Total items in batch |
| Succeeded | int | Successfully processed |
| Failed | int | Failed items |
| Errors | BulkItemError[] | Per-item error details |
| Duration | TimeSpan | Total operation time |

### BulkItemError (new)

| Field | Type | Purpose |
|-------|------|---------|
| Index | int | Item index (0-based) or CSV row number |
| Identifier | string | Item identifier (e.g., email, wallet address) |
| Error | string | Error message |

### HealthReport (new)

| Field | Type | Purpose |
|-------|------|---------|
| OverallStatus | string | "healthy", "degraded", "unhealthy" |
| Services | ServiceHealth[] | Per-service health details |
| CheckedAt | string | ISO 8601 timestamp |

### ServiceHealth (new)

| Field | Type | Purpose |
|-------|------|---------|
| Name | string | Service name |
| Status | string | "healthy", "unhealthy", "unreachable" |
| ResponseTimeMs | int | Health check response time |
| Error | string? | Error message if unhealthy |

## Command Tree (new/modified commands)

```
sorcha
├── help                          # NEW: Getting started guide
├── version                       # MODIFIED: Add ASCII banner
├── events                        # NEW command group
│   └── watch                     # NEW: Real-time event streaming
│       ├── --register <id>
│       ├── --blueprints
│       ├── --all
│       ├── --role <consumer|admin|sysadmin>
│       └── --since <timestamp>
├── health                        # NEW: System health check
│   └── --service <name>
├── invitation                    # NEW command group
│   ├── create
│   ├── list
│   ├── accept
│   └── revoke
├── audit                         # NEW command group
│   ├── list
│   └── export
├── verify                        # NEW command group
│   ├── receipt --tx-id <id>
│   ├── bundle --tx-id <id>
│   └── inclusion-proof --tx-id <id>
├── register
│   ├── sync-status --id <id>    # NEW
│   ├── watch --id <id>          # NEW: Real-time sync progress
│   ├── export --id <id>         # NEW
│   └── export-transactions      # NEW
├── blueprint
│   └── export --id <id>         # NEW
├── wallet
│   └── create-batch             # NEW
├── user
│   └── bulk-import              # NEW
├── platform                      # NEW command group
│   ├── settings
│   ├── orgs
│   └── stats
├── config
│   ├── view                     # NEW
│   ├── set                      # NEW
│   ├── validate                 # NEW
│   └── export                   # NEW
├── completion                    # NEW: Shell completion scripts
│   ├── bash
│   ├── zsh
│   ├── powershell
│   └── fish
└── (all existing commands)       # MODIFIED: output consistency, --help examples, pagination
```
