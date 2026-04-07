# Data Model: Autonomous Actor Agent Framework

**Date**: 2026-04-07
**Feature**: 087-actor-agent

## Entities

### ActorDefinition

The root configuration object deserialized from the actor JSON file.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| actor | ActorIdentity | Yes | Name and description |
| connection | ConnectionConfig | Yes | Gateway URL, credentials, wallet |
| inbox | InboxConfig | Yes | SignalR and polling settings |
| mode | string | Yes | "rules" or "ai" |
| rules | ActorRule[] | No | Rules for "rules" mode |
| ai | AiConfig | No | Settings for "ai" mode |
| resilience | ResilienceConfig | No | Retry and circuit breaker settings |
| logging | LoggingConfig | No | Log level and audit file path |

### ActorIdentity

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | string | Yes | Actor identifier (e.g., "planning-officer") |
| description | string | No | Human-readable description |

### ConnectionConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| gatewayUrl | string | Yes | Sorcha gateway base URL |
| registerId | string | Yes | Target register (supports `{{placeholder}}`) |
| credentials | CredentialsConfig | Yes | Login credentials |
| walletAddress | string | Yes | Actor's wallet address (supports `{{placeholder}}`) |

### CredentialsConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| email | string | Yes | Login email |
| password | string | Yes | Login password (supports `$env:VAR`) |
| organizationId | string | Yes | Org ID (supports `{{placeholder}}`) |

### InboxConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| signalR | SignalRConfig | No | SignalR listener settings |
| polling | PollingConfig | No | Poll fallback settings |

### SignalRConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| enabled | bool | Yes | Whether SignalR is active |

### PollingConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| enabled | bool | Yes | Whether polling is active |
| intervalSeconds | int | No | Poll interval (default: 60) |

### ActorRule

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| actionName | string | Yes | Action name to match |
| condition | JsonNode | No | JSON Logic expression (default: always true) |
| decision | string | Yes | "approve", "reject", or "skip" |
| payload | JsonObject | No | Payload template to submit |

### AiConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| promptFile | string | Yes | Path to persona markdown file |
| model | string | No | Claude model ID (default: "claude-sonnet-4-6") |
| temperature | double | No | Generation temperature (default: 0.3) |

### ResilienceConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| retryCount | int | No | Max retries (default: 3) |
| retryDelaySeconds | int | No | Base delay for backoff (default: 2) |
| circuitBreakerThreshold | int | No | Failures before open (default: 5) |
| circuitBreakerDurationSeconds | int | No | Open duration (default: 30) |

### LoggingConfig

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| level | string | No | Log level (default: "Information") |
| actionLog | string | No | JSONL audit file path |

## Runtime Models

### PendingAction

Represents an action discovered in the actor's inbox.

| Field | Type | Description |
|-------|------|-------------|
| ActionId | string | Unique action identifier |
| ActionName | string | Action name from blueprint |
| ActionIndex | uint | Action index in blueprint |
| BlueprintId | string | Blueprint identifier |
| InstanceId | string | Workflow instance identifier |
| RegisterId | string | Register identifier |
| TransactionId | string | Transaction that triggered this action |
| PreviousPayload | JsonElement | Payload from the previous action |
| Schema | JsonElement | Expected payload JSON schema |
| SenderAddress | string | Wallet address of previous actor |

### ActionDecision

Output of a decision engine.

| Field | Type | Description |
|-------|------|-------------|
| Decision | string | "approve", "reject", or "skip" |
| Payload | Dictionary<string, object> | Payload to submit (null for skip) |
| Reasoning | string | Logged but not submitted |

### ActionAuditEntry

Written to the JSONL audit log.

| Field | Type | Description |
|-------|------|-------------|
| Timestamp | DateTimeOffset | When the decision was made |
| ActionId | string | Action that was evaluated |
| ActionName | string | Action name |
| Decision | string | approve/reject/skip |
| Success | bool | Whether submission succeeded |
| Error | string | Error message if failed |
| DurationMs | long | Time from discovery to completion |

## State Transitions

The actor itself is stateless. Actions flow through a simple pipeline:

```
Discovered → Deduplicated → Evaluated → Submitted → Logged
                                ↓
                             Skipped → Re-evaluated on next poll
```

## Relationships

```
ActorDefinition ──1:N── ActorRule
ActorDefinition ──1:1── ConnectionConfig
ActorDefinition ──1:1── InboxConfig
ActorDefinition ──0:1── AiConfig
ActorDefinition ──0:1── ResilienceConfig
ActorDefinition ──0:1── LoggingConfig

InboundActionEvent ──maps to── PendingAction
PendingAction ──evaluated by── IDecisionEngine
IDecisionEngine ──returns── ActionDecision
ActionDecision ──logged as── ActionAuditEntry
```
