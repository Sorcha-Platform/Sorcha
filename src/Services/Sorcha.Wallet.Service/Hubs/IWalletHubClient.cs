// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="WalletHub"/>. Bridge interface added
/// in Feature 118 Phase 3 (US1 — multi-node correctness).
/// </summary>
/// <remarks>
/// Phase 4 (US2) absorbs additional events from the retired EventsHub:
/// <c>EncryptionProgress</c>, <c>EncryptionComplete</c>, <c>EncryptionFailed</c>,
/// <c>CredentialReceived</c>, <c>CredentialStatusChanged</c>,
/// <c>PendingCredentialCountUpdated</c>, plus the new
/// <c>TransactionReceived</c> / <c>TransactionConfirmed</c> /
/// <c>TransactionReceipted</c> events for wallet-detail tick state. Today's
/// interface only carries the citizen-wallet (Feature 114) events that are
/// already emitted; later phases extend it.
/// </remarks>
public interface IWalletHubClient
{
    /// <summary>Citizen device was revoked. Sent on the citizen-wallet group.</summary>
    Task DeviceRevoked(Guid deviceId);

    /// <summary>A new credential is available for the citizen's wallet to sync.</summary>
    Task CredentialAvailable(string credentialId);
}
