// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Resolves blueprint versions by following transaction chains.
/// Caches version information for performance.
/// </summary>
public class BlueprintVersionResolver : IBlueprintVersionResolver
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IBlueprintCache _blueprintCache;
    private readonly ILogger<BlueprintVersionResolver> _logger;

    // Cache of blueprint version history per register+blueprint
    private readonly ConcurrentDictionary<string, List<BlueprintVersionInfo>> _versionHistoryCache = new();
    private readonly ConcurrentDictionary<string, ResolvedBlueprintVersion> _versionCache = new();

    // Maximum chain depth to follow to prevent infinite loops
    private const int MaxChainDepth = 1000;

    public BlueprintVersionResolver(
        IRegisterServiceClient registerClient,
        IBlueprintCache blueprintCache,
        ILogger<BlueprintVersionResolver> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _blueprintCache = blueprintCache ?? throw new ArgumentNullException(nameof(blueprintCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ResolvedBlueprintVersion?> ResolveForActionAsync(
        string registerId,
        string blueprintId,
        string previousTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousTransactionId);

        _logger.LogDebug(
            "Resolving blueprint version for action. Register: {RegisterId}, Blueprint: {BlueprintId}, PreviousTx: {PrevTxId}",
            registerId, blueprintId, previousTransactionId);

        // Follow the chain backwards to find the blueprint publication transaction
        var publicationTxId = await FindBlueprintPublicationTransactionAsync(
            registerId, blueprintId, previousTransactionId, ct);

        if (string.IsNullOrEmpty(publicationTxId))
        {
            _logger.LogWarning(
                "Could not find blueprint publication for action. Register: {RegisterId}, Blueprint: {BlueprintId}",
                registerId, blueprintId);
            return null;
        }

        return await GetByPublicationTransactionAsync(registerId, blueprintId, publicationTxId, ct);
    }

    /// <inheritdoc/>
    public async Task<ResolvedBlueprintVersion?> GetByPublicationTransactionAsync(
        string registerId,
        string blueprintId,
        string publicationTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationTransactionId);

        var cacheKey = $"{registerId}:{blueprintId}:{publicationTransactionId}";

        // Check cache first
        if (_versionCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // Get the publication transaction
        var tx = await _registerClient.GetTransactionAsync(registerId, publicationTransactionId, ct);
        if (tx == null)
        {
            _logger.LogWarning(
                "Publication transaction not found. Register: {RegisterId}, TxId: {TxId}",
                registerId, publicationTransactionId);
            return null;
        }

        // Verify this transaction is for the correct blueprint
        if (tx.MetaData?.BlueprintId != blueprintId)
        {
            _logger.LogWarning(
                "Transaction blueprint mismatch. Expected: {Expected}, Actual: {Actual}",
                blueprintId, tx.MetaData?.BlueprintId);
            return null;
        }

        // Get the blueprint from cache
        var blueprint = await _blueprintCache.GetBlueprintAsync(blueprintId, ct);
        if (blueprint == null)
        {
            _logger.LogWarning("Blueprint not found in cache: {BlueprintId}", blueprintId);
            return null;
        }

        // Get version history to determine version number
        var history = await GetVersionHistoryAsync(registerId, blueprintId, ct);
        var versionInfo = history.FirstOrDefault(v => v.PublicationTransactionId == publicationTransactionId);

        var resolved = new ResolvedBlueprintVersion
        {
            BlueprintId = blueprintId,
            VersionNumber = versionInfo?.VersionNumber ?? 1,
            PublicationTransactionId = publicationTransactionId,
            Blueprint = blueprint,
            PublishedAt = tx.TimeStamp,
            IsLatest = versionInfo?.IsLatest ?? true,
            PreviousVersionTransactionId = string.IsNullOrEmpty(tx.PrevTxId) ? null : tx.PrevTxId
        };

        // Cache the result
        _versionCache.TryAdd(cacheKey, resolved);

        return resolved;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BlueprintVersionInfo>> GetVersionHistoryAsync(
        string registerId,
        string blueprintId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        var cacheKey = $"{registerId}:{blueprintId}";

        // Check cache first
        if (_versionHistoryCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.AsReadOnly();
        }

        _logger.LogDebug(
            "Building version history for blueprint. Register: {RegisterId}, Blueprint: {BlueprintId}",
            registerId, blueprintId);

        // Get all transactions for the register and filter for blueprint publications
        var versions = new List<BlueprintVersionInfo>();
        var pageSize = 100;
        var page = 1;
        var hasMore = true;

        while (hasMore)
        {
            var txPage = await _registerClient.GetTransactionsAsync(registerId, page, pageSize, ct);

            foreach (var tx in txPage.Transactions)
            {
                // Check if this is a blueprint publication for our blueprint
                if (IsBlueprintPublicationTransaction(tx, blueprintId))
                {
                    versions.Add(new BlueprintVersionInfo
                    {
                        BlueprintId = blueprintId,
                        VersionNumber = 0, // Will be assigned after sorting
                        PublicationTransactionId = tx.TxId,
                        PublishedAt = tx.TimeStamp,
                        Title = null, // Would need to deserialize blueprint to get title
                        IsLatest = false // Will be updated after sorting
                    });
                }
            }

            hasMore = page < txPage.TotalPages;
            page++;
        }

        // Sort by timestamp (oldest first) and assign version numbers
        versions = versions
            .OrderBy(v => v.PublishedAt)
            .Select((v, i) => v with
            {
                VersionNumber = i + 1,
                IsLatest = i == versions.Count - 1
            })
            .ToList();

        // Mark the last one as latest
        if (versions.Count > 0)
        {
            versions[^1] = versions[^1] with { IsLatest = true };
        }

        // Cache the result
        _versionHistoryCache.TryAdd(cacheKey, versions);

        return versions.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<ResolvedBlueprintVersion?> GetLatestVersionAsync(
        string registerId,
        string blueprintId,
        CancellationToken ct = default)
    {
        var history = await GetVersionHistoryAsync(registerId, blueprintId, ct);
        var latest = history.LastOrDefault();

        if (latest == null)
        {
            return null;
        }

        return await GetByPublicationTransactionAsync(registerId, blueprintId, latest.PublicationTransactionId, ct);
    }

    /// <inheritdoc/>
    public async Task<ResolvedBlueprintVersion?> GetVersionAsOfAsync(
        string registerId,
        string blueprintId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        var history = await GetVersionHistoryAsync(registerId, blueprintId, ct);

        // Find the latest version that was published before or at the specified time
        var versionAtTime = history
            .Where(v => v.PublishedAt <= asOf)
            .OrderByDescending(v => v.PublishedAt)
            .FirstOrDefault();

        if (versionAtTime == null)
        {
            return null;
        }

        return await GetByPublicationTransactionAsync(registerId, blueprintId, versionAtTime.PublicationTransactionId, ct);
    }

    /// <summary>
    /// Follows the transaction chain backwards to find the blueprint publication transaction
    /// </summary>
    private async Task<string?> FindBlueprintPublicationTransactionAsync(
        string registerId,
        string blueprintId,
        string startTransactionId,
        CancellationToken ct)
    {
        var currentTxId = startTransactionId;
        var depth = 0;

        while (!string.IsNullOrEmpty(currentTxId) && depth < MaxChainDepth)
        {
            var tx = await _registerClient.GetTransactionAsync(registerId, currentTxId, ct);
            if (tx == null)
            {
                _logger.LogWarning(
                    "Transaction not found while following chain. Register: {RegisterId}, TxId: {TxId}",
                    registerId, currentTxId);
                return null;
            }

            // Check if this is the blueprint publication
            if (IsBlueprintPublicationTransaction(tx, blueprintId))
            {
                _logger.LogDebug(
                    "Found blueprint publication at depth {Depth}. TxId: {TxId}",
                    depth, currentTxId);
                return currentTxId;
            }

            // Check if we've reached the genesis (no previous transaction)
            if (string.IsNullOrEmpty(tx.PrevTxId))
            {
                _logger.LogDebug(
                    "Reached genesis without finding blueprint publication. BlueprintId: {BlueprintId}",
                    blueprintId);
                return null;
            }

            // Move to previous transaction
            currentTxId = tx.PrevTxId;
            depth++;
        }

        if (depth >= MaxChainDepth)
        {
            _logger.LogWarning(
                "Chain depth exceeded maximum ({MaxDepth}) while searching for blueprint publication",
                MaxChainDepth);
        }

        return null;
    }

    /// <summary>
    /// Determines if a transaction is a blueprint publication for the specified blueprint
    /// </summary>
    private static bool IsBlueprintPublicationTransaction(TransactionModel tx, string blueprintId)
    {
        if (tx.MetaData == null)
            return false;

        // A blueprint publication has:
        // - BlueprintId matching our target
        // - TransactionType.BlueprintPublish (post-#876) or TransactionType.Control (pre-#876)
        // - No persisted ActionId (the validator's MetaData projection sets ActionId to null
        //   when the submission's ActionId string ("blueprint-publish") does not parse as uint)

        if (tx.MetaData.BlueprintId != blueprintId)
            return false;

        // If ActionId is null, this is likely a blueprint publication
        // (actions always have an ActionId)
        if (!tx.MetaData.ActionId.HasValue)
            return true;

        // Legacy edge case: a publish tx where ActionId somehow got set. Both the post-#876
        // BlueprintPublish type and the pre-#876 Control type are valid here.
        if (tx.MetaData.TransactionType == TransactionType.BlueprintPublish
            || tx.MetaData.TransactionType == TransactionType.Control)
            return true;

        return false;
    }

    /// <summary>
    /// Clears the version cache (useful for testing or when blueprints are updated)
    /// </summary>
    public void ClearCache()
    {
        _versionHistoryCache.Clear();
        _versionCache.Clear();
    }

    /// <summary>
    /// Invalidates cache for a specific blueprint
    /// </summary>
    public void InvalidateCacheForBlueprint(string registerId, string blueprintId)
    {
        var historyKey = $"{registerId}:{blueprintId}";
        _versionHistoryCache.TryRemove(historyKey, out _);

        // Remove all version entries for this blueprint
        var keysToRemove = _versionCache.Keys
            .Where(k => k.StartsWith($"{registerId}:{blueprintId}:"))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _versionCache.TryRemove(key, out _);
        }
    }
}
