// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// In-memory implementation of instance storage.
/// Suitable for development and testing. Replace with persistent storage for production.
/// </summary>
public class InMemoryInstanceStore : IInstanceStore
{
    private readonly ConcurrentDictionary<string, Instance> _instances = new();

    /// <inheritdoc/>
    public Task<Instance> CreateAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instance.Id))
        {
            throw new ArgumentException("Instance ID is required", nameof(instance));
        }

        if (!_instances.TryAdd(instance.Id, instance))
        {
            throw new InvalidOperationException($"Instance {instance.Id} already exists");
        }

        return Task.FromResult(instance);
    }

    /// <inheritdoc/>
    public Task<Instance?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

    /// <inheritdoc/>
    public Task<Instance> UpdateAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instance.Id))
        {
            throw new ArgumentException("Instance ID is required", nameof(instance));
        }

        if (!_instances.TryGetValue(instance.Id, out var existing))
        {
            throw new InvalidOperationException($"Instance {instance.Id} not found");
        }

        // Optimistic concurrency: version must match
        if (existing.Version != instance.Version)
        {
            throw new ConcurrencyException(instance.Id, instance.Version, existing.Version);
        }

        instance.Version++;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        _instances[instance.Id] = instance;
        return Task.FromResult(instance);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<Instance>> GetByBlueprintAsync(
        string blueprintId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _instances.Values
            .Where(i => i.BlueprintId == blueprintId);

        if (state.HasValue)
        {
            query = query.Where(i => i.State == state.Value);
        }

        var result = query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<Instance>> GetByRegisterAsync(
        string registerId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _instances.Values
            .Where(i => i.RegisterId == registerId);

        if (state.HasValue)
        {
            query = query.Where(i => i.State == state.Value);
        }

        var result = query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<Instance>> GetByParticipantWalletAsync(
        string walletAddress,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _instances.Values
            .Where(i => i.ParticipantWallets.Values.Contains(walletAddress));

        if (state.HasValue)
        {
            query = query.Where(i => i.State == state.Value);
        }

        var result = query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<PendingActionSummary>> GetPendingActionsByWalletAsync(
        string walletAddress,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        // Only return actions where the wallet is the assigned participant for that action.
        // ParticipantWallets maps participant IDs to wallet addresses; we need to find
        // which participant ID this wallet owns, then only include actions targeting that participant.
        var results = _instances.Values
            .Where(i => i.State == InstanceState.Active)
            .Where(i => i.ParticipantWallets.Values.Contains(walletAddress))
            .SelectMany(i =>
            {
                // Find the participant ID(s) this wallet is assigned to
                var participantIds = i.ParticipantWallets
                    .Where(kv => kv.Value == walletAddress)
                    .Select(kv => kv.Key)
                    .ToHashSet();

                // Only include current actions — in the in-memory store we don't have
                // per-action participant assignment, so we include actions where this wallet
                // is a participant in the instance. This is a best-effort filter; production
                // storage will join with blueprint action definitions for exact matching.
                return i.CurrentActionIds.Select(actionId => new PendingActionSummary
                {
                    InstanceId = i.Id,
                    ActionId = actionId,
                    ActionTitle = $"Action {actionId}",
                    BlueprintId = i.BlueprintId,
                    BlueprintTitle = i.Metadata.GetValueOrDefault("BlueprintTitle", i.BlueprintId),
                    RegisterId = i.RegisterId,
                    TransactionId = i.LastTransactionId ?? string.Empty,
                    NavigationPath = $"/blueprints/{i.BlueprintId}/instances/{i.Id}/actions/{actionId}",
                    ReceivedAt = i.UpdatedAt
                });
            })
            .OrderByDescending(s => s.ReceivedAt)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(results);
    }

    /// <inheritdoc/>
    public Task<int> GetPendingActionCountByWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        var count = _instances.Values
            .Where(i => i.State == InstanceState.Active)
            .Where(i => i.ParticipantWallets.Values.Contains(walletAddress))
            .Sum(i => i.CurrentActionIds.Count);

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_instances.TryRemove(instanceId, out _));
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_instances.Count);
    }

    /// <inheritdoc/>
    public Task<int> CountByStateAsync(InstanceState state, CancellationToken cancellationToken = default)
    {
        var count = _instances.Values.Count(i => i.State == state);
        return Task.FromResult(count);
    }
}
