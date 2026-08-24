// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;

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
    /// <param name="scopeFactory">Service scope factory for resolving scoped services (e.g., IActionResolverService) from this singleton.</param>
    public EfCoreInstanceStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreInstanceStore> logger,
        IServiceScopeFactory scopeFactory)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
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
        entity.BlueprintDefinitionTxId = instance.BlueprintDefinitionTxId;
        entity.RegisterId = instance.RegisterId;
        entity.State = instance.State;
        entity.CurrentActionIds = SerializeJson(instance.CurrentActionIds);
        entity.ParticipantWallets = SerializeJson(instance.ParticipantWallets);
        entity.FirstTransactionId = instance.FirstTransactionId;
        entity.LastTransactionId = instance.LastTransactionId;
        entity.CompletedActionCount = instance.CompletedActionCount;
        entity.AccumulatedData = SerializeJson(instance.AccumulatedData);
        entity.PendingActionPayloads = SerializePendingActionPayloads(instance.PendingActionPayloads);
        entity.ActiveBranches = SerializeJson(instance.ActiveBranches);
        entity.Metadata = SerializeMetadataWithTenant(instance);
        entity.Version = instance.Version;
        entity.UpdatedAt = instance.UpdatedAt;
        entity.CompletedAt = instance.CompletedAt;
        // Feature 145 projection watermark. Omitted from this copy list until 2026-07-30, which cost
        // nothing visible: InstanceProjection sets it on the line after LastTransactionId, so the
        // instance still advanced correctly and only this column stayed NULL. Two things depended on
        // it and both failed open — the projector's replay guard (it re-reads the instance before
        // every fold, so an already-folded tx was never recognised) and Feature 142's rehearsal wait
        // (it matches on this watermark, so the go-live gate could never be earned).
        entity.LastAppliedTxId = instance.LastAppliedTxId;
        // Feature 186 projected decision. Assigned unconditionally so a clear (both null) persists —
        // an application refused on one branch and then advanced on another must not keep the reason.
        entity.DecisionRouteId = instance.DecisionRouteId;
        entity.DecisionReasonCode = instance.DecisionReasonCode;

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

        // Build blueprint cache for action title lookup.
        // IActionResolverService is scoped; resolve via scope factory since this store is singleton.
        var blueprintCache = new Dictionary<string, Sorcha.Blueprint.Models.Blueprint?>();
        IActionResolverService? actionResolver = null;

        var matchingInstances = entities
            .Where(e => ContainsWalletAddress(e.ParticipantWallets, walletAddress))
            .Select(ToModel)
            .Where(i => i is not null)
            .Cast<Instance>()
            .ToList();

        // Pre-fetch blueprints for all unique blueprint IDs
        await using var scope = _scopeFactory.CreateAsyncScope();
        actionResolver = scope.ServiceProvider.GetService<IActionResolverService>();
        if (actionResolver != null)
        {
            // Feature 195 — keyed by (blueprintId, pin). Two instances of one blueprint can be
            // running DIFFERENT definitions, and this cache feeds the decision-notice wording each
            // applicant is shown (F186). Keyed by blueprint id alone it would show one applicant the
            // reason text from another applicant's definition.
            foreach (var pinned in matchingInstances
                .Select(i => (i.BlueprintId, i.BlueprintDefinitionTxId))
                .Distinct())
            {
                var key = $"{pinned.BlueprintId}:{pinned.BlueprintDefinitionTxId}";
                if (!blueprintCache.ContainsKey(key))
                {
                    blueprintCache[key] = string.IsNullOrWhiteSpace(pinned.BlueprintDefinitionTxId)
                        ? null   // pre-feature instance: no pin, so no definition to resolve
                        : await actionResolver.GetBlueprintAsync(
                            pinned.BlueprintId, pinned.BlueprintDefinitionTxId, cancellationToken);
                }
            }
        }

        var summaries = matchingInstances
            .SelectMany(instance =>
            {
                blueprintCache.TryGetValue($"{instance.BlueprintId}:{instance.BlueprintDefinitionTxId}", out var blueprint);

                // Only surface actions this wallet is actually the sender of — not every current
                // action of an instance the wallet merely participates in (the citizen-sees-the-
                // analyst's-action bug). See IsActionForWallet.
                return instance.CurrentActionIds
                    .Where(actionId => IsActionForWallet(blueprint, actionResolver, instance, actionId, walletAddress))
                    .Select(actionId =>
                {
                    var actionTitle = $"Action {actionId}";
                    JsonElement? dataSchema = null;

                    if (blueprint != null && actionResolver != null)
                    {
                        var actionDef = actionResolver.GetActionDefinition(blueprint, actionId.ToString());
                        if (actionDef != null)
                        {
                            if (!string.IsNullOrEmpty(actionDef.Title))
                            {
                                actionTitle = actionDef.Title;
                            }

                            // Surface the first declared DataSchema so the client-side
                            // pending-actions dispatcher can detect Feature 104 wave 14b
                            // claim actions via the x-credential-offer schema extension.
                            // Without this the CredentialOfferSchemaResolver short-circuits
                            // on a null schema and the UI falls through to a generic
                            // empty form. Wave 14b shipped this gap — fixing it here so
                            // the claim card finally renders in the browser.
                            //
                            // Actions may declare multiple schemas, but the client-side
                            // CredentialOfferSchemaResolver only inspects the first to
                            // detect the x-credential-offer extension. Multi-schema
                            // actions are not a wave 14b use-case; revisit this
                            // projection if that assumption ever changes.
                            //
                            // RootElement.Clone() is required — JsonElement shares memory
                            // with its parent JsonDocument and becomes invalid once the
                            // document is disposed. Without Clone() this would corrupt
                            // at runtime in a hard-to-reproduce way.
                            var firstSchemaDoc = actionDef.DataSchemas?.FirstOrDefault();
                            if (firstSchemaDoc is not null)
                            {
                                dataSchema = firstSchemaDoc.RootElement.Clone();
                            }
                        }
                    }

                    // Surface any prepopulated seed payload for this action so
                    // the UI can render it without a second round trip.
                    // Feature 104 wave 14a (FR-006).
                    instance.PendingActionPayloads.TryGetValue(actionId, out var seededPayload);

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
                        ReceivedAt = instance.UpdatedAt,
                        PrepopulatedPayload = seededPayload,
                        DataSchema = dataSchema
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

        var matchingInstances = entities
            .Where(e => ContainsWalletAddress(e.ParticipantWallets, walletAddress))
            .Select(ToModel)
            .Where(i => i is not null)
            .Cast<Instance>()
            .ToList();

        // Resolve blueprints to apply the same sender-filter as GetPendingActionsByWalletAsync, so
        // the badge count matches the list (count only actions this wallet is the sender of).
        var blueprintCache = new Dictionary<string, Sorcha.Blueprint.Models.Blueprint?>();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var actionResolver = scope.ServiceProvider.GetService<IActionResolverService>();
        if (actionResolver != null)
        {
            // Feature 195 — keyed by (blueprintId, pin); see the sibling loop above.
            foreach (var pinned in matchingInstances
                .Select(i => (i.BlueprintId, i.BlueprintDefinitionTxId))
                .Distinct())
            {
                var key = $"{pinned.BlueprintId}:{pinned.BlueprintDefinitionTxId}";
                if (!blueprintCache.ContainsKey(key))
                {
                    blueprintCache[key] = string.IsNullOrWhiteSpace(pinned.BlueprintDefinitionTxId)
                        ? null
                        : await actionResolver.GetBlueprintAsync(
                            pinned.BlueprintId, pinned.BlueprintDefinitionTxId, cancellationToken);
                }
            }
        }

        return matchingInstances.Sum(instance =>
        {
            blueprintCache.TryGetValue($"{instance.BlueprintId}:{instance.BlueprintDefinitionTxId}", out var blueprint);
            return instance.CurrentActionIds.Count(actionId =>
                IsActionForWallet(blueprint, actionResolver, instance, actionId, walletAddress));
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

    /// <summary>
    /// Issue #1350 — the metadata key <see cref="Instance.TenantId"/> is carried in.
    /// <see cref="InstanceEntity"/> has no TenantId column, and <see cref="ToModel"/> has always read
    /// the value back out of metadata — but nothing ever wrote it, so every instance loaded from
    /// Postgres had <c>TenantId == ""</c> despite the model declaring the property <c>required</c>.
    /// </summary>
    private const string TenantIdMetadataKey = "TenantId";

    /// <summary>
    /// Serialises an instance's metadata with <see cref="Instance.TenantId"/> folded in under
    /// <see cref="TenantIdMetadataKey"/>, closing #1350 with no schema change — adding a real column
    /// would need a migration, which under this repo's squashed-migration rule is a deployment
    /// decision (DB recreate) rather than a code change.
    ///
    /// <para>The caller's own dictionary is never mutated: the key is merged into a copy, so an
    /// <c>Instance</c> handed to the store does not silently grow a metadata entry.</para>
    /// </summary>
    private static string SerializeMetadataWithTenant(Instance instance)
    {
        var metadata = new Dictionary<string, string>(instance.Metadata)
        {
            [TenantIdMetadataKey] = instance.TenantId,
        };

        return SerializeJson(metadata);
    }

    private static InstanceEntity ToEntity(Instance instance)
    {
        return new InstanceEntity
        {
            Id = instance.Id,
            BlueprintId = instance.BlueprintId,
            BlueprintVersion = instance.BlueprintVersion,
            BlueprintDefinitionTxId = instance.BlueprintDefinitionTxId,
            RegisterId = instance.RegisterId,
            State = instance.State,
            CurrentActionIds = SerializeJson(instance.CurrentActionIds),
            ParticipantWallets = SerializeJson(instance.ParticipantWallets),
            FirstTransactionId = instance.FirstTransactionId,
            LastTransactionId = instance.LastTransactionId,
            CompletedActionCount = instance.CompletedActionCount,
            AccumulatedData = SerializeJson(instance.AccumulatedData),
            PendingActionPayloads = SerializePendingActionPayloads(instance.PendingActionPayloads),
            ActiveBranches = SerializeJson(instance.ActiveBranches),
            Metadata = SerializeMetadataWithTenant(instance),
            Version = instance.Version,
            CreatedAt = instance.CreatedAt != default ? instance.CreatedAt : DateTimeOffset.UtcNow,
            UpdatedAt = instance.UpdatedAt != default ? instance.UpdatedAt : DateTimeOffset.UtcNow,
            CompletedAt = instance.CompletedAt,
            LastAppliedTxId = instance.LastAppliedTxId,
            DecisionRouteId = instance.DecisionRouteId,
            DecisionReasonCode = instance.DecisionReasonCode,
        };
    }

    private Instance? ToModel(InstanceEntity entity)
    {
        try
        {
            var metadata = DeserializeJson<Dictionary<string, string>>(entity.Metadata)
                           ?? new Dictionary<string, string>();

            // Issue #1350 — TenantId travels inside Metadata (InstanceEntity has no column for it).
            // Removing the key after reading it is load-bearing, not tidiness: Metadata is handed
            // back to the caller wholesale below, so leaving the key in would surface a phantom
            // entry the caller never wrote and break Metadata's own round-trip.
            var tenantId = metadata.GetValueOrDefault(TenantIdMetadataKey, string.Empty);
            metadata.Remove(TenantIdMetadataKey);

            return new Instance
            {
                Id = entity.Id,
                BlueprintId = entity.BlueprintId,
                BlueprintVersion = entity.BlueprintVersion,
                BlueprintDefinitionTxId = entity.BlueprintDefinitionTxId ?? string.Empty,
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
                PendingActionPayloads = DeserializePendingActionPayloads(entity.PendingActionPayloads),
                ActiveBranches = DeserializeJson<List<Branch>>(entity.ActiveBranches) ?? [],
                Metadata = metadata,
                Version = entity.Version,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                CompletedAt = entity.CompletedAt,
                LastAppliedTxId = entity.LastAppliedTxId,
                DecisionRouteId = entity.DecisionRouteId,
                DecisionReasonCode = entity.DecisionReasonCode,
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
    /// Serialises the <see cref="Instance.PendingActionPayloads"/> dictionary
    /// to a JSON object whose property names are the integer action IDs.
    /// Returns null when the dictionary is null or empty so that the JSONB
    /// column stores SQL NULL rather than "{}". Feature 104 wave 14a.
    /// </summary>
    private static string? SerializePendingActionPayloads(Dictionary<int, JsonObject>? payloads)
    {
        if (payloads is null || payloads.Count == 0)
        {
            return null;
        }

        var obj = new JsonObject();
        foreach (var (actionId, value) in payloads)
        {
            // Deep-clone via round-trip so the serialised form is not coupled to the caller's node
            var cloned = value is null ? null : JsonNode.Parse(value.ToJsonString());
            obj[actionId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = cloned;
        }

        return obj.ToJsonString();
    }

    /// <summary>
    /// Deserialises <see cref="Instance.PendingActionPayloads"/> from JSONB.
    /// Expects an object whose property names are integer action IDs. Unknown
    /// or non-integer keys are ignored. Feature 104 wave 14a.
    /// </summary>
    private Dictionary<int, JsonObject> DeserializePendingActionPayloads(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<int, JsonObject>();
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                return new Dictionary<int, JsonObject>();
            }

            var result = new Dictionary<int, JsonObject>(capacity: obj.Count);
            foreach (var kvp in obj)
            {
                if (!int.TryParse(kvp.Key, System.Globalization.CultureInfo.InvariantCulture, out var actionId))
                {
                    continue;
                }
                if (kvp.Value is not JsonObject value)
                {
                    continue;
                }
                // Clone so the caller cannot mutate the parsed tree and affect other readers
                var cloned = JsonNode.Parse(value.ToJsonString()) as JsonObject;
                if (cloned != null)
                {
                    result[actionId] = cloned;
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            // Do NOT log the raw JSON — wave 14b will persist short-lived
            // OpenID4VCI pre_authorized_code values here and we must not leak
            // them to log sinks on parse failure. Log length only.
            _logger.LogWarning(
                ex,
                "Failed to deserialize PendingActionPayloads JSON (length {Length})",
                json.Length);
            return new Dictionary<int, JsonObject>();
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

    /// <summary>
    /// Whether a current action is actionable by <paramref name="walletAddress"/> — i.e. the
    /// action's <c>Sender</c> participant either binds to that wallet in the instance, or is not
    /// yet bound to any wallet (open / late-bound, ambiguous). It excludes ONLY actions whose
    /// sender is bound to a <b>different</b> wallet, so a participant who merely shares an instance
    /// (e.g. the citizen applicant whose wallet is in <see cref="Instance.ParticipantWallets"/>
    /// alongside the analyst's) is no longer shown the analyst's action as if it were their own.
    /// Falls back to inclusive whenever the blueprint / action / sender can't be resolved, so a
    /// resolution failure never hides a legitimate pending action from the actor who must perform it.
    /// </summary>
    internal static bool IsActionForWallet(
        Sorcha.Blueprint.Models.Blueprint? blueprint,
        IActionResolverService? actionResolver,
        Instance instance,
        int actionId,
        string walletAddress)
    {
        if (blueprint is null || actionResolver is null)
        {
            return true;
        }

        var sender = actionResolver.GetActionDefinition(blueprint, actionId.ToString())?.Sender;
        if (string.IsNullOrEmpty(sender))
        {
            return true;
        }

        if (instance.ParticipantWallets.TryGetValue(sender, out var senderWallet)
            && !string.IsNullOrEmpty(senderWallet))
        {
            // Sender is bound to a wallet — the action belongs to that wallet only.
            return string.Equals(senderWallet, walletAddress, StringComparison.OrdinalIgnoreCase);
        }

        // Sender not yet bound (open / late-bound participant) — ambiguous, don't hide it.
        return true;
    }
}
