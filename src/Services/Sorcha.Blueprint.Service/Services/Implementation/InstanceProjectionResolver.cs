// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

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

        // Feature 145 US6: a presentation-lifecycle tx (PresentationInitiated / PresentationOutcome /
        // PresentationAbandoned) advances the instance ONLY when it carries a signed RoutingDecision —
        // i.e. a successful outcome that routes onward. All three chain off (and carry the ActionId of)
        // the same presentation-gated action, which became current via the PREVIOUS action's routing
        // fold; folding one with an empty next-action set would wrongly retire that still-current
        // action (premature completion) and then make the imperative advance early-exit. So skip a
        // presentation-lifecycle tx that carries no decision — the action stays current until a
        // successful outcome routes it onward. Genuine action terminals are unaffected: the producer
        // always writes a RoutingDecision (with an empty NextActions set) for them, so they are not
        // presentation-lifecycle txs and fold to completion correctly.
        if (decision is null && tx.MetaData.TransactionType.IsPresentationLifecycle())
            return null;

        // Feature 145: the carried RoutingDecision is the sole routing source — the legacy singular
        // NextActionId hint is fully removed. A tx with no decision contributes a terminal (empty)
        // next-action set (genuine terminals + legacy pre-145 txs that never carried one).
        var nextActionIds = decision is not null
            ? decision.NextActions.Select(a => a.ActionId).ToList()
            : new List<int>();

        var bindings = await ResolveParticipantBindingsAsync(
            blueprintId, completedActionId, nextActionIds, tx, actionResolver, logger, ct);

        var projected = new ProjectedTransaction(
            TxId: tx.TxId,
            PreviousTransactionId: string.IsNullOrEmpty(tx.PrevTxId) ? null : tx.PrevTxId,
            CompletedActionId: completedActionId,
            NextActionIds: nextActionIds,
            ParticipantBindings: bindings,
            // Feature 186: carry the decision's route and reason code through to the fold. Both ride
            // the transaction in the clear and are inside RoutingDecision.ComputeSignableBytes, so
            // they are signed and every node folding this transaction records the same pair.
            RouteId: decision?.RouteId,
            ReasonCode: decision?.ReasonCode);

        return new ResolvedProjection(blueprintId, instanceId, ResolveTenantId(tx), projected);
    }

    /// <summary>
    /// Reads the carried <see cref="RoutingDecision"/> — preferring the typed
    /// <see cref="TransactionMetaData.RoutingDecision"/> field, falling back to the canonical JSON
    /// the producer wrote into the clear tracking metadata (key <c>routingDecision</c>). Returns null
    /// when none present.
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

            // Feature 145 (#912): seed pre-baked participant → wallet mappings straight from the
            // published blueprint. A closed / pre-registered participant (e.g. a verification
            // analyst) carries its WalletAddress baked into the blueprint at publish time. Without
            // this, a participant only enters instance.ParticipantWallets once they ACT — so an
            // instance sitting at a pre-baked participant's action is invisible to
            // EfCoreInstanceStore.GetPendingActionsByWalletAsync(thatWallet) until they submit, the
            // chicken-and-egg that stops a rules agent from auto-approving the action it was waiting
            // on. Open participants (WalletAddress null, e.g. the late-bound applicant) are
            // intentionally skipped — they bind from the tx sender below. Seeding here, in the
            // shared resolver, keeps the online projector and the offline rebuild in lock-step
            // (US4 parity); the per-tx bindings below take precedence via last-writer-wins.
            foreach (var participant in bp.Participants)
            {
                if (!string.IsNullOrEmpty(participant.Id) && !string.IsNullOrEmpty(participant.WalletAddress))
                    bindings[participant.Id] = participant.WalletAddress;
            }

            var completedAction = actionResolver.GetActionDefinition(bp, completedActionId.ToString());
            if (completedAction is { Sender.Length: > 0 } && !string.IsNullOrEmpty(tx.SenderWallet))
                bindings[completedAction.Sender] = tx.SenderWallet;

            // Hand-off binding: name the NEXT actor from this transaction's recipients, so an open
            // participant who has not yet acted is discoverable before their first submission.
            //
            // This is a GUESS and is treated as one. A transaction fans out to every participant a
            // disclosure names, so RecipientsWallets is a set whose ORDER carries no meaning —
            // "the first entry that isn't the sender" identifies the next actor only when there is
            // exactly one candidate. It is therefore applied only where nothing better is known,
            // and never allowed to displace a fact:
            //
            //   - a participant the BLUEPRINT binds is already seeded above, authoritatively;
            //   - a participant who SIGNED this transaction was just bound from tx.SenderWallet;
            //   - more than one non-sender recipient means the fan-out does not identify anyone.
            //
            // Overwriting either fact is not a lesser evil than leaving a participant unbound: the
            // fold merges these bindings last-writer-wins, and instance.ParticipantWallets is what
            // InstanceParticipantGate and GetPendingActionsByWalletAsync authorise against. A wrong
            // entry therefore locks the real participant out of the instance that is waiting for
            // them — 403 "You are not a participant on this instance", and nothing in their pending
            // list — whereas an absent entry costs only pre-action discoverability and repairs
            // itself the moment they act. Found live on n1 by the TradeFinance walkthrough (#1427),
            // where two consecutive actions share a sender and folding the first rebound that
            // sender to whichever recipient happened to be listed first.
            var handOffCandidates = tx.RecipientsWallets?
                .Where(w => !string.IsNullOrEmpty(w) && !string.Equals(w, tx.SenderWallet, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            if (handOffCandidates.Count == 1)
            {
                var recipientWallet = handOffCandidates[0];
                foreach (var nextId in nextActionIds)
                {
                    var nextAction = actionResolver.GetActionDefinition(bp, nextId.ToString());
                    if (nextAction is { Sender.Length: > 0 })
                        bindings.TryAdd(nextAction.Sender, recipientWallet);
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
