// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Core.Storage;

/// <summary>
/// Read-only repository interface for register operations.
/// Use this in services that need to read register data but do not own the register database.
/// The Register Service owns read-write access via <see cref="IRegisterRepository"/>.
/// </summary>
public interface IReadOnlyRegisterRepository
{
    // ===========================
    // Register Reads
    // ===========================

    /// <summary>
    /// Checks if a register exists locally
    /// </summary>
    Task<bool> IsLocalRegisterAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registers
    /// </summary>
    Task<IEnumerable<Models.Register>> GetRegistersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries registers using a predicate
    /// </summary>
    Task<IEnumerable<Models.Register>> QueryRegistersAsync(
        Func<Models.Register, bool> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific register by ID
    /// </summary>
    Task<Models.Register?> GetRegisterAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts total number of registers
    /// </summary>
    Task<int> CountRegistersAsync(CancellationToken cancellationToken = default);

    // ===========================
    // Docket Reads
    // ===========================

    /// <summary>
    /// Gets all dockets for a register
    /// </summary>
    Task<IEnumerable<Docket>> GetDocketsAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific docket by register and docket ID
    /// </summary>
    Task<Docket?> GetDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default);

    // ===========================
    // Transaction Reads
    // ===========================

    /// <summary>
    /// Gets all transactions for a register (queryable)
    /// </summary>
    Task<IQueryable<TransactionModel>> GetTransactionsAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific transaction by register and transaction ID
    /// </summary>
    Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries transactions using LINQ expression
    /// </summary>
    Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all transactions in a specific docket
    /// </summary>
    Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default);

    // ===========================
    // Advanced Queries
    // ===========================

    /// <summary>
    /// Gets all transactions where the address is a recipient
    /// </summary>
    Task<IEnumerable<TransactionModel>> GetAllTransactionsByRecipientAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all transactions where the address is the sender
    /// </summary>
    Task<IEnumerable<TransactionModel>> GetAllTransactionsBySenderAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all transactions that reference the given previous transaction ID
    /// </summary>
    Task<IEnumerable<TransactionModel>> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        CancellationToken cancellationToken = default);

    // ===========================
    // Receipt Reads
    // ===========================

    /// <summary>
    /// Gets a transaction receipt by transaction ID
    /// </summary>
    Task<TransactionReceipt?> GetReceiptByTxIdAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated receipts for a specific docket
    /// </summary>
    Task<(IEnumerable<TransactionReceipt> Receipts, int Total)> GetReceiptsByDocketAsync(
        string registerId,
        long docketNumber,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    // ===========================
    // Revocation Queries
    // ===========================

    /// <summary>
    /// Finds a revocation transaction that targets the specified transaction ID.
    /// Searches for transactions with TransactionType.Revocation whose payload
    /// contains an OriginalTxId matching the target.
    /// </summary>
    /// <param name="registerId">The register identifier.</param>
    /// <param name="targetTxId">The transaction ID being checked for revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revocation transaction if found, null otherwise.</returns>
    Task<TransactionModel?> FindRevocationForTransactionAsync(
        string registerId,
        string targetTxId,
        CancellationToken cancellationToken = default);

    // ===========================
    // Credential Anchor Queries (Feature 155)
    // ===========================

    /// <summary>
    /// Locates the credential-issuance transaction on the given register that anchors the
    /// supplied credential id. Matches transactions whose tracking metadata carries
    /// <c>TrackingData["type"] == "credential-issuance"</c> and
    /// <c>TrackingData["credentialId"] == credentialId</c>.
    /// </summary>
    /// <param name="registerId">The register the credential claims to be anchored on.</param>
    /// <param name="credentialId">The credential's own identifier (jti).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first matching issuance transaction, or <c>null</c> if none matches.</returns>
    Task<TransactionModel?> GetCredentialIssuanceTransactionAsync(
        string registerId,
        string credentialId,
        CancellationToken cancellationToken = default);
}
