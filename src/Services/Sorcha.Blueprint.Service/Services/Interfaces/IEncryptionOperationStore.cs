// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Tracks encryption operation status for async pipeline monitoring.
/// </summary>
public interface IEncryptionOperationStore
{
    /// <summary>
    /// Creates a new encryption operation.
    /// </summary>
    /// <param name="operation">The operation to track.</param>
    /// <returns>The created operation.</returns>
    Task<EncryptionOperation> CreateAsync(EncryptionOperation operation);

    /// <summary>
    /// Updates an existing encryption operation.
    /// </summary>
    /// <param name="operation">The operation with updated fields.</param>
    /// <returns>The updated operation.</returns>
    Task<EncryptionOperation> UpdateAsync(EncryptionOperation operation);

    /// <summary>
    /// Gets an encryption operation by ID.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <returns>The operation, or null if not found.</returns>
    Task<EncryptionOperation?> GetByIdAsync(string operationId);

    /// <summary>
    /// Gets the most recent encryption operation for a wallet address.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address.</param>
    /// <returns>The most recent operation, or null if none found.</returns>
    Task<EncryptionOperation?> GetByWalletAddressAsync(string walletAddress);
}
