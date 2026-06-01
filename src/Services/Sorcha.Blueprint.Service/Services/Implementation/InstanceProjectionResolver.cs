// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 — resolves a sealed register transaction into the <see cref="ProjectedTransaction"/>
/// facts the deterministic <see cref="InstanceProjection"/> fold consumes. This is the <b>single</b>
/// resolution used by both the online <see cref="InstanceProjector"/> (fold-as-sealed) and the
/// <see cref="InstanceRebuildService"/> (batch replay) — sharing it is what guarantees a rebuilt view
/// is bit-for-bit identical to the materialized one (US4 parity invariant).
/// </summary>
public static class InstanceProjectionResolver
{
    /// <summary>
    /// The instance-scoping facts plus the projected transaction resolved from a sealed tx.
    /// </summary>
    public sealed record ResolvedProjection(
        string BlueprintId, string InstanceId, string TenantId, ProjectedTransaction Tx);

    /// <summary>
    /// Resolves a sealed transaction into a <see cref="ResolvedProjection"/>, or null when the
    /// transaction is not instance-scoped (genesis, governance, credential-issuance record, etc.).
    /// </summary>
    public static async Task<ResolvedProjection?> ResolveAsync(
        TransactionModel tx,
        IActionResolverService actionResolver,
        ILogger logger,
        CancellationToken ct)
    {
        if (tx?.MetaData is null)
            return null;

        var blueprintId = tx.MetaData.BlueprintId;
        var instanceId = tx.MetaData.InstanceId;
        if (string.IsNullOrEmpty(blueprintId) || string.IsNullOrEmpty(instanceId) || tx.MetaData.ActionId is null)
            return null;

        var completedActionId = (int)tx.MetaData.ActionId.Value;
        var decision = ResolveRoutingDecision(tx.MetaData, logger);
        var nextActionIds = decision is not null
            ? decision.NextActions.Select(a => a.ActionId).ToList()
            : tx.MetaData.NextActionId.HasValue ? [(int)tx.MetaData.NextActionId.Value] : [];

        var bindings = await ResolveParticipantBindingsAsync(
            blueprintId, completedActionId, nextActionIds, tx, actionResolver, logger, ct);

        var projected = new ProjectedTransaction(
            TxId: tx.TxId,
            PreviousTransactionId: string.IsNullOrEmpty(tx.PrevTxId) ? null : tx.PrevTxId,
            CompletedActionId: completedActionId,
            NextActionIds: nextActionIds,
            ParticipantBindings: bindings);

        return new ResolvedProjection(blueprintId, instanceId, ResolveTenantId(tx), projected);
    }

    /// <summary>
    /// Reads the carried <see cref="RoutingDecision"/> — preferring the typed
    /// <see cref="TransactionMetaData.RoutingDecision"/> field, falling back to the canonical JSON
    /// the producer wrote into the clear tracking metadata (key <c>routingDecision</c>), then to the
    /// legacy singular <see cref="TransactionMetaData.NextActionId"/>. Returns null when none present.
    /// </summary>
    public static RoutingDecision? ResolveRoutingDecision(TransactionMetaData metadata, ILogger logger)
    {
        if (metadata.RoutingDecision is not null)
            return metadata.RoutingDecision;

        if (metadata.TrackingData is not null
            && metadata.TrackingData.TryGetValue("routingDecision", out var json)
            && !string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<RoutingDecision>(
                    json, RegisterSerializationOptions.Canonical);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "InstanceProjectionResolver: could not deserialize carried routingDecision metadata");
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves participant-id → wallet bindings this transaction contributes, keyed by the
    /// blueprint participant id (never self-keyed by wallet address). Binds the completed action's
    /// sender to the tx sender, and the next action's sender to the recipient (the next actor), so
    /// bindings accumulate across the chain. Best-effort — on any resolution failure the transaction
    /// still folds control state with whatever bindings resolved.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> ResolveParticipantBindingsAsync(
        string blueprintId,
        int completedActionId,
        IReadOnlyList<int> nextActionIds,
        TransactionModel tx,
        IActionResolverService actionResolver,
        ILogger logger,
        CancellationToken ct)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bp = await actionResolver.GetBlueprintAsync(blueprintId, ct);
            if (bp is null)
                return bindings;

            var completedAction = actionResolver.GetActionDefinition(bp, completedActionId.ToString());
            if (completedAction is { Sender.Length: > 0 } && !string.IsNullOrEmpty(tx.SenderWallet))
                bindings[completedAction.Sender] = tx.SenderWallet;

            var recipientWallet = tx.RecipientsWallets?.FirstOrDefault(
                w => !string.IsNullOrEmpty(w) && !string.Equals(w, tx.SenderWallet, StringComparison.OrdinalIgnoreCase));
            if (recipientWallet is not null)
            {
                foreach (var nextId in nextActionIds)
                {
                    var nextAction = actionResolver.GetActionDefinition(bp, nextId.ToString());
                    if (nextAction is { Sender.Length: > 0 })
                        bindings[nextAction.Sender] = recipientWallet;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex,
                "InstanceProjectionResolver: participant-binding resolution failed for blueprint {BlueprintId}", blueprintId);
        }

        return bindings;
    }

    /// <summary>Resolves the tenant id carried on the transaction's tracking metadata.</summary>
    public static string ResolveTenantId(TransactionModel tx)
        => tx.MetaData?.TrackingData?.GetValueOrDefault("tenantId") ?? string.Empty;
}
