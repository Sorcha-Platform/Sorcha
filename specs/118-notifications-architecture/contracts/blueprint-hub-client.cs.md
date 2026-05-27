# `IBlueprintHubClient` — BlueprintHub typed client contract

Service: `Sorcha.Blueprint.Service`
Hub: `BlueprintHub` at `/hubs/blueprint` (alias: `/actionshub` during deprecation window)
Groups: see `data-model.md` → `BlueprintHubGroups`

## Method contract

```csharp
namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="BlueprintHub"/>.
/// Replaces the legacy untyped ActionsHub contract. Every method conforms to
/// the thin-signal contract — opaque IDs only.
/// </summary>
public interface IBlueprintHubClient
{
    /// <summary>
    /// A new action is available for the recipient wallet.
    /// </summary>
    /// <see cref="ActionsEndpoints.GetAction" path="/api/instances/{instanceId}/actions/{actionId}"/>
    Task ActionAvailable(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An action was rejected by the validation pipeline.
    /// </summary>
    /// <see cref="ActionsEndpoints.GetAction" path="/api/instances/{instanceId}/actions/{actionId}"/>
    Task ActionRejected(string instanceId, string actionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A workflow instance reached a terminal state.
    /// </summary>
    /// <see cref="InstanceEndpoints.GetInstance" path="/api/instances/{instanceId}"/>
    Task WorkflowCompleted(string instanceId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An instance state transition occurred (advancing through lifecycle).
    /// </summary>
    /// <see cref="InstanceEndpoints.GetInstance" path="/api/instances/{instanceId}"/>
    Task InstanceStateChanged(string instanceId, DateTimeOffset occurredAt, string traceId);
}
```

## Group emission rules

| Method | Group(s) | Emitter |
|---|---|---|
| `ActionAvailable` | `BlueprintHubGroups.Wallet(recipientAddress)` | `NotificationService` |
| `ActionRejected` | `BlueprintHubGroups.Wallet(recipientAddress)` | `NotificationService` |
| `WorkflowCompleted` | `BlueprintHubGroups.Wallet(recipientAddress)`, `BlueprintHubGroups.Instance(instanceId)` | `NotificationService` |
| `InstanceStateChanged` | `BlueprintHubGroups.Instance(instanceId)` | `WorkflowService` |

## Client-to-server methods

```csharp
public class BlueprintHub : Hub<IBlueprintHubClient>
{
    public Task SubscribeToWallet(string walletAddress);
    public Task UnsubscribeFromWallet(string walletAddress);
    public Task SubscribeToInstance(string instanceId);
    public Task UnsubscribeFromInstance(string instanceId);
}
```

## Removed from legacy ActionsHub

These events used to fire on ActionsHub but conceptually belonged to the wallet domain. They moved to `IWalletHubClient`:
- `EncryptionProgress`
- `EncryptionComplete`
- `EncryptionFailed`
- `CredentialReceived`
- `CredentialStatusChanged`
- `PendingCredentialCountUpdated`

UI code that previously injected `ActionsHubConnection` to subscribe to these split into `BlueprintHubConnection` (workflow-side) and `WalletHubConnection` (wallet-side).

## Auth

`[Authorize]` Bearer JWT with the `platform_user_id` claim required.
