// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// A single sealed action transaction reduced to the facts the deterministic instance
/// projection needs (Feature 145). The impure <c>InstanceProjector</c> resolves these from
/// the register (transaction + carried <c>RoutingDecision</c> + blueprint participant map)
/// and hands the pure fold a value it can replay identically on any node.
/// </summary>
/// <param name="TxId">The sealed transaction id (idempotency key + chain node).</param>
/// <param name="PreviousTransactionId">The predecessor in this instance's chain; null for the starting action.</param>
/// <param name="CompletedActionId">The action this transaction completes.</param>
/// <param name="NextActionIds">The full next-action set from the validated <c>RoutingDecision</c> (preserves parallel branches).</param>
/// <param name="ParticipantBindings">Participant-id → wallet bindings this transaction contributes (already resolved against the blueprint; never self-keyed by the projector).</param>
/// <param name="IsRejection">True when this transaction is a terminal rejection.</param>
/// <param name="RouteId">
/// Feature 186 — the route the sender took, from the signed <c>RoutingDecision</c>. Null on
/// transactions sealed before Feature 184 and on presentation-outcome decisions.
/// </param>
/// <param name="ReasonCode">
/// Feature 186 — the non-sensitive reason code the sender stamped on the decision, from the same
/// signed source. Null unless the taken route declares an <c>x-decision-notice</c> with a
/// <c>reasonCodeField</c> that resolved against the submitted payload.
/// </param>
public sealed record ProjectedTransaction(
    string TxId,
    string? PreviousTransactionId,
    int CompletedActionId,
    IReadOnlyList<int> NextActionIds,
    IReadOnlyDictionary<string, string> ParticipantBindings,
    bool IsRejection = false,
    string? RouteId = null,
    string? ReasonCode = null,
    string? BlueprintDefinitionTxId = null);

/// <summary>
/// The result of folding one sealed transaction into an instance (Feature 194 made this a
/// three-state answer: "did not advance" is no longer enough, because refusing a foreign definition
/// and re-seeing an already-folded transaction are entirely different events).
/// </summary>
public enum FoldOutcome
{
    /// <summary>The instance advanced.</summary>
    Advanced,

    /// <summary>Already folded — the transaction id equals the watermark. Routine and silent.</summary>
    AlreadyApplied,

    /// <summary>
    /// The transaction claims a different blueprint definition than the one the instance is pinned
    /// to. Refused: a sender must not be able to move a running instance onto another definition by
    /// asserting one. This is a divergence, and an operator should see it.
    /// </summary>
    RefusedForeignDefinition,
}

/// <summary>
/// The pure, deterministic core of Feature 145: folds a set of sealed action transactions into
/// an instance projection. Identical sealed input yields identical instance control state on
/// every node, independent of arrival order (FR-001), and re-applying an already-folded
/// transaction is a no-op (FR-004). This same fold backs both the online projector and the
/// offline <c>RebuildAsync</c> (FR-003), so the materialized view and a fresh replay agree.
/// </summary>
/// <remarks>
/// Determinism comes from folding in <b>chain order</b> (each transaction links to its
/// predecessor via <see cref="ProjectedTransaction.PreviousTransactionId"/>), not arrival order.
/// The fold needs only the validated <c>RoutingDecision</c> carried in the clear — never the
/// encrypted payload (FR-010).
/// </remarks>
public static class InstanceProjection
{
    /// <summary>
    /// Projects the complete set of an instance's sealed action transactions into an
    /// <see cref="Instance"/> materialized view. Order-independent: any permutation of
    /// <paramref name="sealedTransactions"/> yields the same result.
    /// </summary>
    /// <param name="instanceId">The ledger-derived instance id.</param>
    /// <param name="registerId">The register the instance lives on.</param>
    /// <param name="blueprintId">The blueprint the instance executes.</param>
    /// <param name="blueprintVersion">The blueprint version at the starting action.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="sealedTransactions">Every sealed action transaction for this instance, in any order.</param>
    /// <param name="createdAt">Creation timestamp to stamp on the rebuilt view (defaults to now).</param>
    /// <returns>The folded instance, or null when no transactions are supplied.</returns>
    public static Instance? Project(
        string instanceId,
        string registerId,
        string blueprintId,
        int blueprintVersion,
        string tenantId,
        IEnumerable<ProjectedTransaction> sealedTransactions,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var ordered = OrderByChain(sealedTransactions);
        if (ordered.Count == 0)
            return null;

        var instance = new Instance
        {
            Id = instanceId,
            RegisterId = registerId,
            BlueprintId = blueprintId,
            BlueprintVersion = blueprintVersion,
            TenantId = tenantId,
            State = InstanceState.Active,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            FirstTransactionId = ordered[0].TxId,
        };

        foreach (var tx in ordered)
        {
            // Feature 194: a transaction claiming a definition other than the one this instance is
            // pinned to is not folded — in the batch path as in the online one, or a rebuild would
            // reach a different answer than the projector and break the FR-003 parity guarantee.
            // Skipping (rather than throwing) keeps the fold total and deterministic; the caller
            // reports the refusal.
            if (!IsDefinitionCompatible(instance, tx))
                continue;

            ApplyInPlace(instance, tx);
        }

        return instance;
    }

    /// <summary>
    /// Idempotently folds a single sealed transaction into an existing instance view (the online
    /// projector path). Returns the instance unchanged if <paramref name="tx"/> has already been
    /// applied (its id equals the watermark, FR-004). The caller is responsible for delivering
    /// transactions in chain order; out-of-order delivery is handled by a full
    /// <see cref="Project"/> rebuild.
    /// </summary>
    /// <param name="instance">The current materialized view (mutated in place).</param>
    /// <param name="tx">The sealed transaction to fold.</param>
    /// <returns>What happened — see <see cref="FoldOutcome"/>.</returns>
    public static FoldOutcome Apply(Instance instance, ProjectedTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(tx);

        if (string.Equals(instance.LastAppliedTxId, tx.TxId, StringComparison.Ordinal))
            return FoldOutcome.AlreadyApplied;

        if (!IsDefinitionCompatible(instance, tx))
            return FoldOutcome.RefusedForeignDefinition;

        ApplyInPlace(instance, tx);
        return FoldOutcome.Advanced;
    }

    /// <summary>
    /// Feature 194 — whether a transaction may be folded into this instance, given the definition it
    /// claims to have been executed against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A different non-null pin is refused.</b> That is the divergence this feature exists to
    /// prevent: without it, a sender could move a running instance onto another definition simply by
    /// asserting one, and two nodes folding the same ledger could disagree about which rules an
    /// instance runs under.
    /// </para>
    /// <para>
    /// <b>A null pin is accepted, deliberately.</b> Null means the transaction predates Feature 194,
    /// not that it claims something different — refusing it would wedge instances whose earlier
    /// actions sealed before the feature shipped, which is a worse outcome than folding one action
    /// through the documented fallback. The callers log and count each occurrence so the fallback
    /// can eventually be removed on evidence rather than on hope.
    /// </para>
    /// </remarks>
    public static bool IsDefinitionCompatible(Instance instance, ProjectedTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(tx);

        if (string.IsNullOrWhiteSpace(tx.BlueprintDefinitionTxId))
            return true;

        if (string.IsNullOrWhiteSpace(instance.BlueprintDefinitionTxId))
            return true; // not yet pinned — this transaction establishes the pin

        return string.Equals(
            instance.BlueprintDefinitionTxId, tx.BlueprintDefinitionTxId, StringComparison.Ordinal);
    }

    private static void ApplyInPlace(Instance instance, ProjectedTransaction tx)
    {
        // Feature 194: establish the pin from the first transaction that carries one, and never
        // change it afterwards. `Apply` has already refused a transaction claiming a different
        // definition, so reaching here with a non-empty existing pin means they agree.
        //
        // Assigned only when currently empty — the OPPOSITE of the Feature 186 decision fields
        // below, which are assigned unconditionally so a later transaction clears a stale reason.
        // A pin is not a per-transaction fact; it is the instance's identity for its whole life.
        if (string.IsNullOrWhiteSpace(instance.BlueprintDefinitionTxId)
            && !string.IsNullOrWhiteSpace(tx.BlueprintDefinitionTxId))
        {
            instance.BlueprintDefinitionTxId = tx.BlueprintDefinitionTxId;
        }

        // Advance control state: remove the completed action, add the full next-action set
        // (parallel branches preserved). Dedup keeps CurrentActionIds a clean set.
        instance.CurrentActionIds.RemoveAll(id => id == tx.CompletedActionId);
        foreach (var next in tx.NextActionIds)
        {
            if (!instance.CurrentActionIds.Contains(next))
                instance.CurrentActionIds.Add(next);
        }
        instance.CurrentActionIds.Sort(); // canonical order — identical on every node

        // Merge participant→wallet bindings (participant-id keyed, last-writer-wins).
        foreach (var binding in tx.ParticipantBindings)
            instance.ParticipantWallets[binding.Key] = binding.Value;

        instance.CompletedActionCount++;
        instance.LastTransactionId = tx.TxId;
        instance.LastAppliedTxId = tx.TxId;
        instance.UpdatedAt = DateTimeOffset.UtcNow;

        // Feature 186: record the decision this transaction carried. Both values come from the
        // signed clear metadata, so the fold stays byte-identical on every node and a rebuild
        // reproduces them. The citizen-facing WORDING is deliberately not recorded here — it is
        // resolved on read from the local blueprint, and folding it would put node-local state
        // inside a fold FR-001 requires to be node-independent.
        //
        // Assigned unconditionally, so a transaction carrying no decision CLEARS the previous one:
        // an application refused on one branch and then advanced on another must not keep showing
        // the reason for a step it has already moved past.
        instance.DecisionRouteId = tx.RouteId;
        instance.DecisionReasonCode = tx.ReasonCode;

        // Derive terminal state: a rejection is terminal; no remaining current actions means
        // every branch has reached a route-graph terminal (Completed).
        if (tx.IsRejection)
        {
            instance.State = InstanceState.Rejected;
        }
        else if (instance.CurrentActionIds.Count == 0)
        {
            instance.State = InstanceState.Completed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            instance.State = InstanceState.Active;
        }
    }

    /// <summary>
    /// Orders transactions by their instance chain (predecessor links), deterministically and
    /// independent of input order. The root is the transaction whose predecessor is null or
    /// outside the set (the starting action). Any transactions not reachable through the chain
    /// (defensive: a gap or fork) are appended in stable tx-id order so the fold stays total.
    /// </summary>
    private static List<ProjectedTransaction> OrderByChain(IEnumerable<ProjectedTransaction> transactions)
    {
        // De-duplicate by TxId (idempotent input), keeping the first occurrence.
        var byId = new Dictionary<string, ProjectedTransaction>(StringComparer.Ordinal);
        foreach (var tx in transactions)
            byId.TryAdd(tx.TxId, tx);

        if (byId.Count == 0)
            return [];

        var byPrev = new Dictionary<string, ProjectedTransaction>(StringComparer.Ordinal);
        var roots = new List<ProjectedTransaction>();
        foreach (var tx in byId.Values)
        {
            if (tx.PreviousTransactionId is { Length: > 0 } prev && byId.ContainsKey(prev))
                byPrev[prev] = tx;
            else
                roots.Add(tx);
        }

        // Deterministic root selection if more than one (defensive): lowest tx id.
        roots.Sort((a, b) => string.CompareOrdinal(a.TxId, b.TxId));

        var ordered = new List<ProjectedTransaction>(byId.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var cursor = root;
            while (cursor is not null && seen.Add(cursor.TxId))
            {
                ordered.Add(cursor);
                cursor = byPrev.TryGetValue(cursor.TxId, out var next) ? next : null;
            }
        }

        // Append any stragglers not reached via the chain, in stable order.
        if (ordered.Count != byId.Count)
        {
            foreach (var tx in byId.Values.OrderBy(t => t.TxId, StringComparer.Ordinal))
                if (seen.Add(tx.TxId))
                    ordered.Add(tx);
        }

        return ordered;
    }
}
