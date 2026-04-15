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
    /// Gets a credential by its ID.
    /// </summary>
    Task<CredentialEntity?> GetByIdAsync(string credentialId, CancellationToken ct = default);

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
