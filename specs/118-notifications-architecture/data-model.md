# Phase 1 Data Model — Notifications & Realtime Architecture

**Date**: 2026-05-05
**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Research**: [research.md](research.md)

This document captures every persisted entity, in-memory record, and Redis key the feature introduces. Wire shapes for hub events and HTTP endpoints live in `contracts/`.

---

## Persisted entities (Postgres — Tenant DB)

### `InboxEntry`

Durable user-facing notification. Lives in the Tenant Service database. Source of truth.

```csharp
public sealed class InboxEntry
{
    public Guid Id { get; set; }                            // PK; generated server-side
    public Guid PlatformUserId { get; set; }                // FK → PlatformUser
    public InboxCategory Category { get; set; }
    public InboxSeverity Severity { get; set; }
    public string CorrelationKey { get; set; } = "";        // ≤ 256 chars; format per R-006
    public string DetailHref { get; set; } = "";            // ≤ 1024 chars; absolute path on the API gateway
    public Guid SourceEventId { get; set; }                 // writer-supplied; idempotency key
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public string Title { get; set; } = "";                 // ≤ 200 chars; rendered in inbox card header
    public string? Summary { get; set; }                    // ≤ 1000 chars; rendered as card body if present
    public string? IconKey { get; set; }                    // optional; e.g. "credential.received"
    public ChannelHints ChannelHints { get; set; }          // bitmask flags; default applied per category
    public Guid? WriterServiceId { get; set; }              // optional audit field — which service emitted
}

public enum InboxCategory
{
    Action = 0,
    Credential = 1,
    Membership = 2,
    Security = 3,
    System = 4,
    Workflow = 5,
    Custom = 99
}

public enum InboxSeverity
{
    Info = 0,
    Warning = 1,
    ActionRequired = 2,
    Critical = 3
}

[Flags]
public enum ChannelHints
{
    None = 0,
    Inbox = 1,
    Push = 2,
    Email = 4,
    Digest = 8
}
```

**Validation rules**:
- `PlatformUserId` MUST reference an existing `PlatformUser`.
- `CorrelationKey` MUST be non-empty and ≤ 256 chars.
- `DetailHref` MUST start with `/api/` (relative path on the gateway). Validated server-side.
- `Title` MUST be non-empty and ≤ 200 chars.
- `Summary` (if present) MUST be ≤ 1000 chars.
- `(PlatformUserId, SourceEventId)` MUST be unique — duplicate POSTs to the internal inbox endpoint are no-ops.
- `OccurredAt` MUST be ≤ `now + 5s` (clock-skew tolerance) at write time.

**Transitions**:

```
[New]
  │
  ▼
[Unread]   ──Read──▶  [Read]   ──Dismiss──▶ [Dismissed]
  │                                            ▲
  └────────────────── Dismiss ─────────────────┘
```

`ReadAt` is set on first read API call (idempotent thereafter). `DismissedAt` is set on first dismiss API call (idempotent). Dismiss can happen from either Unread or Read state. There is no "Unread an entry" transition; mark-all-read is the only bulk operation.

**Indexes**:
- PK `Id`
- Unique `(PlatformUserId, SourceEventId)` — idempotency
- Composite `(PlatformUserId, OccurredAt DESC) WHERE DismissedAt IS NULL` — primary list query
- Composite `(PlatformUserId, ReadAt) WHERE ReadAt IS NULL AND DismissedAt IS NULL` — unread-only filter
- Composite `(PlatformUserId, CorrelationKey, OccurredAt)` — sibling-grouping lookup
- Index `(PlatformUserId, Category, OccurredAt DESC)` — category filter

**Estimated size**: ~400 bytes/row average. At 10⁵ entries/user the table sits at ~40 MB/user, well within Postgres comfort.

**Retention**: Deferred to follow-up phase (out of scope per spec). For v1, no automatic GC; entries live indefinitely.

---

## Hub-event records (in-memory, on the wire)

### `HubSignal` (envelope concept)

Conceptual envelope. The wire shape is the SignalR method-call argument list per R-005; this record is the contract every hub method conforms to.

```csharp
public sealed record HubSignal(
    string EventType,
    IReadOnlyList<string> Ids,
    DateTimeOffset OccurredAt,
    string TraceId);
```

Hub method signatures take individual parameters matching this shape. Example for `BlueprintHub`:

```csharp
public interface IBlueprintHubClient
{
    /// <summary>
    /// A new action is available for the recipient wallet.
    /// </summary>
    /// <see cref="ActionsEndpoints.GetAction" path="/api/instances/{instanceId}/actions/{actionId}"/>
    Task ActionAvailable(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId);
}
```

**Validation**: At code-review time. Every parameter must be one of:
- `string` (representing an opaque ID)
- `Guid` or `Guid?`
- `int` or `long` (counts, heights — explicit numerics)
- `DateTimeOffset` (timestamp)
- A trace-id string

No `string description`, no `decimal balance`, no `bool isAdmin`, no nested DTOs. ChatHub is exempt and explicitly marked.

---

## Redis keys

### Backplane (managed by Microsoft.AspNetCore.SignalR.StackExchangeRedis)

```
sorcha:signalr:tenant:{...}      # internal SignalR backplane keys (managed by package)
sorcha:signalr:blueprint:{...}
sorcha:signalr:wallet:{...}
sorcha:signalr:register:{...}
```

Per-service prefix per FR-009. Internal layout is an implementation detail of the backplane package; we configure the prefix and do not read these keys directly.

### Unread-count index

```
sorcha:tenant:inbox:unread:{platformUserId:N}    # ZSET — score=epoch ms, member=entry GUID string
```

Operations:
- Write: `ZADD sorcha:tenant:inbox:unread:{userId} {epochMs} {entryGuid}` on inbox entry creation.
- Read count: `ZCARD sorcha:tenant:inbox:unread:{userId}`.
- Read top-N: `ZREVRANGE sorcha:tenant:inbox:unread:{userId} 0 {n-1}` for "newest unread first."
- Mark read: `ZREM sorcha:tenant:inbox:unread:{userId} {entryGuid}` on the read endpoint.
- Mark all read: `DEL sorcha:tenant:inbox:unread:{userId}` on the mark-all-read endpoint.

**Atomicity**: Single-key operations are atomic by Redis semantics. Cross-key operations (e.g., decrement-on-dismiss while updating Postgres) use the existing `IAtomicDistributedCache` GETDEL pattern from Feature 113 where applicable.

**Failure mode**: Index unavailable → fallback to Postgres `SELECT COUNT(*) WHERE PlatformUserId = ? AND ReadAt IS NULL AND DismissedAt IS NULL`. Logged as `Degraded`.

### Inbox-bridge dedup (optional, for Tenant `InboxBridgeService`)

```
sorcha:tenant:inbox:dedup:{sourceEventIdN}       # STRING — value=entryGuid; TTL=10 minutes
```

Used by the optional Redis-driven inbox bridge (R-004 alternative path) when an emitter prefers fire-and-forget Redis over HTTP. Not on the v1 primary path.

---

## Hub group conventions (formalised)

### `TenantHubGroups`

```csharp
public static class TenantHubGroups
{
    public static string User(Guid platformUserId) => $"user:{platformUserId:N}";
    public static string Org(Guid orgId) => $"org:{orgId:N}";
    public const string SystemAll = "system:all";
}
```

### `BlueprintHubGroups`

```csharp
public static class BlueprintHubGroups
{
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";
    public static string Instance(Guid instanceId) => $"instance:{instanceId:N}";
    public static string Org(Guid orgId) => $"org:{orgId:N}";
}
```

### `WalletHubGroups`

```csharp
public static class WalletHubGroups
{
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";
    public static string CitizenWallet(Guid platformUserId) => $"wallet:platform-user:{platformUserId:N}";
}
```

### `RegisterHubGroups`

```csharp
public static class RegisterHubGroups
{
    public static string Register(Guid registerId) => $"register:{registerId:N}";
}
```

**Naming rule**: Group strings are constructed only via these builders. Service code MUST NOT use string interpolation matching the patterns `"wallet:`, `"register:`, `"user:`, `"org:`, `"instance:`, `"system:` outside `*HubGroups.cs` and test files. Enforced by `scripts/check-no-inline-group-strings.ps1` in CI.

---

## OpenTelemetry instruments

Meter name `Sorcha.SignalR`. Created in `SignalRMetrics.cs` and used by every hub via the `AddSorchaHub` extension.

| Instrument | Type | Tags | Description |
|---|---|---|---|
| `sorcha_signalr_connections_total` | Counter | `hub`, `state` (connected/disconnected) | Connection lifecycle |
| `sorcha_signalr_messages_sent_total` | Counter | `hub`, `event_type` | Outbound event count |
| `sorcha_signalr_backplane_state` | Gauge | `service` | Backplane health: 0=down, 1=degraded, 2=up |
| `sorcha_signalr_reconnects_total` | Counter | `hub`, `reason` | Client reconnect attempts |
| `sorcha_signalr_events_hub_subscribers` | Gauge | (no tags) | EventsHub subscriber count — used to gate decommission per FR-038. Removed when EventsHub is deleted. |

Plus existing instruments (storage registration log) extended with the new `IInboxStore` registration.

---

## State diagram — peer-replicated transaction inbox flow

The walkthrough from spec US3 / design D6, expressed as state.

```
External event (peer replicates docket)
        │
        ├──▶ Register Service: project events, fire RegisterHub signals on register:{id}
        │       (no inbox writes — operator-only events)
        │
        ├──▶ Wallet Service: InboundTransactionRouter receives tx
        │       │
        │       ├──▶ WalletHub: TransactionReceived signal on wallet:{addr}
        │       │       (no inbox write — tick state, not notification-worthy)
        │       │
        │       ├──IF tx carries credential──▶
        │       │       ├──▶ WalletHub: CredentialReceived signal on wallet:{addr}
        │       │       └──▶ POST /api/internal/inbox
        │       │              { Category=Credential,
        │       │                CorrelationKey=tx:{addr}:{txId},
        │       │                SourceEventId=<credentialEventId> }
        │       │
        │       └──IF tx carries action──▶
        │               (Blueprint Service handles action creation; sees the same tx via routing)
        │
        └──▶ Blueprint Service (action created on the local instance)
                ├──▶ BlueprintHub: ActionAvailable signal on wallet:{addr}
                └──▶ POST /api/internal/inbox
                       { Category=Action,
                         CorrelationKey=tx:{addr}:{txId},
                         SourceEventId=<actionEventId> }

Tenant Service receives both inbox POSTs
        │
        ├──▶ Validates idempotency via (PlatformUserId, SourceEventId) unique index
        ├──▶ Persists InboxEntry rows in Postgres
        ├──▶ ZADD on Redis unread index (one per row)
        ├──▶ TenantHub: InboxEntryAdded signal on user:{platformUserId:N} (one per row)
        └──▶ TenantHub: InboxUnreadCountUpdated signal on user:{platformUserId:N} (latest count)

UI client (subscribed to TenantHub user:{platformUserId:N})
        ├──▶ Receives InboxEntryAdded(entryId) twice with same correlation key, < 30s apart
        ├──▶ Fetches GET /api/me/inbox (or /api/me/inbox/{id} per entry) for full content
        └──▶ Renders one grouped card with two sub-items, each individually dismissible
```

This flow is exactly the spec's US3 acceptance scenario, expressed as data movement. Three independent service writes converge to two inbox rows correlated by transaction ID, surfaced as one grouped UI card.
