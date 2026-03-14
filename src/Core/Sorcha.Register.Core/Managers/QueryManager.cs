// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;

namespace Sorcha.Register.Core.Managers;

/// <summary>
/// Manages complex queries across registers, transactions, and dockets
/// </summary>
public class QueryManager
{
    private readonly IRegisterRepository _repository;

    public QueryManager(IRegisterRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Gets transactions with LINQ expression filtering
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(predicate);

        return await _repository.QueryTransactionsAsync(registerId, predicate, cancellationToken);
    }

    /// <summary>
    /// Gets all transactions for a register as queryable
    /// </summary>
    public async Task<IQueryable<TransactionModel>> GetQueryableTransactionsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        return await _repository.GetTransactionsAsync(registerId, cancellationToken);
    }

    /// <summary>
    /// Gets transactions with pagination
    /// </summary>
    public async Task<PaginatedResult<TransactionModel>> GetTransactionsPaginatedAsync(
        string registerId,
        int page = 1,
        int pageSize = 20,
        Expression<Func<TransactionModel, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = await _repository.GetTransactionsAsync(registerId, cancellationToken);

        // Apply filter if provided
        if (filter != null)
        {
            query = query.Where(filter);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(t => t.TimeStamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PaginatedResult<TransactionModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    /// <summary>
    /// Gets transactions by wallet address with pagination
    /// </summary>
    public async Task<PaginatedResult<TransactionModel>> GetTransactionsByWalletPaginatedAsync(
        string registerId,
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        bool asSender = true,
        bool asRecipient = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var allTransactions = new List<TransactionModel>();

        if (asSender)
        {
            var senderTxs = await _repository.GetAllTransactionsBySenderAddressAsync(
                registerId,
                walletAddress,
                cancellationToken);
            allTransactions.AddRange(senderTxs);
        }

        if (asRecipient)
        {
            var recipientTxs = await _repository.GetAllTransactionsByRecipientAddressAsync(
                registerId,
                walletAddress,
                cancellationToken);
            allTransactions.AddRange(recipientTxs);
        }

        // Remove duplicates and order by timestamp
        var distinctTransactions = allTransactions
            .DistinctBy(t => t.TxId)
            .OrderByDescending(t => t.TimeStamp)
            .ToList();

        var total = distinctTransactions.Count;
        var items = distinctTransactions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PaginatedResult<TransactionModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    /// <summary>
    /// Gets transactions by wallet address across all registers with pagination.
    /// Returns items wrapped with register context (registerId, registerName).
    /// </summary>
    public async Task<PaginatedResult<CrossRegisterTransactionItem>> GetTransactionsByWalletAcrossRegistersAsync(
        string walletAddress,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var registers = (await _repository.GetRegistersAsync(cancellationToken)).ToList();
        var allItems = new List<CrossRegisterTransactionItem>();

        foreach (var register in registers)
        {
            var senderTxs = await _repository.GetAllTransactionsBySenderAddressAsync(
                register.Id, walletAddress, cancellationToken);
            var recipientTxs = await _repository.GetAllTransactionsByRecipientAddressAsync(
                register.Id, walletAddress, cancellationToken);

            var registerTxs = senderTxs.Concat(recipientTxs)
                .DistinctBy(t => t.TxId)
                .Select(t => new CrossRegisterTransactionItem
                {
                    Transaction = t,
                    RegisterId = register.Id,
                    RegisterName = register.Name
                });

            allItems.AddRange(registerTxs);
        }

        var sorted = allItems
            .OrderByDescending(i => i.Transaction.TimeStamp)
            .ToList();

        var total = sorted.Count;
        var items = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PaginatedResult<CrossRegisterTransactionItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    /// <summary>
    /// Gets transactions by previous transaction ID with pagination.
    /// Used for fork detection and chain traversal.
    /// </summary>
    public async Task<PaginatedResult<TransactionModel>> GetTransactionsByPrevTxIdPaginatedAsync(
        string registerId,
        string prevTxId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        if (string.IsNullOrEmpty(prevTxId))
        {
            return new PaginatedResult<TransactionModel>
            {
                Items = new List<TransactionModel>(),
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
                TotalPages = 0
            };
        }

        var transactions = (await _repository.GetTransactionsByPrevTxIdAsync(
            registerId,
            prevTxId,
            cancellationToken))
            .OrderByDescending(t => t.TimeStamp)
            .ToList();

        var total = transactions.Count;
        var items = transactions
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PaginatedResult<TransactionModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    /// <summary>
    /// Gets transactions by blueprint with optional instance filtering
    /// </summary>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByBlueprintAsync(
        string registerId,
        string blueprintId,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        Expression<Func<TransactionModel, bool>> predicate;

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            predicate = t => t.MetaData != null
                && t.MetaData.BlueprintId == blueprintId
                && t.MetaData.InstanceId == instanceId;
        }
        else
        {
            predicate = t => t.MetaData != null && t.MetaData.BlueprintId == blueprintId;
        }

        return await _repository.QueryTransactionsAsync(registerId, predicate, cancellationToken);
    }

    /// <summary>
    /// Gets transaction statistics for a register
    /// </summary>
    public async Task<TransactionStatistics> GetTransactionStatisticsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var transactions = await _repository.GetTransactionsAsync(registerId, cancellationToken);
        var transactionList = transactions.ToList();

        return new TransactionStatistics
        {
            TotalTransactions = transactionList.Count,
            UniqueWallets = transactionList
                .SelectMany(t => new[] { t.SenderWallet }.Concat(t.RecipientsWallets))
                .Distinct()
                .Count(),
            UniqueSenders = transactionList.Select(t => t.SenderWallet).Distinct().Count(),
            UniqueRecipients = transactionList
                .SelectMany(t => t.RecipientsWallets)
                .Distinct()
                .Count(),
            TotalPayloads = transactionList.Sum(t => (long)t.PayloadCount),
            EarliestTransaction = transactionList.MinBy(t => t.TimeStamp)?.TimeStamp,
            LatestTransaction = transactionList.MaxBy(t => t.TimeStamp)?.TimeStamp
        };
    }
}

/// <summary>
/// Paginated query result
/// </summary>
public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>
/// A transaction item with register context for cross-register queries
/// </summary>
public class CrossRegisterTransactionItem
{
    public required TransactionModel Transaction { get; init; }
    public required string RegisterId { get; init; }
    public required string RegisterName { get; init; }
}

/// <summary>
/// Transaction statistics
/// </summary>
public class TransactionStatistics
{
    public int TotalTransactions { get; set; }
    public int UniqueWallets { get; set; }
    public int UniqueSenders { get; set; }
    public int UniqueRecipients { get; set; }
    public long TotalPayloads { get; set; }
    public DateTime? EarliestTransaction { get; set; }
    public DateTime? LatestTransaction { get; set; }
}
