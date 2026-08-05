// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Register.Core.Storage;

/// <summary>
/// Full read-write repository interface for register operations.
/// Only the Register Service should use this interface — other services
/// should use <see cref="IReadOnlyRegisterRepository"/> for read-only access.
/// </summary>
public interface IRegisterRepository : IReadOnlyRegisterRepository
{
    // ===========================
    // Register Writes
    // ===========================

    /// <summary>
    /// Inserts a new register
    /// </summary>
    Task<Models.Register> InsertRegisterAsync(Models.Register newRegister, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing register
    /// </summary>
    Task<Models.Register> UpdateRegisterAsync(Models.Register register, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a register
    /// </summary>
    Task DeleteRegisterAsync(string registerId, CancellationToken cancellationToken = default);

    // ===========================
    // DocketHeader Writes
    // ===========================

    /// <summary>
    /// Inserts a new docket
    /// </summary>
    Task<DocketHeader> InsertDocketAsync(DocketHeader docket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the register height atomically
    /// </summary>
    Task UpdateRegisterHeightAsync(
        string registerId,
        uint newHeight,
        CancellationToken cancellationToken = default);

    // ===========================
    // Transaction Writes
    // ===========================

    /// <summary>
    /// Inserts a new transaction
    /// </summary>
    Task<TransactionModel> InsertTransactionAsync(
        TransactionModel transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a transaction by ID from a register
    /// </summary>
    Task DeleteTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default);

    // ===========================
    // Receipt Writes
    // ===========================

    /// <summary>
    /// Inserts a batch of transaction receipts
    /// </summary>
    Task InsertReceiptsAsync(
        IEnumerable<TransactionReceipt> receipts,
        CancellationToken cancellationToken = default);
}
