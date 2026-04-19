// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Store for managing verifiable credentials in a wallet.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Gets all credentials for a wallet address.
    /// </summary>
    Task<IReadOnlyList<CredentialEntity>> GetByWalletAsync(string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Gets a credential by its ID. Returns the first match across wallets — use
    /// <see cref="GetByIdForWalletAsync"/> when a specific wallet's copy is required
    /// (e.g. Feature 106 InboundCredentialDetector dedup, where both issuer and
    /// recipient hold rows with the same credential id on single-node deployments).
    /// </summary>
    Task<CredentialEntity?> GetByIdAsync(string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Feature 106 — returns the row keyed on <c>(credentialId, walletAddress)</c>,
    /// or null if this wallet has no copy of the credential yet. Used by the
    /// InboundCredentialDetector to dedup against its own prior inserts without
    /// tripping on the issuer's audit row.
    /// </summary>
    Task<CredentialEntity?> GetByIdForWalletAsync(
        string credentialId, string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Stores a new credential.
    /// </summary>
    Task StoreAsync(CredentialEntity credential, CancellationToken ct = default);

    /// <summary>
    /// Deletes a credential from the wallet store.
    /// </summary>
    Task<bool> DeleteAsync(string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Updates the status of a credential (e.g., Active → Revoked). Enforces the state machine
    /// defined on <see cref="CredentialStatus"/>; returns false on disallowed transitions.
    /// </summary>
    Task<bool> UpdateStatusAsync(string credentialId, CredentialStatus status, CancellationToken ct = default);

    /// <summary>
    /// Feature 106 — transitions a credential's status and returns the updated row.
    /// Throws <see cref="InvalidOperationException"/> on disallowed state-machine transitions
    /// (enforces invariants INV-1 through INV-4 from <c>data-model.md §2</c>). Returns
    /// <c>null</c> if the credential does not exist or does not belong to the given wallet.
    /// </summary>
    /// <remarks>
    /// Callers (the holder accept/decline PATCH endpoint) should surface the exception as
    /// <c>409 Conflict</c>.
    /// </remarks>
    Task<CredentialEntity?> PatchStatusAsync(
        string walletAddress,
        string credentialId,
        CredentialStatus newStatus,
        CancellationToken ct = default);

    /// <summary>
    /// Finds credentials matching the specified type and optional filters.
    /// </summary>
    Task<IReadOnlyList<CredentialEntity>> MatchAsync(
        string walletAddress,
        string? type = null,
        IEnumerable<string>? acceptedIssuers = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records a credential presentation, incrementing the count and consuming
    /// the credential if its usage policy limit has been reached.
    /// Returns true if the credential was consumed by this presentation.
    /// </summary>
    Task<bool> RecordPresentationAsync(string credentialId, CancellationToken ct = default);
}
