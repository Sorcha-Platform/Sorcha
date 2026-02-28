// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Manages Bitstring Status Lists for credential revocation and suspension tracking.
/// </summary>
public interface IStatusListManager
{
    /// <summary>
    /// Gets or creates a status list for the given issuer, register, and purpose.
    /// </summary>
    Task<BitstringStatusList> GetOrCreateListAsync(
        string issuerWallet, string registerId, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Allocates the next available index in the list. Returns the allocated index.
    /// </summary>
    Task<StatusListAllocation> AllocateIndexAsync(
        string issuerWallet, string registerId, string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Sets or clears a bit at the given index.
    /// </summary>
    Task<StatusListBitUpdate> SetBitAsync(
        string listId, int index, bool value, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a status list by its ID.
    /// </summary>
    Task<BitstringStatusList?> GetListAsync(string listId, CancellationToken ct = default);
}

/// <summary>
/// Result of allocating an index in a status list.
/// </summary>
public record StatusListAllocation(string ListId, int Index, string StatusListUrl);

/// <summary>
/// Result of setting a bit in a status list.
/// </summary>
public record StatusListBitUpdate(string ListId, int Index, bool Value, int Version, string? RegisterTxId);

/// <summary>
/// In-memory implementation of <see cref="IStatusListManager"/>.
/// Production would persist to MongoDB or PostgreSQL.
/// </summary>
public class StatusListManager : IStatusListManager, IDisposable
{
    private readonly ConcurrentDictionary<string, BitstringStatusList> _lists = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly IDistributedCache? _cache;
    private readonly ILogger<StatusListManager> _logger;
    private readonly string _baseUrl;

    public StatusListManager(
        ILogger<StatusListManager> logger,
        IConfiguration configuration,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _baseUrl = configuration.GetValue<string>("StatusList:BaseUrl")
            ?? "https://sorcha.example/api/v1/credentials/status-lists";
    }

    /// <inheritdoc />
    public Task<BitstringStatusList> GetOrCreateListAsync(
        string issuerWallet, string registerId, string purpose, CancellationToken ct = default)
    {
        var key = $"{issuerWallet}-{registerId}-{purpose}-1";
        var list = _lists.GetOrAdd(key, _ =>
        {
            _logger.LogInformation(
                "Creating new {Purpose} status list for issuer {Issuer} on register {Register}",
                purpose, issuerWallet, registerId);
            return BitstringStatusList.Create(issuerWallet, registerId, purpose);
        });

        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public async Task<StatusListAllocation> AllocateIndexAsync(
        string issuerWallet, string registerId, string credentialId, CancellationToken ct = default)
    {
        var list = await GetOrCreateListAsync(issuerWallet, registerId, "revocation", ct);
        var semaphore = _locks.GetOrAdd(list.Id, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            var index = list.AllocateIndex();
            if (index == -1)
            {
                _logger.LogWarning("Status list {ListId} is full (capacity: {Size})", list.Id, list.Size);
                throw new InvalidOperationException($"Status list {list.Id} is full");
            }

            _logger.LogInformation(
                "Allocated index {Index} in status list {ListId} for credential {CredentialId}",
                index, list.Id, credentialId);

            var url = $"{_baseUrl}/{list.Id}";
            return new StatusListAllocation(list.Id, index, url);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StatusListBitUpdate> SetBitAsync(
        string listId, int index, bool value, string? reason = null, CancellationToken ct = default)
    {
        if (!_lists.TryGetValue(listId, out var list))
            throw new KeyNotFoundException($"Status list {listId} not found");

        var semaphore = _locks.GetOrAdd(listId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            list.SetBit(index, value);

            // Invalidate cache
            if (_cache != null)
            {
                await _cache.RemoveAsync($"statuslist:{listId}", ct);
            }

            _logger.LogInformation(
                "Set bit {Index} to {Value} in status list {ListId} (v{Version}). Reason: {Reason}",
                index, value, listId, list.Version, reason ?? "(none)");

            return new StatusListBitUpdate(listId, index, value, list.Version, list.RegisterTxId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task<BitstringStatusList?> GetListAsync(string listId, CancellationToken ct = default)
    {
        _lists.TryGetValue(listId, out var list);
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();
        GC.SuppressFinalize(this);
    }
}
