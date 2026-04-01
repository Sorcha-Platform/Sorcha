// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using Sorcha.Blueprint.Engine;
using Sorcha.Blueprint.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for resolving blueprints and actions.
/// Caches both blueprints and their action indexes for O(1) lookups.
/// </summary>
public class ActionResolverService : IActionResolverService
{
    private readonly IBlueprintStore _blueprintStore;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ActionResolverService> _logger;
    private const int CacheTtlMinutes = 10;

    /// <summary>
    /// Per-blueprint action index cache. Evicted when blueprint is re-fetched.
    /// Static so it survives scoped DI lifetimes.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Dictionary<int, ActionModel>> _actionIndexCache = new();

    /// <summary>Initialises a new instance of the <see cref="ActionResolverService"/> class.</summary>
    public ActionResolverService(
        IBlueprintStore blueprintStore,
        IDistributedCache cache,
        ILogger<ActionResolverService> logger)
    {
        _blueprintStore = blueprintStore ?? throw new ArgumentNullException(nameof(blueprintStore));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> GetBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            throw new ArgumentException("Blueprint ID cannot be null or empty", nameof(blueprintId));
        }

        // Try to get from cache first
        var cacheKey = $"blueprint:{blueprintId}";
        var cachedBlueprint = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrEmpty(cachedBlueprint))
        {
            _logger.LogDebug("Blueprint {BlueprintId} retrieved from cache", blueprintId);
            var cached = JsonSerializer.Deserialize<BlueprintModel>(cachedBlueprint);
            if (cached != null)
            {
                // Ensure action index is also cached
                _actionIndexCache.GetOrAdd(blueprintId, _ => cached.BuildActionIndex());
            }
            return cached;
        }

        // Get from store
        var blueprint = await _blueprintStore.GetAsync(blueprintId);
        if (blueprint == null)
        {
            _logger.LogWarning("Blueprint {BlueprintId} not found", blueprintId);
            return null;
        }

        // Cache for future requests
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(blueprint),
            cacheOptions,
            cancellationToken);

        // Pre-compute action index alongside blueprint cache
        _actionIndexCache[blueprintId] = blueprint.BuildActionIndex();

        _logger.LogDebug("Blueprint {BlueprintId} cached for {Minutes} minutes", blueprintId, CacheTtlMinutes);

        return blueprint;
    }

    /// <inheritdoc/>
    public ActionModel? GetActionDefinition(BlueprintModel blueprint, string actionId)
    {
        if (blueprint == null)
        {
            throw new ArgumentNullException(nameof(blueprint));
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            throw new ArgumentException("Action ID cannot be null or empty", nameof(actionId));
        }

        // Action.Id is an int, so parse the actionId string
        if (!int.TryParse(actionId, out var actionIdInt))
        {
            _logger.LogWarning("Invalid action ID format: {ActionId}", actionId);
            return null;
        }

        // Use cached action index (O(1) lookup) — falls back to building on demand
        var actionIndex = _actionIndexCache.GetOrAdd(
            blueprint.Id ?? "",
            _ => blueprint.BuildActionIndex());

        if (!actionIndex.TryGetValue(actionIdInt, out var action))
        {
            _logger.LogWarning("Action {ActionId} not found in blueprint {BlueprintId}", actionId, blueprint.Id);
            return null;
        }

        return action;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> ResolveParticipantWalletsAsync(
        BlueprintModel blueprint,
        IEnumerable<string> participantIds,
        CancellationToken cancellationToken = default)
    {
        if (blueprint == null)
        {
            throw new ArgumentNullException(nameof(blueprint));
        }

        if (participantIds == null)
        {
            throw new ArgumentNullException(nameof(participantIds));
        }

        var walletMap = new Dictionary<string, string>();
        var participantIdList = participantIds.ToList();

        foreach (var participantId in participantIdList)
        {
            var participant = blueprint.Participants?.FirstOrDefault(p => p.Id == participantId);
            if (participant == null)
            {
                _logger.LogWarning("Participant {ParticipantId} not found in blueprint {BlueprintId}", participantId, blueprint.Id);
                continue;
            }

            // For now, we'll use a placeholder wallet resolution
            // In the future, this would call a Participant/Wallet service to resolve actual wallet addresses
            // For MVP, participants are expected to have wallet addresses in metadata or properties
            var walletAddress = ResolveWalletFromParticipant(participant);
            if (!string.IsNullOrEmpty(walletAddress))
            {
                walletMap[participantId] = walletAddress;
            }
            else
            {
                _logger.LogWarning("Could not resolve wallet for participant {ParticipantId}", participantId);
            }
        }

        await Task.CompletedTask; // For async signature compatibility
        return walletMap;
    }

    private string? ResolveWalletFromParticipant(Sorcha.Blueprint.Models.Participant participant)
    {
        // Return the wallet address from the participant if available
        if (!string.IsNullOrWhiteSpace(participant.WalletAddress))
        {
            return participant.WalletAddress;
        }

        // If no wallet address is set, log a warning
        _logger.LogWarning(
            "Participant {ParticipantId} ({ParticipantName}) does not have a wallet address configured",
            participant.Id,
            participant.Name);

        // Return null to indicate no wallet available
        // The calling code should handle this appropriately
        return null;
    }
}
