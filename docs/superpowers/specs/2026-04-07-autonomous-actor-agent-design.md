# Autonomous Actor Agent Framework

**Date:** 2026-04-07
**Status:** Draft
**Scope:** New `Sorcha.Agent` CLI project for autonomous walkthrough actor execution

---

## Problem

Existing walkthroughs (ConstructionPermit, SelfBuildHouse) run as single-threaded PowerShell scripts from one machine, logging in and out as each actor sequentially. This model cannot:

- Run actors on different machines for distributed testing
- Leave actors running and responding autonomously
- Scale to demos where participants act independently
- Reuse actor behaviour across walkthroughs

The TradeFinance walkthrough advanced this with a two-machine MCP/Claude Code pattern, but it's walkthrough-specific — not a reusable framework.

## Solution

A standalone `Sorcha.Agent` CLI project that runs a single autonomous actor as a long-lived process. The actor connects to a Sorcha instance, listens for pending actions via SignalR (with polling fallback), and responds using a pluggable decision engine — either deterministic JSON Logic rules or an AI agent with a persona prompt.

### Design Principles

- **One actor, one process** — simple, isolated, easy to distribute across machines
- **Pluggable decision engine** — same lifecycle, different brains (rules vs AI)
- **Maximum reuse** — `Sorcha.ServiceClients.Http` for auth/actions/SignalR, `Sorcha.Blueprint.Models` for schemas
- **Stateless** — actors react to inbox events without tracking history. State can be layered on later.
- **Portable deployment** — copy actor.json + state.json to any machine with the binary

---

## Project Structure

```
src/Apps/Sorcha.Agent/
├── Program.cs                     # System.CommandLine entry point
├── Commands/
│   └── RunCommand.cs              # "run" command — the main loop
├── Configuration/
│   ├── ActorDefinition.cs         # Deserialized actor JSON file
│   └── ActorDefinitionLoader.cs   # Load + validate actor file
├── Inbox/
│   ├── IInboxListener.cs          # Abstraction over SignalR + poll
│   ├── SignalRInboxListener.cs    # Real-time events
│   ├── PollingInboxListener.cs    # Fallback poll
│   └── CompositeInboxListener.cs  # Merges both, deduplicates
├── Decision/
│   ├── IDecisionEngine.cs         # Given an action, return decision
│   ├── RulesDecisionEngine.cs     # JSON Logic evaluation
│   └── AiDecisionEngine.cs        # Claude/MCP integration
├── Execution/
│   └── ActionExecutor.cs          # Submit action via ServiceClients
└── Sorcha.Agent.csproj
```

### Dependencies

| Package | Purpose |
|---------|---------|
| `Sorcha.ServiceClients.Http` | Auth, action execution, SignalR client |
| `Sorcha.Blueprint.Models` | Action schemas, payload handling |
| `System.CommandLine` (2.0.2) | CLI framework |
| `JsonLogic.Net` | Rules evaluation |
| `Microsoft.Extensions.Http.Polly` | Retry / circuit-breaker |
| `Serilog` or built-in `ILogger` | Structured logging |

### CLI Surface

```bash
sorcha-agent run --config ./actors/planning-officer.json --state ./state.json
sorcha-agent validate --config ./actors/planning-officer.json --state ./state.json
```

`validate` performs: JSON schema validation of the actor file, `$env:` variable resolution check, `{{placeholder}}` resolution against `--state` file (if provided), credential connectivity test (login + org selection), and SignalR hub reachability. Exits 0 on success, non-zero with diagnostics on failure.

---

## Actor Definition File Format

Each actor is defined by a JSON file that specifies identity, connection, behaviour, and resilience settings.

```json
{
  "actor": {
    "name": "planning-officer",
    "description": "Reviews planning applications and issues decisions"
  },
  "connection": {
    "gatewayUrl": "https://n1.sorcha.dev",
    "registerId": "{{registerId}}",
    "credentials": {
      "email": "po@highland-council.example",
      "password": "$env:PO_PASSWORD",
      "organizationId": "{{orgId}}"
    },
    "walletAddress": "{{walletAddress}}"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 60 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "ReviewApplication",
      "condition": { "==": [true, true] },
      "decision": "approve",
      "payload": {
        "decision": "approved",
        "reviewNotes": "Application meets all planning requirements.",
        "conditions": "Standard conditions apply."
      }
    },
    {
      "actionName": "ReviewApplication",
      "condition": { ">": [{ "var": "payload.estimatedCost" }, 500000] },
      "decision": "reject",
      "payload": {
        "decision": "rejected",
        "reviewNotes": "Cost exceeds automatic approval threshold."
      }
    }
  ],
  "ai": {
    "promptFile": "./prompts/planning-officer.md",
    "model": "claude-sonnet-4-6",
    "temperature": 0.3
  },
  "resilience": {
    "retryCount": 3,
    "retryDelaySeconds": 2,
    "circuitBreakerThreshold": 5,
    "circuitBreakerDurationSeconds": 30
  },
  "logging": {
    "level": "Information",
    "actionLog": "./logs/planning-officer-actions.jsonl"
  }
}
```

### Variable Resolution

- **`$env:VAR_NAME`** — resolved from environment variables at load time. Keeps secrets out of files.
- **`{{placeholder}}`** — resolved from a companion `state.json` (output of walkthrough setup.ps1). Allows the same actor file to work across environments.

### Rules Evaluation

- Rules are evaluated **top-to-bottom, first match wins**. More specific rules go higher; catch-all rules go last.
- `condition` uses JSON Logic syntax evaluated via `JsonLogic.Net`.
- `var` expressions can reference: `payload.*` (previous actor's submitted data), `action.name`, `action.index`, `actor.name`.
- No match → action is skipped (logged as warning, stays in inbox for re-evaluation on next poll).

### AI Engine

- Loads a markdown persona prompt from `promptFile`.
- Builds context: action name, expected schema, previous payload, participant role.
- Calls Claude API, parses structured response (decision + payload).
- Validates generated payload against the action's JSON schema before submitting.
- If validation fails, retries once with schema errors fed back to the model.

### Mode Selection

`mode` selects which engine runs — `rules` ignores the `ai` block and vice versa. Both blocks can be present so you can switch modes without editing the file.

---

## Inbox Discovery & Deduplication

```
                  ┌──────────────┐
   SignalR Hub ──>│  SignalR      │──┐
   (real-time)    │  Listener    │  │    ┌──────────────┐     ┌──────────────┐
                  └──────────────┘  ├──>│  Composite   │────>│  Decision    │
                  ┌──────────────┐  │    │  (dedup by   │     │  Engine      │
   Poll Timer ──>│  Polling      │──┘    │  actionId)   │     └──────────────┘
   (fallback)     │  Listener    │       └──────────────┘
                  └──────────────┘
```

### SignalR Listener

- Connects to Blueprint Service hub using the actor's JWT.
- Subscribes to action notifications for the actor's participant.
- Auto-reconnect with exponential backoff (built into SignalR client).

### Polling Listener

- Hits the inbox endpoint on a configurable timer (default 60s).
- Returns all pending actions for the participant.

### Composite Listener

- Maintains an in-memory `HashSet<string>` of processed action IDs.
- Deduplicates across both sources.
- Feeds unique actions to the decision engine **sequentially** (one-at-a-time to avoid race conditions on the same workflow instance).

### Reconnection

- SignalR disconnects: polling continues as normal — no gap in coverage.
- On SignalR reconnect: one immediate poll to catch anything missed during the reconnect window.

---

## Decision Engines

```csharp
public interface IDecisionEngine
{
    Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken ct);
}

public record ActionDecision(
    string Decision,                      // "approve", "reject", "skip"
    Dictionary<string, object> Payload,
    string Reasoning);                    // logged, not submitted
```

### Skip Behaviour

Both engines can return `skip`. The action stays in the inbox and is re-evaluated on the next poll cycle (but not on every SignalR event, to avoid tight loops).

---

## Action Execution & Resilience

### Execution Flow

1. Decision engine returns `ActionDecision`
2. Validate payload against the action's JSON schema — mismatch logs error and skips
3. Execute action via `Sorcha.ServiceClients.Http` (POST with JWT + X-Delegation-Token)
4. Log to JSONL audit trail: `{ timestamp, actionId, decision, success }`

### Polly Resilience Pipeline

| Policy | Configuration | Behaviour |
|--------|--------------|-----------|
| **Retry** | Count from config (default 3), exponential backoff | Retries on 5xx, timeouts, `HttpRequestException` |
| **Circuit breaker** | Threshold from config (default 5 failures), duration from config (default 30s) | Opens after consecutive failures, actions deferred until close |
| **Timeout** | 30s per request | Prevents hanging on unresponsive services |

Applied at the `HttpClient` level via `IHttpClientFactory`, benefiting both action execution and polling.

### Error Handling

| Error Type | Behaviour |
|------------|-----------|
| Transient (5xx, network) | Polly retries, then skips. Action stays in inbox for next cycle. |
| Auth failure (401) | Re-authenticate using stored credentials, retry once. |
| Validation failure (400) | Log error detail, skip permanently (added to rejected set). |
| Circuit open | Log warning, continue listening. Resume when circuit closes. |

---

## Graceful Shutdown

- `Ctrl+C` / SIGTERM triggers `CancellationToken`
- Completes any in-flight action execution before exiting
- Logs final summary: actions processed, errors, uptime

---

## Walkthrough Port: ConstructionPermit

The ConstructionPermit walkthrough (4 orgs, 5 participants) is the first port to validate the framework.

### What Changes

- **`setup.ps1`** — unchanged. Creates orgs, wallets, participants, registers, publishes blueprint, writes `state.json`.
- **`run.ps1`** — replaced by 5 `sorcha-agent` processes, one per actor.
- **New:** `walkthroughs/ConstructionPermit/actors/` directory with 5 actor definition files:
  - `contractor.json` — submits the application (rules: always approve with static payload)
  - `structural-engineer.json` — provides certification (rules: always approve)
  - `planning-officer.json` — reviews application (rules: approve/reject based on payload)
  - `building-inspector.json` — conducts inspection (rules: always approve)
  - `council-admin.json` — issues final permit (rules: always approve)
- **New:** `run-agents.ps1` — launcher script that starts all 5 processes, waits for workflow completion or timeout, then shuts all down.

### Success Criteria

- ConstructionPermit workflow completes end-to-end with 5 independent agent processes.
- Works on a single machine (localhost) and across machines (localhost + n1.sorcha.dev).
- No changes to `setup.ps1` or the shared `SorchaWalkthrough` module.

### Deployment Model

To run actors on remote machines: copy the actor JSON file(s) and `state.json`. The actor file contains the gateway URL; the state file contains the resolved IDs. That is the entire deployment contract.

---

## Out of Scope (Future)

| Feature | Rationale |
|---------|-----------|
| Stateful actor memory | Not needed to prove the model. Layer on when rules need decision history. |
| Actor definition UI | Natural follow-on once the file format is stable. |
| Multi-actor per process | One-actor-one-process is simpler and maps to container deployment. |
| Auto-provisioning | Setup.ps1 handles wallet/participant creation. Agent just consumes. |
| Binary packaging / distribution | `dotnet publish --self-contained` or bundling with CLI is a deployment concern, not a design concern. |
