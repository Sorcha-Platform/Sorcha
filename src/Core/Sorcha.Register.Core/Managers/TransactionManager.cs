// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;

namespace Sorcha.Register.Core.Managers;

/// <summary>
/// Manages transaction operations (storage, retrieval, validation)
/// </summary>
public class TransactionManager
{
    private readonly IRegisterRepository _repository;
    private readonly IEventPublisher _eventPublisher;

    public TransactionManager(
        IRegisterRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// Stores a validated transaction in the register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>TOCTOU Warning:</strong> The fork-detection check (GetTransactionsByPrevTxIdAsync)
    /// and the subsequent insert (InsertTransactionAsync) are not atomic. Under concurrent writes,
    /// a second transaction referencing the same PrevTxId could pass the check before the first
    /// insert completes, resulting in an undetected fork.
    /// </para>
    /// <para>
    /// The storage layer should enforce uniqueness via a database constraint (e.g., a unique index
    /// on (RegisterId, PrevTxId) for non-genesis transactions) to provide a definitive guard against
    /// forks. The in-application check here serves as a fast-path rejection only.
    /// </para>
    /// </remarks>
    public async Task<TransactionModel> StoreTransactionAsync(
        TransactionModel transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Validate basic transaction fields
        ValidateTransaction(transaction);

        // Verify register exists
        var register = await _repository.GetRegisterAsync(transaction.RegisterId, cancellationToken);
        if (register == null)
        {
            throw new InvalidOperationException($"Register {transaction.RegisterId} not found");
        }

        // Validate chain continuity
        if (!string.IsNullOrEmpty(transaction.PrevTxId) &&
            !transaction.PrevTxId.Equals("genesis", StringComparison.OrdinalIgnoreCase))
        {
            var prevTx = await _repository.GetTransactionAsync(
                transaction.RegisterId, transaction.PrevTxId, cancellationToken);

            if (prevTx == null)
            {
                throw new InvalidOperationException(
                    $"Previous transaction {transaction.PrevTxId} not found in register {transaction.RegisterId}");
            }

            // Check for forks: ensure no other tx already references this PrevTxId
            var existing = await _repository.GetTransactionsByPrevTxIdAsync(
                transaction.RegisterId, transaction.PrevTxId, cancellationToken);

            if (existing.Any())
            {
                throw new InvalidOperationException(
                    "Chain fork detected: another transaction already references this previous transaction");
            }
        }

        // Generate DID URI
        if (string.IsNullOrEmpty(transaction.Id))
        {
            transaction.Id = transaction.GenerateDidUri();
        }

        // Set timestamp if not already set
        if (transaction.TimeStamp == default)
        {
            transaction.TimeStamp = DateTime.UtcNow;
        }

        // Store transaction
        var storedTransaction = await _repository.InsertTransactionAsync(transaction, cancellationToken);

        // Publish transaction confirmed event
        await _eventPublisher.PublishAsync(
            "transaction:confirmed",
            new TransactionConfirmedEvent
            {
                TransactionId = storedTransaction.TxId,
                RegisterId = storedTransaction.RegisterId,
                ToWallets = storedTransaction.RecipientsWallets.ToList(),
                SenderWallet = storedTransaction.SenderWallet,
                PreviousTransactionId = storedTransaction.PrevTxId,
                MetaData = storedTransaction.MetaData,
                ConfirmedAt = DateTime.UtcNow
            },
            cancellationToken);

        return storedTransaction;
    }

    /// <summary>
    /// Gets a transaction by ID
    /// </summary>
    public async Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        return await _repository.GetTransactionAsync(registerId, transactionId, cancellationToken);
    }

    /// <summary>
    /// Gets all transactions for a register
    /// </summary>
    public async Task<IQueryable<TransactionModel>> GetTransactionsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        return await _repository.GetTransactionsAsync(registerId, cancellationToken);
    }

    /// <summary>
    /// Gets transactions by sender address
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsBySenderAsync(
        string registerId,
        string senderAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);

        return await _repository.GetAllTransactionsBySenderAddressAsync(
            registerId,
            senderAddress,
            cancellationToken);
    }

    /// <summary>
    /// Gets transactions by recipient address
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByRecipientAsync(
        string registerId,
        string recipientAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);

        return await _repository.GetAllTransactionsByRecipientAddressAsync(
            registerId,
            recipientAddress,
            cancellationToken);
    }

    /// <summary>
    /// Gets transactions for a specific docket
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        return await _repository.GetTransactionsByDocketAsync(
            registerId,
            docketId,
            cancellationToken);
    }

    /// <summary>
    /// Gets transactions by blueprint ID
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByBlueprintAsync(
        string registerId,
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        return await _repository.QueryTransactionsAsync(
            registerId,
            t => t.MetaData != null && t.MetaData.BlueprintId == blueprintId,
            cancellationToken);
    }

    /// <summary>
    /// Gets transactions by blueprint instance ID
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByInstanceAsync(
        string registerId,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        return await _repository.QueryTransactionsAsync(
            registerId,
            t => t.MetaData != null && t.MetaData.InstanceId == instanceId,
            cancellationToken);
    }

    /// <summary>
    /// Validates transaction basic fields
    /// </summary>
    private void ValidateTransaction(TransactionModel transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.RegisterId))
        {
            throw new ArgumentException("Transaction RegisterId is required", nameof(transaction));
        }

        if (string.IsNullOrWhiteSpace(transaction.TxId))
        {
            throw new ArgumentException("Transaction TxId is required", nameof(transaction));
        }

        if (transaction.TxId.Length != 64)
        {
            throw new ArgumentException("Transaction TxId must be 64 characters", nameof(transaction));
        }

        if (string.IsNullOrWhiteSpace(transaction.SenderWallet))
        {
            throw new ArgumentException("Transaction SenderWallet is required", nameof(transaction));
        }

        // Genesis/system transactions (SenderWallet = "system") don't require signatures
        // They are validated by the Validator Service before storage
        var isSystemTransaction = transaction.SenderWallet.Equals("system", StringComparison.OrdinalIgnoreCase);

        if (!isSystemTransaction && string.IsNullOrWhiteSpace(transaction.Signature))
        {
            throw new ArgumentException("Transaction Signature is required", nameof(transaction));
        }

        // Validate PrevTxId format when specified
        if (!string.IsNullOrEmpty(transaction.PrevTxId) &&
            !transaction.PrevTxId.Equals("genesis", StringComparison.OrdinalIgnoreCase) &&
            transaction.PrevTxId.Length != 64)
        {
            throw new ArgumentException("PrevTxId must be 64 characters when specified", nameof(transaction));
        }

        if (transaction.PayloadCount != (ulong)transaction.Payloads.Length)
        {
            throw new ArgumentException(
                $"Transaction PayloadCount ({transaction.PayloadCount}) does not match actual payload count ({transaction.Payloads.Length})",
                nameof(transaction));
        }
    }
}
