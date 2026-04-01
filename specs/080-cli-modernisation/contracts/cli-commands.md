# CLI Command Contracts: 080 — CLI Modernisation

## New Commands

### `sorcha events watch`
```
sorcha events watch [--register <id>] [--blueprints] [--all] [--role <consumer|admin|sysadmin>] [--since <timestamp>] [--output <json|table>]
```
Connects to SignalR hubs and streams events. `--output json` produces one JSON line per event. Ctrl+C to stop.

### `sorcha health`
```
sorcha health [--service <name>] [--output <table|json|csv|yaml>]
```
Aggregates health from all services. Calls `/api/health` on API Gateway plus per-service health endpoints.

### `sorcha invitation create|list|accept|revoke`
```
sorcha invitation create --register-id <id> --target-org-did <did> [--expires-in <hours>]
sorcha invitation list [--register-id <id>] [--status <pending|accepted|revoked>]
sorcha invitation accept --token <token>
sorcha invitation revoke --id <id>
```
Maps to Tenant Service `/api/organizations/{orgId}/register-invitations` endpoints.

### `sorcha audit list|export`
```
sorcha audit list [--since <date>] [--until <date>] [--action <action>] [--user <email>] [--page <n>] [--page-size <n>]
sorcha audit export --output <file> [--format <csv|json>] [--since <date>]
```
Maps to Tenant Service audit endpoints (new Refit client methods needed).

### `sorcha verify receipt|bundle|inclusion-proof`
```
sorcha verify receipt --register-id <id> --tx-id <id>
sorcha verify bundle --register-id <id> --tx-id <id>
sorcha verify inclusion-proof --register-id <id> --tx-id <id>
```
Maps to Register Service `/api/registers/{id}/verification/*` endpoints (Feature 079).

### `sorcha register sync-status|watch|export|export-transactions`
```
sorcha register sync-status --id <id>
sorcha register watch --id <id>
sorcha register export --id <id> --output <file>
sorcha register export-transactions --id <id> --output <file> [--format <csv|json>]
```

### `sorcha wallet create-batch`
```
sorcha wallet create-batch --count <n> [--algorithm <ED25519|P256|RSA4096>]
```

### `sorcha user bulk-import`
```
sorcha user bulk-import --file <csv-path>
```
CSV columns: email, firstName, lastName, roles

### `sorcha config view|set|validate|export`
```
sorcha config view
sorcha config set <key> <value>
sorcha config validate
sorcha config export --output <file>
```

### `sorcha completion`
```
sorcha completion [bash|zsh|powershell|fish]
```

### `sorcha platform settings|orgs|stats`
```
sorcha platform settings [--output <format>]
sorcha platform orgs [--status <active|suspended>] [--page <n>]
sorcha platform stats [--output <format>]
```

## Modified Commands (all existing)

All existing list commands gain: `--page <n>`, `--page-size <n>` options.
All existing commands gain: `--output yaml` option, `--machine-readable` flag.
All commands gain: usage examples in `--help` output.

## Global Options (modified)

| Option | Current | New |
|--------|---------|-----|
| `--output` | table, json, csv | table, json, csv, **yaml** |
| `--machine-readable` | N/A | **NEW** — wraps output in standard JSON envelope |
