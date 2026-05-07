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
/// Phase 4 (US2) absorbs additional events from the retired EventsHub and
/// from the BlueprintHub encryption surface — <c>EncryptionProgress</c>,
/// <c>EncryptionComplete</c>, <c>EncryptionFailed</c>, <c>CredentialReceived</c>,
/// <c>CredentialStatusChanged</c>, <c>PendingCredentialCountUpdated</c>, and the
/// transaction-lifecycle events <c>TransactionReceived</c> /
/// <c>TransactionConfirmed</c> / <c>TransactionReceipted</c>. Today's interface
/// only carries the citizen-wallet (Feature 114) events that are already
/// emitted; later phases extend it.
/// </remarks>
public interface IWalletHubClient
{
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
