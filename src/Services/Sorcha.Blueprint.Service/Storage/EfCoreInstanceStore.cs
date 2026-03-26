// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IInstanceStore"/>.
/// Registered as a singleton; uses <see cref="IDbContextFactory{TContext}"/>
/// to create scoped <see cref="BlueprintDbContext"/> instances per operation.
/// Implements optimistic concurrency via the Version column configured as a
/// concurrency token in <see cref="BlueprintDbContext"/>.
/// </summary>
public class EfCoreInstanceStore : IInstanceStore
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCoreInstanceStore> _logger;
    private readonly IActionResolverService _actionResolver;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreInstanceStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="actionResolver">Action resolver for enriching pending action data.</param>
    public EfCoreInstanceStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreInstanceStore> logger,
        IActionResolverService actionResolver)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _actionResolver = actionResolver;
    }

    /// <inheritdoc/>
    public async Task<Instance> CreateAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instance.Id))
        {
            throw new ArgumentException("Instance ID is required", nameof(instance));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = ToEntity(instance);
        context.Instances.Add(entity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate") == true
                                            || ex.InnerException?.Message.Contains("unique") == true)
        {
            throw new InvalidOperationException($"Instance {instance.Id} already exists", ex);
        }

        _logger.LogInformation("Created instance {InstanceId} for blueprint {BlueprintId}",
            instance.Id, instance.BlueprintId);

        return instance;
    }

    /// <inheritdoc/>
    public async Task<Instance?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Instances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc/>
    public async Task<Instance> UpdateAsync(Instance instance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instance.Id))
        {
            throw new ArgumentException("Instance ID is required", nameof(instance));
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Instances
            .FirstOrDefaultAsync(i => i.Id == instance.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Instance {instance.Id} not found");
        }

        // Optimistic concurrency check (manual, supplementing EF Core's concurrency token)
        var expectedVersion = instance.Version;
        if (entity.Version != expectedVersion)
        {
            throw new ConcurrencyException(instance.Id, expectedVersion, entity.Version);
        }

        // Increment version and update timestamp
        instance.Version = expectedVersion + 1;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        // Map updated model back to entity
        entity.BlueprintId = instance.BlueprintId;
        entity.BlueprintVersion = instance.BlueprintVersion;
        entity.RegisterId = instance.RegisterId;
        entity.State = instance.State;
        entity.CurrentActionIds = SerializeJson(instance.CurrentActionIds);
        entity.ParticipantWallets = SerializeJson(instance.ParticipantWallets);
        entity.FirstTransactionId = instance.FirstTransactionId;
        entity.LastTransactionId = instance.LastTransactionId;
        entity.CompletedActionCount = instance.CompletedActionCount;
        entity.AccumulatedData = SerializeJson(instance.AccumulatedData);
        entity.ActiveBranches = SerializeJson(instance.ActiveBranches);
        entity.Metadata = SerializeJson(instance.Metadata);
        entity.Version = instance.Version;
        entity.UpdatedAt = instance.UpdatedAt;
        entity.CompletedAt = instance.CompletedAt;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict updating instance {InstanceId}", instance.Id);
            throw new ConcurrencyException(instance.Id, expectedVersion, entity.Version);
        }

        _logger.LogInformation("Updated instance {InstanceId} to version {Version}",
            instance.Id, instance.Version);

        return instance;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Instance>> GetByBlueprintAsync(
        string blueprintId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Instances.AsNoTracking()
            .Where(i => i.BlueprintId == blueprintId);

        if (state.HasValue)
        {
            var stateFilter = state.Value;
            query = query.Where(i => i.State == stateFilter);
        }

        var entities = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).Where(i => i is not null).Cast<Instance>();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Instance>> GetByRegisterAsync(
        string registerId,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Instances.AsNoTracking()
            .Where(i => i.RegisterId == registerId);

        if (state.HasValue)
        {
            var stateFilter = state.Value;
            query = query.Where(i => i.State == stateFilter);
        }

        var entities = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).Where(i => i is not null).Cast<Instance>();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Instance>> GetByParticipantWalletAsync(
        string walletAddress,
        InstanceState? state = null,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Query all instances (with optional state filter), then filter in-memory
        // by deserializing ParticipantWallets JSON. This is O(n) but acceptable for MVP.
        var query = context.Instances.AsNoTracking().AsQueryable();

        if (state.HasValue)
        {
            var stateFilter = state.Value;
            query = query.Where(i => i.State == stateFilter);
        }

        var entities = await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities
            .Where(e => ContainsWalletAddress(e.ParticipantWallets, walletAddress))
            .Skip(skip)
            .Take(take)
            .Select(ToModel)
            .Where(i => i is not null)
            .Cast<Instance>()
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<PendingActionSummary>> GetPendingActionsByWalletAsync(
        string walletAddress,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activeState = InstanceState.Active;

        var entities = await context.Instances.AsNoTracking()
            .Where(i => i.State == activeState)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(cancellationToken);

        // Build blueprint cache for action title lookup
        var blueprintCache = new Dictionary<string, Sorcha.Blueprint.Models.Blueprint?>();

        var matchingInstances = entities
            .Where(e => ContainsWalletAddress(e.ParticipantWallets, walletAddress))
            .Select(ToModel)
            .Where(i => i is not null)
            .Cast<Instance>()
            .ToList();

        // Pre-fetch blueprints for all unique blueprint IDs
        var blueprintIds = matchingInstances.Select(i => i.BlueprintId).Distinct();
        foreach (var bpId in blueprintIds)
        {
            if (!blueprintCache.ContainsKey(bpId))
            {
                blueprintCache[bpId] = await _actionResolver.GetBlueprintAsync(bpId, cancellationToken);
            }
        }

        var summaries = matchingInstances
            .SelectMany(instance =>
            {
                blueprintCache.TryGetValue(instance.BlueprintId, out var blueprint);

                return instance.CurrentActionIds.Select(actionId =>
                {
                    var actionTitle = $"Action {actionId}";
                    if (blueprint != null)
                    {
                        var actionDef = _actionResolver.GetActionDefinition(blueprint, actionId.ToString());
                        if (actionDef != null && !string.IsNullOrEmpty(actionDef.Title))
                        {
                            actionTitle = actionDef.Title;
                        }
                    }

                    return new PendingActionSummary
                    {
                        InstanceId = instance.Id,
                        ActionId = actionId,
                        ActionTitle = actionTitle,
                        BlueprintId = instance.BlueprintId,
                        BlueprintTitle = instance.Metadata.GetValueOrDefault("BlueprintTitle", instance.BlueprintId),
                        InstanceReference = instance.Metadata.GetValueOrDefault("instanceReference", string.Empty),
                        RegisterId = instance.RegisterId,
                        TransactionId = instance.LastTransactionId ?? string.Empty,
                        NavigationPath = $"/blueprints/{instance.BlueprintId}/instances/{instance.Id}/actions/{actionId}",
                        ReceivedAt = instance.UpdatedAt
                    };
                });
            })
            .OrderByDescending(s => s.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToList();

        return summaries;
    }

    /// <inheritdoc/>
    public async Task<int> GetPendingActionCountByWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activeState = InstanceState.Active;

        var entities = await context.Instances.AsNoTracking()
            .Where(i => i.State == activeState)
            .ToListAsync(cancellationToken);

        return entities
            .Where(e => ContainsWalletAddress(e.ParticipantWallets, walletAddress))
            .Sum(e =>
            {
                var actionIds = DeserializeJson<List<int>>(e.CurrentActionIds);
                return actionIds?.Count ?? 0;
            });
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        context.Instances.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted instance {InstanceId}", instanceId);

        return true;
    }

    /// <inheritdoc/>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Instances.CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> CountByStateAsync(InstanceState state, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Instances.CountAsync(i => i.State == state, cancellationToken);
    }

    private static InstanceEntity ToEntity(Instance instance)
    {
        return new InstanceEntity
        {
            Id = instance.Id,
            BlueprintId = instance.BlueprintId,
            BlueprintVersion = instance.BlueprintVersion,
            RegisterId = instance.RegisterId,
            State = instance.State,
            CurrentActionIds = SerializeJson(instance.CurrentActionIds),
            ParticipantWallets = SerializeJson(instance.ParticipantWallets),
            FirstTransactionId = instance.FirstTransactionId,
            LastTransactionId = instance.LastTransactionId,
            CompletedActionCount = instance.CompletedActionCount,
            AccumulatedData = SerializeJson(instance.AccumulatedData),
            ActiveBranches = SerializeJson(instance.ActiveBranches),
            Metadata = SerializeJson(instance.Metadata),
            Version = instance.Version,
            CreatedAt = instance.CreatedAt != default ? instance.CreatedAt : DateTimeOffset.UtcNow,
            UpdatedAt = instance.UpdatedAt != default ? instance.UpdatedAt : DateTimeOffset.UtcNow,
            CompletedAt = instance.CompletedAt
        };
    }

    private Instance? ToModel(InstanceEntity entity)
    {
        try
        {
            var metadata = DeserializeJson<Dictionary<string, string>>(entity.Metadata)
                           ?? new Dictionary<string, string>();

            // Extract TenantId from metadata if stored there; default to empty
            var tenantId = metadata.GetValueOrDefault("TenantId", string.Empty);

            return new Instance
            {
                Id = entity.Id,
                BlueprintId = entity.BlueprintId,
                BlueprintVersion = entity.BlueprintVersion,
                RegisterId = entity.RegisterId,
                TenantId = tenantId,
                State = entity.State,
                CurrentActionIds = DeserializeJson<List<int>>(entity.CurrentActionIds) ?? [],
                ParticipantWallets = DeserializeJson<Dictionary<string, string>>(entity.ParticipantWallets)
                                     ?? new Dictionary<string, string>(),
                FirstTransactionId = entity.FirstTransactionId,
                LastTransactionId = entity.LastTransactionId,
                CompletedActionCount = entity.CompletedActionCount,
                AccumulatedData = DeserializeJson<Dictionary<string, object>>(entity.AccumulatedData)
                                  ?? new Dictionary<string, object>(),
                ActiveBranches = DeserializeJson<List<Branch>>(entity.ActiveBranches) ?? [],
                Metadata = metadata,
                Version = entity.Version,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                CompletedAt = entity.CompletedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize instance {InstanceId}", entity.Id);
            return null;
        }
    }

    private static string? SerializeJson<T>(T? value)
    {
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    private T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON: {Json}", json);
            return default;
        }
    }

    /// <summary>
    /// Checks if the serialized ParticipantWallets JSON contains the given wallet address
    /// as a value. Uses lightweight deserialization to avoid full model mapping.
    /// </summary>
    private bool ContainsWalletAddress(string? participantWalletsJson, string walletAddress)
    {
        if (string.IsNullOrEmpty(participantWalletsJson))
        {
            return false;
        }

        try
        {
            var wallets = JsonSerializer.Deserialize<Dictionary<string, string>>(
                participantWalletsJson, SerializerOptions);

            return wallets?.Values.Contains(walletAddress) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
