# `IRegisterHubClient` — RegisterHub typed client contract

Service: `Sorcha.Register.Service`
Hub: `RegisterHub` at `/hubs/register`
Groups: see `data-model.md` → `RegisterHubGroups`

## Method contract

The interface already exists in tree (`src/Services/Sorcha.Register.Service/Hubs/IRegisterHubClient.cs`). Feature 118 changes:
- Adds `[Authorize]` to the hub class (FR-011 — staged behind a one-release UI ship-first)
- Tightens existing event payloads to thin-signal shape (FR-016 — FR-019); no new methods
- Adds `RegisterHubGroups` builder (FR-013, FR-014)

```csharp
namespace Sorcha.Register.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="RegisterHub"/>.
/// Existing surface — Feature 118 only tightens it to the thin-signal contract.
/// </summary>
public interface IRegisterHubClient
{
    /// <summary>
    /// A register was created.
    /// </summary>
    /// <see cref="RegistersEndpoints.GetRegister" path="/api/registers/{registerId}"/>
    Task RegisterCreated(string registerId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A register was deleted.
    /// </summary>
    /// <see cref="RegistersEndpoints.GetRegister" path="/api/registers/{registerId}"/>
    Task RegisterDeleted(string registerId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A register's status changed (active, suspended, archived).
    /// </summary>
    /// <see cref="RegistersEndpoints.GetRegister" path="/api/registers/{registerId}"/>
    Task RegisterStatusChanged(string registerId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction was confirmed in a register.
    /// </summary>
    /// <see cref="TransactionEndpoints.GetTransaction" path="/api/registers/{registerId}/transactions/{transactionId}"/>
    Task TransactionConfirmed(string registerId, string transactionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A docket was sealed.
    /// </summary>
    /// <see cref="DocketEndpoints.GetDocket" path="/api/registers/{registerId}/dockets/{docketNumber}"/>
    Task DocketSealed(string registerId, string docketNumber, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A register's height advanced.
    /// </summary>
    /// <see cref="RegistersEndpoints.GetRegister" path="/api/registers/{registerId}"/>
    Task RegisterHeightUpdated(string registerId, long newHeight, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A register's local sync state changed.
    /// </summary>
    /// <see cref="RegistersEndpoints.GetSyncState" path="/api/registers/{registerId}/sync-state"/>
    Task RegisterSyncStateChanged(string registerId, string syncState, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction receipt was issued.
    /// </summary>
    /// <see cref="ReceiptsEndpoints.GetReceipt" path="/api/registers/{registerId}/transactions/{transactionId}/receipt"/>
    Task TransactionReceipt(string registerId, string transactionId, string receiptId, DateTimeOffset occurredAt, string traceId);
}
```

## Group emission rules

| Method | Group(s) | Emitter |
|---|---|---|
| `RegisterCreated` | `RegisterHubGroups.Register(registerId)` | `RegisterService` |
| `RegisterDeleted` | `RegisterHubGroups.Register(registerId)` | `RegisterService` |
| `RegisterStatusChanged` | `RegisterHubGroups.Register(registerId)` | `RegisterService` |
| `TransactionConfirmed` | `RegisterHubGroups.Register(registerId)` | `RegisterEventBridgeService` |
| `DocketSealed` | `RegisterHubGroups.Register(registerId)` | `RegisterEventBridgeService` |
| `RegisterHeightUpdated` | `RegisterHubGroups.Register(registerId)` | `RegisterEventBridgeService` |
| `RegisterSyncStateChanged` | `RegisterHubGroups.Register(registerId)` | `RegisterSyncStateResolver` |
| `TransactionReceipt` | `RegisterHubGroups.Register(registerId)` | `ReceiptService` |

## Client-to-server methods

```csharp
public class RegisterHub : Hub<IRegisterHubClient>
{
    public Task SubscribeToRegister(string registerId);
    public Task UnsubscribeFromRegister(string registerId);
}
```

`SubscribeToRegister` validates the caller has an active subscription to the register via `SubscriptionServiceClient`. Existing behaviour preserved.

## Auth

Pre-Feature-118: `[Authorize]` not present on the hub class — connections accepted anonymously, subscription gated.

Post-Feature-118 (after FR-011 cutover): `[Authorize]` Bearer JWT with `platform_user_id` claim required at hub level. Subscription validation continues as a second layer.

## Migration sequencing

1. **Release N**: UI's `RegisterHubConnection` ships token-passing. Server accepts both authenticated and anonymous connections. `sorcha_signalr_connections_total{hub="register",authenticated=...}` gauge tracks rollout.
2. **Release N+1** (only when authenticated gauge is ≥ 99 % of total): server adds `[Authorize]`. Anonymous connections rejected with 401.
