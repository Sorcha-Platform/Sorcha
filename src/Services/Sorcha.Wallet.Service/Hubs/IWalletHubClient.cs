// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="WalletHub"/>. Every method conforms
/// to the Feature 118 thin-signal contract — opaque IDs and timestamps only.
/// Clients fetch full detail through authenticated REST endpoints referenced
/// in each method's <c>&lt;see cref&gt;</c> doc.
/// </summary>
/// <remarks>
/// Surface declared per <c>contracts/wallet-hub-client.cs.md</c> (Feature 118
/// US2 — topology consolidation). Some methods don't yet have a server-side
/// emit site — those are forward-declared so the typed contract is in place
/// when the emitters land. The contract test
/// (<c>tests/Sorcha.Integration.Tests/Hubs/ThinSignalContractTests.cs</c>)
/// enforces parameter-shape on every method here.
/// </remarks>
public interface IWalletHubClient
{
    // === Transaction lifecycle ===

    /// <summary>
    /// A new inbound transaction arrived for the wallet (post peer-replication).
    /// Clients fetch full detail via
    /// <c>GET /api/wallets/{walletAddress}/transactions/{transactionId}</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task TransactionReceived(string walletAddress, string transactionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction sealed by the validator. Tick state advances to confirmed.
    /// Clients fetch full detail via
    /// <c>GET /api/wallets/{walletAddress}/transactions/{transactionId}</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task TransactionConfirmed(string walletAddress, string transactionId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A transaction receipt was generated. Tick state advances to receipted.
    /// Clients fetch the receipt via
    /// <c>GET /api/registers/{registerId}/transactions/{transactionId}/receipt</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="receiptId">Receipt identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task TransactionReceipted(string walletAddress, string transactionId, string receiptId, DateTimeOffset occurredAt, string traceId);

    // === Encryption operations ===

    /// <summary>
    /// Encryption operation progress. Carries the operation id only; clients
    /// fetch percent / stage / per-recipient state via
    /// <c>GET /api/operations/{operationId}</c>.
    /// </summary>
    /// <param name="operationId">Encryption operation identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task EncryptionProgress(string operationId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// Encryption operation completed successfully. Clients fetch the final
    /// result via <c>GET /api/operations/{operationId}</c>.
    /// </summary>
    /// <param name="operationId">Encryption operation identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task EncryptionComplete(string operationId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// Encryption operation failed. Clients fetch failure detail via
    /// <c>GET /api/operations/{operationId}</c>.
    /// </summary>
    /// <param name="operationId">Encryption operation identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task EncryptionFailed(string operationId, DateTimeOffset occurredAt, string traceId);

    // === Org credentials ===

    /// <summary>
    /// A new org credential was issued to the wallet. Clients fetch the
    /// credential via <c>GET /api/wallets/{walletAddress}/credentials/{credentialId}</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="credentialId">Credential identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task CredentialReceived(string walletAddress, string credentialId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// An org credential's status changed (revoked, suspended, etc.). Clients
    /// fetch authoritative state via
    /// <c>GET /api/wallets/{walletAddress}/credentials/{credentialId}</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="credentialId">Credential identifier.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task CredentialStatusChanged(string walletAddress, string credentialId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// The pending-credential count for the wallet changed. Clients fetch the
    /// list via <c>GET /api/wallets/{walletAddress}/credentials?status=pending</c>.
    /// </summary>
    /// <param name="walletAddress">Wallet group target.</param>
    /// <param name="pendingCount">New pending credential count.</param>
    /// <param name="occurredAt">Server timestamp at which the signal was emitted.</param>
    /// <param name="traceId">W3C trace-id for correlation.</param>
    Task PendingCredentialCountUpdated(string walletAddress, int pendingCount, DateTimeOffset occurredAt, string traceId);

    // === Citizen wallet (Feature 114) ===

    /// <summary>
    /// Citizen device was revoked. Sent on the citizen-wallet group.
    /// Clients fetch full device detail via
    /// <c>GET /api/me/devices/{deviceId}</c>.
    /// </summary>
    /// <param name="deviceId">Identifier of the revoked device.</param>
    Task DeviceRevoked(Guid deviceId);

    /// <summary>
    /// A new credential is available for the citizen's wallet to sync.
    /// The wallet pulls the delta via
    /// <c>GET /api/v1/wallet/sync?since={cursor}</c>.
    /// </summary>
    /// <param name="credentialId">Identifier of the newly-available credential.</param>
    Task CredentialAvailable(string credentialId);
}
