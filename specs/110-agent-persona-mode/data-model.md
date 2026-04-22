# Phase 1 Data Model: Agent Persona Mode

All types live in `Sorcha.Agent.Persona` (new namespace) unless noted. Records use C# 14 `init`-only properties to match the existing `ActorDefinition` style.

## Top-level types

### `PersonaDefinition` (root record, loaded from persona JSON file)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | `string` | Yes | Human-readable persona label; surfaces in logs. |
| `Target` | `PersonaTarget` | Yes | Which blueprint + action + instance this persona submits against. |
| `Trigger` | `PersonaTrigger` | Yes | Discriminated union: `once` or `interval`. |
| `PayloadTemplate` | `JsonNode` | Yes | Raw JSON template; tokens resolved per fire. |

**Validation rules**:
- `Name` non-empty.
- Exactly one `Trigger` kind set.
- `PayloadTemplate` must parse as a JSON object.
- All `${...}` tokens in `PayloadTemplate` must be resolvable by `PayloadTokenResolver` at load time (FR-010).

---

### `PersonaTarget`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `BlueprintId` | `string` | Yes | ID of the blueprint whose starting action is being submitted. Supports `{{blueprints.<key>.id}}` placeholder via existing `VariableResolver`. |
| `InstanceId` | `string` | Yes | ID of the pre-created instance (created by `run-agents.ps1`). Supports `{{instances.<key>.id}}` placeholder. |
| `ActionName` | `string` | Yes, unless `ActionIndex` | Human name of the action within the blueprint. |
| `ActionIndex` | `int?` | Yes, unless `ActionName` | Blueprint action id (1-based in published blueprints — matches the action's `id` field). The value is sent verbatim as the Blueprint Service's action id; the name `ActionIndex` is historical. Either name or index; if both, index wins and a load-time warning is logged. |

**Validation rules**: exactly one of `ActionName`/`ActionIndex` required.

---

### `PersonaTrigger` (discriminated union)

Two subtypes identified by the `Kind` discriminator (`"once"` or `"interval"`).

#### `OnceTrigger`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Kind` | `"once"` | Yes | Discriminator. |
| `DelaySeconds` | `int` | No (default `0`) | Delay between agent start and the single fire. Useful when the persona wants to wait for dependent agents to connect SignalR. |

#### `IntervalTrigger`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Kind` | `"interval"` | Yes | Discriminator. |
| `EverySeconds` | `int` | Yes (or `EveryMinutes`) | Interval between fires in seconds. |
| `EveryMinutes` | `int?` | (alternative) | Convenience alias; converted to seconds at load time. Exactly one of `EverySeconds`/`EveryMinutes` required. |
| `StartDelaySeconds` | `int` | No (default `0`) | Delay before the first fire. |
| `MaxIterations` | `int?` | No | Stop after N successful fires. |
| `Until` | `DateTimeOffset?` | No | Stop at wall-clock time (ISO 8601, parsed by `DateTimeOffset.Parse`). |

**Validation rules**:
- `EverySeconds` or `EveryMinutes` > 0.
- If `Until` is in the past at load time, log `Warning` and persona exits immediately at startup.
- `MaxIterations`, if present, > 0.

---

## Internal runtime types

### `PersonaFireContext`

State passed to `PayloadTokenResolver` on each fire.

| Field | Type | Description |
|-------|------|-------------|
| `Iteration` | `int` | 1-based counter. |
| `Now` | `DateTimeOffset` | Wall-clock at fire time (from injected `TimeProvider`). |
| `RandomSource` | `IRandomSource` | Seam for test-determinism; wraps `System.Random`. |

---

### `PayloadTokenResolver` (service)

**Responsibility**: Given a template `JsonNode` and a `PersonaFireContext`, produce a concrete `JsonObject` for submission.

**Token grammar** (evaluated only inside string values in the template; string values that are exactly `${token(...)}` are replaced by the token's *typed* result — integer, decimal, array — not a stringified form):

| Token | Example | Result type | Notes |
|-------|---------|-------------|-------|
| `${now}` | `"${now}"` | string (ISO 8601) | Current wall-clock, UTC, `O` format. |
| `${uuid}` | `"${uuid}"` | string | Fresh `Guid.NewGuid().ToString()`. |
| `${counter}` | `"${counter}"` | integer | Current `Iteration` value. |
| `${random.int(MIN, MAX)}` | `"${random.int(1, 100)}"` | integer | Inclusive range. |
| `${random.decimal(MIN, MAX, PRECISION)}` | `"${random.decimal(0, 9999, 2)}"` | number (JSON) | Two-decimal-place price. |
| `${random.choice([…])}` | `"${random.choice([\"EUR\",\"GBP\",\"USD\"])}"` | matches element type | Literal JSON array of strings or numbers. |

Tokens that are **embedded inside a larger string** (e.g. `"PO-${counter}"`) produce string interpolation of the token's `ToString()`. Tokens that are **the entire string value** preserve the native JSON type (so `"${counter}"` becomes a JSON number, not a string).

### `IPersonaLoop`

```csharp
public interface IPersonaLoop
{
    Task RunAsync(CancellationToken cancellationToken);
}
```

Implementations: `OnceTriggerLoop`, `IntervalTriggerLoop`.

### `IPersonaSubmitter`

```csharp
public interface IPersonaSubmitter
{
    Task<PersonaSubmissionResult> SubmitAsync(
        PersonaDefinition persona,
        JsonObject resolvedPayload,
        CancellationToken cancellationToken);
}
```

`PersonaSubmissionResult` records `Outcome` (`Submitted`/`TransientFailure`/`HardFailure`) + `DurationMs` + optional error message. The submitter wraps a call to the same `POST /api/instances/{instanceId}/actions/{actionIndex}/execute` endpoint the existing `ActionExecutor` uses.

### `PersonaHost`

Constructor-injected with: `PersonaDefinition`, `IPersonaSubmitter`, `IPayloadTokenResolver`, `ILogger<PersonaHost>`, `TimeProvider`, `IRandomSource`, `AuditLogger`. Selects `OnceTriggerLoop` or `IntervalTriggerLoop` based on `Trigger.Kind`. Exposes `Task RunAsync(CancellationToken)` for launch from `RunCommand`.

---

## Relationships

```text
ActorDefinition
└── PersonaFile (string, optional) ──► PersonaDefinition
                                       ├── PersonaTarget ──► (existing Blueprint, Action, Instance entities)
                                       ├── PersonaTrigger ──► OnceTrigger | IntervalTrigger
                                       └── PayloadTemplate ──► resolved at fire time via PayloadTokenResolver
```

No persona type owns any existing entity; persona only *references* blueprints, actions, and instances that are created and lifecycle-managed elsewhere.

## State transitions

`IPersonaLoop` states:

```text
NotStarted ──RunAsync()──► Starting ──(delay)──► Firing ──► Submitted ──┐
                                                     │                   │
                                                     └─ Failed ──────────┤
                                                                          │
                              ┌───────────────────────────────────────────┤
                              │                                           │
                              ▼                                           ▼
                           Stopped (trigger complete, maxIterations,
                                     until reached, or cancelled)
```

`OnceTriggerLoop` has exactly one `Firing` transition. `IntervalTriggerLoop` loops between `Firing` and `Submitted`/`Failed` until a stop condition fires.

## Schema-to-code mapping

The persona JSON file's shape is defined formally in [contracts/persona-schema.json](./contracts/persona-schema.json) and mirrored by these record types. The schema is the source of truth for load-time validation; records are the runtime representation.
