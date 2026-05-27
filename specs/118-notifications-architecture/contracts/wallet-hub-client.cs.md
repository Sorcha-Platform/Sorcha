# `IWalletHubClient` — WalletHub typed client contract

Service: `Sorcha.Wallet.Service`
Hub: `WalletHub` at `/hubs/wallet`
Groups: see `data-model.md` → `WalletHubGroups`

## Method contract

```csharp
namespace Sorcha.Wallet.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="WalletHub"/>.
/// Absorbs encryption + credential events from the retired EventsHub and ActionsHub.
/// Citizen-wallet device events (Feature 114) preserved unchanged.
/// </summary>
public interface IWalletHubClient
{
    // === Transaction lifecycle ===

    /// <summary>
    /// A new inbound transaction arrived for the wallet (post peer-replication).
    /// </summary>
    /// <see cref="WalletEndpoints.GetWalletTransaction" path="/api/wallets/{walletAddress}/transactions/{transactionId}"/>
    Task TransactionReceived(string walletAddress, string transactionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction sealed by the validator. Tick state advances to confirmed.
    /// </summary>
    /// <see cref="WalletEndpoints.GetWalletTransaction" path="/api/wallets/{walletAddress}/transactions/{transactionId}"/>
    Task TransactionConfirmed(string walletAddress, string transactionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction receipt was generated. Tick state advances to receipted.
    /// </summary>
    /// <see cref="ReceiptsEndpoints.GetReceipt" path="/api/registers/{registerId}/transactions/{transactionId}/receipt"/>
    Task TransactionReceipted(string walletAddress, string transactionId, string receiptId, DateTimeOffset occurredAt, string traceId);

    // === Encryption operations ===

    /// <summary>
    /// Progress on a long-running encryption operation.
    /// </summary>
    /// <see cref="OperationsEndpoints.GetOperation" path="/api/operations/{operationId}"/>
    Task EncryptionProgress(string walletAddress, string operationId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An encryption operation completed.
    /// </summary>
    /// <see cref="OperationsEndpoints.GetOperation" path="/api/operations/{operationId}"/>
    Task EncryptionComplete(string walletAddress, string operationId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An encryption operation failed.
    /// </summary>
    /// <see cref="OperationsEndpoints.GetOperation" path="/api/operations/{operationId}"/>
    Task EncryptionFailed(string walletAddress, string operationId, DateTimeOffset occurredAt, string traceId);

    // === Credentials ===

    /// <summary>
    /// A new credential was issued to the wallet.
    /// </summary>
    /// <see cref="WalletEndpoints.GetCredential" path="/api/wallets/{walletAddress}/credentials/{credentialId}"/>
    Task CredentialReceived(string walletAddress, string credentialId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A credential's status changed (revoked, suspended, etc.).
    /// </summary>
    /// <see cref="WalletEndpoints.GetCredential" path="/api/wallets/{walletAddress}/credentials/{credentialId}"/>
    Task CredentialStatusChanged(string walletAddress, string credentialId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// The pending-credential count for the wallet changed.
    /// </summary>
    /// <see cref="WalletEndpoints.ListPendingCredentials" path="/api/wallets/{walletAddress}/credentials?status=pending"/>
    Task PendingCredentialCountUpdated(string walletAddress, int pendingCount, DateTimeOffset occurredAt, string traceId);

    // === Citizen wallet (Feature 114) — preserved unchanged ===

    /// <summary>
    /// The citizen's device was revoked (admin-initiated).
    /// </summary>
    /// <see cref="DevicesEndpoints.GetDevice" path="/api/me/devices/{deviceId}"/>
    Task DeviceRevoked(string deviceId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A new credential is available for the citizen's wallet to sync.
    /// </summary>
    /// <see cref="CitizenWalletEndpoints.Sync" path="/api/v1/wallet/sync"/>
    Task CredentialAvailable(string credentialId, DateTimeOffset occurredAt, string traceId);
}
```

## Group emission rules

| Method | Group(s) | Emitter |
|---|---|---|
| `TransactionReceived` | `WalletHubGroups.Wallet(walletAddress)` | `InboundTransactionRouter` |
| `TransactionConfirmed` | `WalletHubGroups.Wallet(walletAddress)` | `TransactionLifecycleEventBridge` |
| `TransactionReceipted` | `WalletHubGroups.Wallet(walletAddress)` | `TransactionLifecycleEventBridge` |
| `EncryptionProgress` | `WalletHubGroups.Wallet(walletAddress)` | encryption pipeline |
| `EncryptionComplete` | `WalletHubGroups.Wallet(walletAddress)` | encryption pipeline |
| `EncryptionFailed` | `WalletHubGroups.Wallet(walletAddress)` | encryption pipeline |
| `CredentialReceived` | `WalletHubGroups.Wallet(walletAddress)` | `CredentialIssuanceService` |
| `CredentialStatusChanged` | `WalletHubGroups.Wallet(walletAddress)` | `CredentialStatusService` |
| `PendingCredentialCountUpdated` | `WalletHubGroups.Wallet(walletAddress)` | `CredentialIssuanceService` |
| `DeviceRevoked` | `WalletHubGroups.CitizenWallet(platformUserId)` | `DeviceRevocationService` |
| `CredentialAvailable` | `WalletHubGroups.CitizenWallet(platformUserId)` | citizen credential pipeline |

## Client-to-server methods

```csharp
public class WalletHub : Hub<IWalletHubClient>
{
    public Task SubscribeToWallet(string walletAddress);
    public Task UnsubscribeFromWallet(string walletAddress);
    // Citizen wallet (Feature 114) — implicit subscription on connect
    // No explicit Subscribe/Unsubscribe — group is keyed off platform_user_id claim
}
```

## Auth

`[Authorize(AuthenticationSchemes = "Bearer")]` with `platform_user_id` claim required. Wallet ownership for `SubscribeToWallet` is verified server-side by checking the calling user owns the wallet (existing pattern from ActionsHub).
