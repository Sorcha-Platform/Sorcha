// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Shared implementation of the DAD disclosure model (Feature 176). The submit-side
/// <see cref="ApplyDisclosuresAsync"/> is the logic lifted verbatim from
/// <c>ActionExecutionService.ApplyDisclosuresAsync</c> (no behaviour change); the read-side
/// <see cref="ResolveDisclosedDataAsync"/> reconstructs the caller's disclosed prior-action view and
/// clamps it to the caller participant's entitlement so no undisclosed field is ever returned.
/// </summary>
public sealed class ActionDisclosureResolver : IActionDisclosureResolver
{
    private readonly IExecutionEngine _executionEngine;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<ActionDisclosureResolver> _logger;

    // Read-side dependencies. Optional so the execution pipeline can construct a resolver for the
    // submit-side primitive alone; the disclosed-data endpoint resolves the fully-wired singleton from
    // DI where all three are present.
    private readonly IStateReconstructionService? _stateReconstruction;
    private readonly IInstanceStore? _instanceStore;
    private readonly IBlueprintStore? _blueprintStore;

    /// <summary>Initialises a new instance of the <see cref="ActionDisclosureResolver"/> class.</summary>
    public ActionDisclosureResolver(
        IExecutionEngine executionEngine,
        IRegisterServiceClient registerClient,
        ILogger<ActionDisclosureResolver> logger,
        IStateReconstructionService? stateReconstruction = null,
        IInstanceStore? instanceStore = null,
        IBlueprintStore? blueprintStore = null)
    {
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateReconstruction = stateReconstruction;
        _instanceStore = instanceStore;
        _blueprintStore = blueprintStore;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, Dictionary<string, object>>> ApplyDisclosuresAsync(
        ActionModel action,
        Dictionary<string, object> data,
        BlueprintModel blueprint,
        Dictionary<string, string> participantWallets,
        string registerId,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the Blueprint Engine for JSON Pointer disclosure filtering
        var engineResults = _executionEngine.ApplyDisclosures(data, action);

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>();

        foreach (var result in engineResults)
        {
            // Resolve participant ID to wallet address (2-tier: instance bindings → register)
            var recipientAddress = result.ParticipantId;
            if (participantWallets.TryGetValue(recipientAddress, out var walletAddress))
            {
                recipientAddress = walletAddress;
            }
            else
            {
                // Tier 2: Try resolving from register participant index
                var participant = blueprint.Participants.FirstOrDefault(p =>
                    string.Equals(p.Id, recipientAddress, StringComparison.OrdinalIgnoreCase));

                if (participant != null)
                {
                    try
                    {
                        var resolvedRecord = await _registerClient.ResolveParticipantAsync(
                            registerId, participant.Id, participant.Organisation);

                        if (resolvedRecord?.Addresses.Count > 0)
                        {
                            var primaryAddr = resolvedRecord.Addresses.FirstOrDefault(a => a.Primary)
                                              ?? resolvedRecord.Addresses.First();
                            recipientAddress = primaryAddr.WalletAddress;
                            _logger.LogDebug(
                                "Resolved disclosure recipient {ParticipantId} to wallet {Wallet} from register",
                                result.ParticipantId, recipientAddress);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve participant {ParticipantId} from register",
                            result.ParticipantId);
                    }
                }
            }

            if (string.IsNullOrEmpty(recipientAddress))
            {
                _logger.LogWarning("No wallet address for disclosure recipient {ParticipantId}", result.ParticipantId);
                continue;
            }

            disclosedPayloads[recipientAddress] = result.DisclosedData;
        }

        return disclosedPayloads;
    }

    /// <inheritdoc />
    public async Task<DisclosedActionData> ResolveDisclosedDataAsync(
        string instanceId,
        int actionId,
        IReadOnlyCollection<string> callerWallets,
        string? delegationToken,
        CancellationToken cancellationToken = default)
    {
        if (_stateReconstruction is null || _instanceStore is null || _blueprintStore is null)
        {
            throw new InvalidOperationException(
                "ActionDisclosureResolver was constructed without its read-side dependencies "
                + "(state reconstruction / instance store / blueprint store); ResolveDisclosedDataAsync is unavailable.");
        }

        DisclosedActionData Empty(string registerId) => new()
        {
            InstanceId = instanceId,
            ActionId = actionId,
            RegisterId = registerId,
            RecipientResolved = false,
        };

        if (callerWallets.Count == 0)
        {
            return Empty(string.Empty);
        }

        var instance = await _instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance is null)
        {
            _logger.LogDebug("Disclosed-data resolve: instance {InstanceId} not found", instanceId);
            return Empty(string.Empty);
        }

        var registerId = instance.RegisterId;

        var blueprint = await _blueprintStore.GetAsync(instance.BlueprintId);
        if (blueprint is null)
        {
            _logger.LogWarning(
                "Disclosed-data resolve: blueprint {BlueprintId} not found for instance {InstanceId}",
                instance.BlueprintId, instanceId);
            return Empty(registerId);
        }

        // The deciding action must exist — otherwise reconstruction has no required-prior set to work
        // from. Absence is treated as "nothing disclosed" (the caller falls back to a hold), not an error.
        if (blueprint.Actions?.Any(a => a.Id == actionId) != true)
        {
            _logger.LogDebug(
                "Disclosed-data resolve: action {ActionId} not found in blueprint {BlueprintId}",
                actionId, instance.BlueprintId);
            return Empty(registerId);
        }

        var callerSet = new HashSet<string>(callerWallets, StringComparer.OrdinalIgnoreCase);

        // Reconstruct ONLY the caller-decryptable view: pass the caller's wallets as the reconstruction
        // participant set so that on an encrypted register only groups sealed to the caller unwrap, and
        // on a dev-mode register only the caller's plaintext entry is read where present. The
        // per-prior-action clamp below is the belt-and-braces guarantee against the dev-mode
        // merge-everything fallback ever widening disclosure to a non-recipient.
        var callerReconstructionWallets = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        foreach (var w in callerWallets)
        {
            if (!string.IsNullOrEmpty(w))
            {
                callerReconstructionWallets[$"caller-{i++}"] = w;
            }
        }

        Models.AccumulatedState state;
        try
        {
            state = await _stateReconstruction.ReconstructAsync(
                blueprint,
                instanceId,
                actionId,
                registerId,
                delegationToken ?? string.Empty,
                callerReconstructionWallets,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail closed: a reconstruction fault surfaces as "no disclosure available" so the agent
            // holds and retries on the next poll (FR-005 / FR-009) rather than deciding on a blank view.
            _logger.LogWarning(ex,
                "Disclosed-data resolve: state reconstruction failed for instance {InstanceId} action {ActionId}; returning empty (fail-closed)",
                instanceId, actionId);
            return Empty(registerId);
        }

        var entries = new List<DisclosedActionEntry>();
        var merged = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (actionKey, actionElement) in state.ActionData)
        {
            if (!int.TryParse(actionKey, out var priorActionId))
            {
                continue;
            }

            var priorAction = blueprint.Actions!.FirstOrDefault(a => a.Id == priorActionId);
            if (priorAction is null)
            {
                continue;
            }

            var priorData = JsonElementToDictionary(actionElement);
            if (priorData.Count == 0)
            {
                continue;
            }

            // Clamp the reconstructed prior-action data to exactly what THIS prior action discloses to
            // the caller's participant. Uses the full instance bindings so recipient participants resolve
            // to wallets, then keeps only the caller's wallet entry.
            var disclosedByWallet = await ApplyDisclosuresAsync(
                priorAction, priorData, blueprint, instance.ParticipantWallets, registerId, cancellationToken);

            var callerFields = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (wallet, fields) in disclosedByWallet)
            {
                if (!callerSet.Contains(wallet))
                {
                    continue;
                }

                foreach (var (key, value) in fields)
                {
                    callerFields[key] = value;
                }
            }

            if (callerFields.Count == 0)
            {
                continue;
            }

            entries.Add(new DisclosedActionEntry
            {
                ActionId = priorActionId,
                ActionTitle = priorAction.Title ?? string.Empty,
                DisclosedAt = null,
                Data = callerFields,
            });

            foreach (var (key, value) in callerFields)
            {
                merged[key] = value;
            }
        }

        entries.Sort((a, b) => a.ActionId.CompareTo(b.ActionId));

        return new DisclosedActionData
        {
            InstanceId = instanceId,
            ActionId = actionId,
            RegisterId = registerId,
            RecipientResolved = entries.Count > 0,
            Disclosures = entries,
            DisclosedFields = merged,
        };
    }

    /// <summary>
    /// Projects a reconstructed action payload (a JSON object) into a
    /// <see cref="Dictionary{TKey,TValue}"/> the disclosure engine consumes. Non-object elements yield
    /// an empty map (nothing to disclose).
    /// </summary>
    private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }
}
