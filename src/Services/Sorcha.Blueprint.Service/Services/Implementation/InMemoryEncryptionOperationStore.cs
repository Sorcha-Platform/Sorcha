// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// In-memory store for tracking encryption operation status.
/// Completed operations are cleaned up after 1 hour.
/// </summary>
public sealed class InMemoryEncryptionOperationStore : IEncryptionOperationStore, IDisposable
{
    private readonly ConcurrentDictionary<string, EncryptionOperation> _operations = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _retentionPeriod;
    private readonly ILogger<InMemoryEncryptionOperationStore> _logger;

    public InMemoryEncryptionOperationStore(ILogger<InMemoryEncryptionOperationStore> logger)
        : this(logger, TimeSpan.FromHours(1))
    {
    }

    /// <summary>
    /// Constructor with configurable retention period (for testing).
    /// </summary>
    internal InMemoryEncryptionOperationStore(ILogger<InMemoryEncryptionOperationStore> logger, TimeSpan retentionPeriod)
    {
        _logger = logger;
        _retentionPeriod = retentionPeriod;
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc/>
    public Task<EncryptionOperation> CreateAsync(EncryptionOperation operation)
    {
        _operations[operation.OperationId] = operation;
        _logger.LogDebug("Created encryption operation {OperationId} for wallet {Wallet}",
            operation.OperationId, operation.SubmittingWalletAddress);
        return Task.FromResult(operation);
    }

    /// <inheritdoc/>
    public Task<EncryptionOperation> UpdateAsync(EncryptionOperation operation)
    {
        _operations[operation.OperationId] = operation;
        return Task.FromResult(operation);
    }

    /// <inheritdoc/>
    public Task<EncryptionOperation?> GetByIdAsync(string operationId)
    {
        _operations.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation);
    }

    /// <inheritdoc/>
    public Task<EncryptionOperation?> GetByWalletAddressAsync(string walletAddress)
    {
        var operation = _operations.Values
            .Where(o => o.SubmittingWalletAddress == walletAddress)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(operation);
    }

    private void CleanupExpired(object? state)
    {
        var cutoff = DateTimeOffset.UtcNow - _retentionPeriod;
        var removed = 0;

        foreach (var kvp in _operations.Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoff))
        {
            if (_operations.TryRemove(kvp.Key, out _))
                removed++;
        }

        if (removed > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired encryption operations", removed);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
