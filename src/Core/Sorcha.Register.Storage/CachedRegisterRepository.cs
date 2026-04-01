// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.Abstractions.Caching;

namespace Sorcha.Register.Storage;

/// <summary>
/// Cached implementation of IRegisterRepository using verified cache for dockets
/// and standard cache for registers and transactions.
/// </summary>
/// <remarks>
/// This implementation provides:
/// - Verified cache for dockets (WORM store backed with hash verification)
/// - Standard cache for registers (mutable data)
/// - Standard cache for transactions (with cache-aside pattern)
/// </remarks>
public class CachedRegisterRepository : IRegisterRepository
{
    private readonly IRegisterRepository _innerRepository;
    private readonly IVerifiedCache<Docket, ulong>? _docketCache;
    private readonly ICacheStore _cacheStore;
    private readonly RegisterStorageConfiguration _configuration;
    private readonly ILogger<CachedRegisterRepository>? _logger;

    private const string RegisterKeyPrefix = "register:reg:";
    private const string TransactionKeyPrefix = "register:tx:";

    /// <summary>
    /// Initializes a new instance of the CachedRegisterRepository.
    /// </summary>
    /// <param name="innerRepository">The underlying repository to wrap.</param>
    /// <param name="docketCache">Verified cache for dockets (optional).</param>
    /// <param name="cacheStore">Cache store for registers and transactions.</param>
    /// <param name="options">Configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public CachedRegisterRepository(
        IRegisterRepository innerRepository,
        IVerifiedCache<Docket, ulong>? docketCache,
        ICacheStore cacheStore,
        IOptions<RegisterStorageConfiguration> options,
        ILogger<CachedRegisterRepository>? logger = null)
    {
        _innerRepository = innerRepository ?? throw new ArgumentNullException(nameof(innerRepository));
        _docketCache = docketCache;
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _configuration = options?.Value ?? new RegisterStorageConfiguration();
        _logger = logger;
    }

    // ===========================
    // Register Operations (Standard Cache)
    // ===========================

    public async Task<bool> IsLocalRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = $"{RegisterKeyPrefix}{registerId}";
        var cached = await _cacheStore.GetAsync<Models.Register>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return true;
        }

        return await _innerRepository.IsLocalRegisterAsync(registerId, cancellationToken);
    }

    public async Task<IEnumerable<Models.Register>> GetRegistersAsync(CancellationToken cancellationToken = default)
    {
        // Bypass cache for list operations - go directly to repository
        return await _innerRepository.GetRegistersAsync(cancellationToken);
    }

    public async Task<IEnumerable<Models.Register>> QueryRegistersAsync(
        Func<Models.Register, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for query operations
        return await _innerRepository.QueryRegistersAsync(predicate, cancellationToken);
    }

    public async Task<Models.Register?> GetRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{RegisterKeyPrefix}{registerId}";

        // Try cache first
        var cached = await _cacheStore.GetAsync<Models.Register>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger?.LogDebug("Cache hit for register {RegisterId}", registerId);
            return cached;
        }

        // Cache miss - fetch from repository
        var register = await _innerRepository.GetRegisterAsync(registerId, cancellationToken);
        if (register is not null)
        {
            await _cacheStore.SetAsync(cacheKey, register, TimeSpan.FromHours(1), cancellationToken);
            _logger?.LogDebug("Cached register {RegisterId}", registerId);
        }

        return register;
    }

    public async Task<Models.Register> InsertRegisterAsync(Models.Register newRegister, CancellationToken cancellationToken = default)
    {
        var result = await _innerRepository.InsertRegisterAsync(newRegister, cancellationToken);

        // Populate cache
        var cacheKey = $"{RegisterKeyPrefix}{result.Id}";
        await _cacheStore.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        _logger?.LogDebug("Inserted and cached register {RegisterId}", result.Id);
        return result;
    }

    public async Task<Models.Register> UpdateRegisterAsync(Models.Register register, CancellationToken cancellationToken = default)
    {
        var result = await _innerRepository.UpdateRegisterAsync(register, cancellationToken);

        // Update cache
        var cacheKey = $"{RegisterKeyPrefix}{result.Id}";
        await _cacheStore.SetAsync(cacheKey, result, TimeSpan.FromHours(1), cancellationToken);

        _logger?.LogDebug("Updated and cached register {RegisterId}", result.Id);
        return result;
    }

    public async Task DeleteRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        await _innerRepository.DeleteRegisterAsync(registerId, cancellationToken);

        // Invalidate cache
        var cacheKey = $"{RegisterKeyPrefix}{registerId}";
        await _cacheStore.RemoveAsync(cacheKey, cancellationToken);

        // Also invalidate related docket and transaction caches
        await _cacheStore.RemoveByPatternAsync($"register:docket:{registerId}:*", cancellationToken);
        await _cacheStore.RemoveByPatternAsync($"register:tx:{registerId}:*", cancellationToken);

        _logger?.LogDebug("Deleted register {RegisterId} and invalidated caches", registerId);
    }

    public Task<int> CountRegistersAsync(CancellationToken cancellationToken = default)
    {
        return _innerRepository.CountRegistersAsync(cancellationToken);
    }

    // ===========================
    // Docket Operations (Verified Cache)
    // ===========================

    public async Task<IEnumerable<Docket>> GetDocketsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        // For list operations, go directly to repository
        // Could optimize with range queries on verified cache if needed
        return await _innerRepository.GetDocketsAsync(registerId, cancellationToken);
    }

    public async Task<Docket?> GetDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        // Use verified cache if available
        if (_docketCache is not null)
        {
            var cached = await _docketCache.GetAsync(docketId, cancellationToken);
            if (cached is not null && cached.RegisterId == registerId)
            {
                _logger?.LogDebug("Verified cache hit for docket {DocketId} in register {RegisterId}", docketId, registerId);
                return cached;
            }
        }

        // Fall back to repository
        var docket = await _innerRepository.GetDocketAsync(registerId, docketId, cancellationToken);
        return docket;
    }

    public async Task<Docket> InsertDocketAsync(Docket docket, CancellationToken cancellationToken = default)
    {
        // Dockets are immutable - use verified cache if available
        if (_docketCache is not null)
        {
            // Append through verified cache (writes to both cache and WORM store)
            var result = await _docketCache.AppendAsync(docket, cancellationToken);
            _logger?.LogInformation(
                "Appended docket {DocketId} to register {RegisterId} via verified cache",
                result.Id, result.RegisterId);
            return result;
        }

        // Fall back to repository
        return await _innerRepository.InsertDocketAsync(docket, cancellationToken);
    }

    public async Task UpdateRegisterHeightAsync(
        string registerId,
        uint newHeight,
        CancellationToken cancellationToken = default)
    {
        await _innerRepository.UpdateRegisterHeightAsync(registerId, newHeight, cancellationToken);

        // Invalidate register cache
        var cacheKey = $"{RegisterKeyPrefix}{registerId}";
        await _cacheStore.RemoveAsync(cacheKey, cancellationToken);

        _logger?.LogDebug("Updated height for register {RegisterId} to {Height}", registerId, newHeight);
    }

    // ===========================
    // Transaction Operations (Standard Cache)
    // ===========================

    public Task<IQueryable<TransactionModel>> GetTransactionsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for queryable operations
        return _innerRepository.GetTransactionsAsync(registerId, cancellationToken);
    }

    public async Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{TransactionKeyPrefix}{registerId}:{transactionId}";

        // Try cache first
        var cached = await _cacheStore.GetAsync<TransactionModel>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger?.LogDebug("Cache hit for transaction {TransactionId}", transactionId);
            return cached;
        }

        // Cache miss - fetch from repository
        var transaction = await _innerRepository.GetTransactionAsync(registerId, transactionId, cancellationToken);
        if (transaction is not null)
        {
            var ttl = TimeSpan.FromSeconds(_configuration.TransactionCacheConfiguration.CacheTtlSeconds);
            await _cacheStore.SetAsync(cacheKey, transaction, ttl, cancellationToken);
            _logger?.LogDebug("Cached transaction {TransactionId}", transactionId);
        }

        return transaction;
    }

    public async Task<TransactionModel> InsertTransactionAsync(
        TransactionModel transaction,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerRepository.InsertTransactionAsync(transaction, cancellationToken);

        // Populate cache
        var cacheKey = $"{TransactionKeyPrefix}{result.RegisterId}:{result.TxId}";
        var ttl = TimeSpan.FromSeconds(_configuration.TransactionCacheConfiguration.CacheTtlSeconds);
        await _cacheStore.SetAsync(cacheKey, result, ttl, cancellationToken);

        _logger?.LogDebug("Inserted and cached transaction {TransactionId}", result.TxId);
        return result;
    }

    public async Task DeleteTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default)
    {
        await _innerRepository.DeleteTransactionAsync(registerId, txId, cancellationToken);

        var cacheKey = $"{TransactionKeyPrefix}{registerId}:{txId}";
        await _cacheStore.RemoveAsync(cacheKey, cancellationToken);

        _logger?.LogDebug("Deleted transaction {TxId} from register {RegisterId} and cache", txId, registerId);
    }

    public Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for query operations
        return _innerRepository.QueryTransactionsAsync(registerId, predicate, cancellationToken);
    }

    /// <summary>
    /// Gets all transactions associated with a specific docket ID.
    /// Bypasses cache for query operations - could be optimized with batch caching.
    /// </summary>
    /// <param name="registerId">The register identifier.</param>
    /// <param name="docketId">The docket ID to filter transactions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of transactions belonging to the docket.</returns>
    public Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache - could optimize with batch caching
        return _innerRepository.GetTransactionsByDocketAsync(registerId, docketId, cancellationToken);
    }

    public Task<IEnumerable<TransactionModel>> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for query operations
        return _innerRepository.GetTransactionsByPrevTxIdAsync(registerId, prevTxId, cancellationToken);
    }

    // ===========================
    // Advanced Queries
    // ===========================

    /// <summary>
    /// Gets all transactions where the specified address is the recipient.
    /// Bypasses cache for query operations.
    /// </summary>
    /// <param name="registerId">The register identifier.</param>
    /// <param name="address">The recipient wallet address to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of transactions received by the address.</returns>
    public Task<IEnumerable<TransactionModel>> GetAllTransactionsByRecipientAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for query operations
        return _innerRepository.GetAllTransactionsByRecipientAddressAsync(registerId, address, cancellationToken);
    }

    /// <summary>
    /// Gets all transactions where the specified address is the sender.
    /// Bypasses cache for query operations.
    /// </summary>
    /// <param name="registerId">The register identifier.</param>
    /// <param name="address">The sender wallet address to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of transactions sent by the address.</returns>
    public Task<IEnumerable<TransactionModel>> GetAllTransactionsBySenderAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        // Bypass cache for query operations
        return _innerRepository.GetAllTransactionsBySenderAddressAsync(registerId, address, cancellationToken);
    }

    // ===========================
    // Cache Management
    // ===========================

    /// <summary>
    /// Gets statistics from the docket verified cache.
    /// </summary>
    public async Task<VerifiedCacheStatistics?> GetDocketCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_docketCache is null)
        {
            return null;
        }

        return await _docketCache.GetStatisticsAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies integrity of the docket cache against WORM store.
    /// </summary>
    public async Task<CacheIntegrityResult?> VerifyDocketCacheIntegrityAsync(
        ulong? startId = null,
        ulong? endId = null,
        CancellationToken cancellationToken = default)
    {
        if (_docketCache is null)
        {
            return null;
        }

        return await _docketCache.VerifyIntegrityAsync(
            startId ?? default,
            endId ?? default,
            cancellationToken);
    }

    /// <summary>
    /// Warms the docket cache with recent dockets.
    /// </summary>
    public async Task WarmDocketCacheAsync(
        ulong upToSequence,
        IProgress<CacheWarmingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_docketCache is null)
        {
            _logger?.LogWarning("Docket cache is not available, skipping cache warming");
            return;
        }

        await _docketCache.WarmCacheAsync(upToSequence, progress, cancellationToken);
    }

    /// <summary>
    /// Invalidates all caches for a specific register.
    /// </summary>
    public async Task InvalidateRegisterCachesAsync(string registerId, CancellationToken cancellationToken = default)
    {
        await _cacheStore.RemoveAsync($"{RegisterKeyPrefix}{registerId}", cancellationToken);
        await _cacheStore.RemoveByPatternAsync($"register:docket:{registerId}:*", cancellationToken);
        await _cacheStore.RemoveByPatternAsync($"register:tx:{registerId}:*", cancellationToken);

        _logger?.LogInformation("Invalidated all caches for register {RegisterId}", registerId);
    }

    // ===========================
    // Receipt Operations
    // ===========================

    /// <inheritdoc/>
    public Task InsertReceiptsAsync(
        IEnumerable<TransactionReceipt> receipts,
        CancellationToken cancellationToken = default)
    {
        return _innerRepository.InsertReceiptsAsync(receipts, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TransactionReceipt?> GetReceiptByTxIdAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default)
    {
        return _innerRepository.GetReceiptByTxIdAsync(registerId, txId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<(IEnumerable<TransactionReceipt> Receipts, int Total)> GetReceiptsByDocketAsync(
        string registerId,
        long docketNumber,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return _innerRepository.GetReceiptsByDocketAsync(registerId, docketNumber, page, pageSize, cancellationToken);
    }

    // ===========================
    // Revocation Queries
    // ===========================

    /// <inheritdoc/>
    public Task<TransactionModel?> FindRevocationForTransactionAsync(
        string registerId,
        string targetTxId,
        CancellationToken cancellationToken = default)
    {
        return _innerRepository.FindRevocationForTransactionAsync(registerId, targetTxId, cancellationToken);
    }
}
