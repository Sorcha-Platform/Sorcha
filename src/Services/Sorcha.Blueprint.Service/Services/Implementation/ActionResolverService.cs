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
    private readonly IPublishedBlueprintStore _publishedBlueprintStore;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ActionResolverService> _logger;
    private const int CacheTtlMinutes = 10;

    // Feature 195 — the static per-blueprint action-index cache is REMOVED.
    //
    // It was a ConcurrentDictionary keyed by bare blueprint id, read by GetActionDefinition, which
    // receives an already-resolved blueprint and therefore had no way to say WHICH definition it
    // wanted. With two definitions of one blueprint in play it returned the index of whichever
    // populated the entry first — a process-wide cache serving one instance's actions to another,
    // silently, and only when two definitions coexist (i.e. only once version pinning works).
    //
    // Keying it by definition would have fixed the write side while leaving the read side unable to
    // supply the key. The index is a dictionary over an action list already in hand, so building it
    // per call removes the hazard entirely and the "optimisation" it replaced was never measured.

    /// <summary>Initialises a new instance of the <see cref="ActionResolverService"/> class.</summary>
    public ActionResolverService(
        IBlueprintStore blueprintStore,
        IPublishedBlueprintStore publishedBlueprintStore,
        IDistributedCache cache,
        ILogger<ActionResolverService> logger)
    {
        _blueprintStore = blueprintStore ?? throw new ArgumentNullException(nameof(blueprintStore));
        _publishedBlueprintStore = publishedBlueprintStore ?? throw new ArgumentNullException(nameof(publishedBlueprintStore));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> GetBlueprintAsync(
        string blueprintId,
        string definitionTxId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            throw new ArgumentException("Blueprint ID cannot be null or empty", nameof(blueprintId));
        }

        if (string.IsNullOrWhiteSpace(definitionTxId))
        {
            // Refuse rather than fall back. Resolving "latest" for an instance whose pin we do not
            // know is precisely the defect Feature 194 exists to remove, and it fails silently: the
            // payload validates against the wrong rules and the workflow takes the wrong route.
            throw new ArgumentException(
                "A definition pin is required to resolve a blueprint for execution. Resolving the " +
                "latest definition instead would validate the submission against rules the instance " +
                "never agreed to run.",
                nameof(definitionTxId));
        }

        // Keyed by definition, not by blueprint. An entry addressed by content is immutable, so
        // several definitions of one blueprint coexist and a pinned instance resolves its own.
        var cacheKey = $"blueprint:{blueprintId}:{definitionTxId}";

        var cachedBlueprint = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cachedBlueprint))
        {
            _logger.LogDebug(
                "Definition {DefinitionTxId} of blueprint {BlueprintId} retrieved from cache",
                definitionTxId, blueprintId);
            return JsonSerializer.Deserialize<BlueprintModel>(cachedBlueprint);
        }

        // The published store only. The DRAFT store is deliberately absent: a draft is unpublished
        // work-in-progress on one node, and letting it reach the execution path is what allowed a
        // submission to be judged by a definition its instance never ran.
        var versions = await _publishedBlueprintStore.GetVersionsAsync(blueprintId);
        var blueprint = versions
            .FirstOrDefault(v => string.Equals(v.PublicationTxId, definitionTxId, StringComparison.OrdinalIgnoreCase))
            ?.Blueprint;

        if (blueprint == null)
        {
            _logger.LogError(
                "Definition {DefinitionTxId} of blueprint {BlueprintId} is UNRESOLVABLE on this node. " +
                "The submission will be refused rather than judged against a different definition.",
                definitionTxId, blueprintId);
            return null;
        }

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheTtlMinutes)
        };
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(blueprint),
            cacheOptions,
            cancellationToken);

        _logger.LogDebug(
            "Definition {DefinitionTxId} of blueprint {BlueprintId} cached for {Minutes} minutes",
            definitionTxId, blueprintId, CacheTtlMinutes);

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

        // Built from the blueprint in hand — the ONLY definition this caller means. See the note
        // where the former static index cache was removed.
        var actionIndex = blueprint.BuildActionIndex();

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
