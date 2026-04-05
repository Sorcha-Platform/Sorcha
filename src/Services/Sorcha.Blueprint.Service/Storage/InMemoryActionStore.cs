// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Models.Responses;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// In-memory implementation of action storage (for MVP/testing)
/// </summary>
public class InMemoryActionStore : IActionStore
{
    private readonly ConcurrentDictionary<string, ActionDetailsResponse> _actions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FileMetadata>> _fileMetadata = new();
    private readonly ConcurrentDictionary<string, byte[]> _fileContent = new();
    private readonly ConcurrentDictionary<string, (string TransactionHash, DateTime Expiry)> _idempotencyKeys = new();

    // Tracks the wall-clock time each (transactionHash, fileId) pair was stored,
    // so the orphan cleanup service can apply an age threshold.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _fileStoredAt = new();

    public Task<ActionDetailsResponse> StoreActionAsync(ActionDetailsResponse action)
    {
        _actions[action.TransactionHash] = action;
        return Task.FromResult(action);
    }

    public Task<ActionDetailsResponse?> GetActionAsync(string transactionHash)
    {
        _actions.TryGetValue(transactionHash, out var action);
        return Task.FromResult(action);
    }

    public Task<IEnumerable<ActionDetailsResponse>> GetActionsAsync(
        string walletAddress,
        string registerAddress,
        int skip = 0,
        int take = 20)
    {
        var actions = _actions.Values
            .Where(a => a.SenderWallet == walletAddress && a.RegisterAddress == registerAddress)
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(actions.AsEnumerable());
    }

    public Task<int> GetActionCountAsync(string walletAddress, string registerAddress)
    {
        var count = _actions.Values
            .Count(a => a.SenderWallet == walletAddress && a.RegisterAddress == registerAddress);

        return Task.FromResult(count);
    }

    public Task StoreFileMetadataAsync(string transactionHash, string fileId, FileMetadata metadata)
    {
        var filesForTx = _fileMetadata.GetOrAdd(transactionHash, _ => new ConcurrentDictionary<string, FileMetadata>());
        filesForTx[fileId] = metadata;
        _fileStoredAt[$"{transactionHash}:{fileId}"] = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<FileMetadata?> GetFileMetadataAsync(string transactionHash, string fileId)
    {
        if (_fileMetadata.TryGetValue(transactionHash, out var filesForTx))
        {
            filesForTx.TryGetValue(fileId, out var metadata);
            return Task.FromResult(metadata);
        }

        return Task.FromResult<FileMetadata?>(null);
    }

    public Task StoreFileContentAsync(string fileId, byte[] content)
    {
        _fileContent[fileId] = content;
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetFileContentAsync(string fileId)
    {
        _fileContent.TryGetValue(fileId, out var content);
        return Task.FromResult(content);
    }

    public Task<string?> GetByIdempotencyKeyAsync(string idempotencyKey)
    {
        if (_idempotencyKeys.TryGetValue(idempotencyKey, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
            {
                return Task.FromResult<string?>(entry.TransactionHash);
            }

            // Expired — remove and return null
            _idempotencyKeys.TryRemove(idempotencyKey, out _);
        }

        return Task.FromResult<string?>(null);
    }

    public Task StoreIdempotencyKeyAsync(string idempotencyKey, string transactionHash, TimeSpan ttl)
    {
        _idempotencyKeys[idempotencyKey] = (transactionHash, DateTime.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(string FileId, string TransactionHash)>> GetOrphanedFileMetadataAsync(
        DateTimeOffset olderThan)
    {
        var orphans = new List<(string FileId, string TransactionHash)>();

        foreach (var (txHash, files) in _fileMetadata)
        {
            // An orphan is a file whose parent action transaction does not exist in the store.
            if (_actions.ContainsKey(txHash))
                continue;

            foreach (var fileId in files.Keys)
            {
                var key = $"{txHash}:{fileId}";
                if (_fileStoredAt.TryGetValue(key, out var storedAt) && storedAt < olderThan)
                {
                    orphans.Add((fileId, txHash));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<(string, string)>>(orphans);
    }

    /// <inheritdoc/>
    public Task DeleteFileMetadataAsync(string fileId, string transactionHash)
    {
        if (_fileMetadata.TryGetValue(transactionHash, out var files))
        {
            files.TryRemove(fileId, out _);

            // Remove the parent bucket if it is now empty.
            if (files.IsEmpty)
                _fileMetadata.TryRemove(transactionHash, out _);
        }

        _fileContent.TryRemove(fileId, out _);
        _fileStoredAt.TryRemove($"{transactionHash}:{fileId}", out _);

        return Task.CompletedTask;
    }
}
