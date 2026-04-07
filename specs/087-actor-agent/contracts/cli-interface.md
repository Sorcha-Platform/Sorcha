# CLI Interface Contract: Sorcha.Agent

**Date**: 2026-04-07

## Commands

### `run` — Start an autonomous actor

```
sorcha-agent run --config <path> --state <path> [--verbose] [--quiet]
```

| Option | Type | Required | Default | Description |
|--------|------|----------|---------|-------------|
| --config | string | Yes | — | Path to actor definition JSON file |
| --state | string | No | — | Path to state.json for placeholder resolution |
| --verbose | flag | No | false | Debug-level logging |
| --quiet | flag | No | false | Errors only |

**Behaviour**: Authenticates, connects SignalR + starts polling, enters main loop. Runs until SIGTERM/Ctrl+C or fatal error.

**Exit codes**:
| Code | Meaning |
|------|---------|
| 0 | Graceful shutdown (SIGTERM/Ctrl+C) |
| 1 | General error |
| 2 | Authentication error |
| 4 | Validation error (bad config) |
| 6 | Configuration error (missing file, unresolved vars) |
| 7 | Network error (cannot reach gateway) |

**Console output** (stdout):
```
[12:34:56] Actor "planning-officer" started
[12:34:56] SignalR connected to https://gateway/hubs/actions
[12:34:56] Polling enabled (60s interval)
[12:35:02] Action "ReviewApplication" discovered (id: abc-123)
[12:35:02] Rule matched: approve
[12:35:03] Action "ReviewApplication" submitted successfully
[12:40:56] Poll: 0 pending actions
...
[12:45:00] Shutting down (SIGTERM)
[12:45:01] Summary: 3 actions processed, 0 errors, uptime 10m 5s
```

### `validate` — Check actor configuration

```
sorcha-agent validate --config <path> --state <path> [--verbose]
```

| Option | Type | Required | Default | Description |
|--------|------|----------|---------|-------------|
| --config | string | Yes | — | Path to actor definition JSON file |
| --state | string | No | — | Path to state.json for placeholder resolution |
| --verbose | flag | No | false | Show detailed check results |

**Behaviour**: Runs checks sequentially, reports results, exits.

**Checks performed**:
1. JSON schema validation of actor file
2. `$env:` variable resolution
3. `{{placeholder}}` resolution against state file
4. Credential connectivity (login + org selection)
5. SignalR hub reachability

**Exit codes**: Same as `run`.

**Console output** (stdout):
```
Validating actor "planning-officer"...
  [PASS] JSON schema valid
  [PASS] Environment variables resolved (1 variable)
  [PASS] State placeholders resolved (3 placeholders)
  [PASS] Authentication successful (org: Highland Council)
  [PASS] SignalR hub reachable

All checks passed.
```

Or on failure:
```
Validating actor "planning-officer"...
  [PASS] JSON schema valid
  [FAIL] Environment variable $env:PO_PASSWORD is not set
  [SKIP] State placeholders (cannot validate without credentials)
  [SKIP] Authentication (cannot test without credentials)
  [SKIP] SignalR hub (cannot test without authentication)

1 check failed, 3 skipped.
```

## Internal Interfaces

### IDecisionEngine

```
DecideAsync(PendingAction action, CancellationToken ct) → ActionDecision
```

Implementations: `RulesDecisionEngine`, `AiDecisionEngine`

### IInboxListener

```
StartAsync(CancellationToken ct) → IAsyncEnumerable<PendingAction>
```

Implementations: `SignalRInboxListener`, `PollingInboxListener`, `CompositeInboxListener`

### IActionExecutor

```
ExecuteAsync(PendingAction action, ActionDecision decision, CancellationToken ct) → bool
```

Single implementation using `IValidatorServiceClient` + `IWalletServiceClient`.
