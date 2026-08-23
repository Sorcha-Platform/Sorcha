// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 US4 — reconstructs an instance's control state by replaying its sealed transactions
/// through the pure <see cref="InstanceProjection.Project"/> fold. The materialized instance row is a
/// cache; this proves it is rebuildable from the ledger at any time (FR-003) and lets an operator
/// repair a corrupt/missing view. Uses the same <see cref="InstanceProjectionResolver"/> the online
/// projector uses, so a rebuild is bit-for-bit identical to the materialized view (the parity invariant).
/// </summary>
public interface IInstanceRebuildService
{
    /// <summary>
    /// Rebuilds the instance projection purely from the register's sealed transactions for
    /// <paramref name="instanceId"/>. Returns null if the instance has no sealed transactions.
    /// Does not touch the materialized store.
    /// </summary>
    Task<Instance?> RebuildAsync(string registerId, string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Compares a fresh ledger rebuild to the materialized view and reports whether they agree.
    /// </summary>
    Task<InstanceParityResult> CheckParityAsync(string registerId, string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Operator-triggered repair: rebuilds from the ledger and overwrites the materialized view.
    /// Returns the rebuilt instance, or null if there was nothing to rebuild.
    /// </summary>
    Task<Instance?> RebuildAndPersistAsync(string registerId, string instanceId, CancellationToken ct = default);
}

/// <summary>Outcome of an instance parity self-check.</summary>
/// <param name="InSync">True when the rebuilt view matches the materialized view.</param>
/// <param name="Detail">Human-readable description of the first divergence, or null when in sync.</param>
/// <param name="Rebuilt">The fresh ledger rebuild (may be null if no sealed txs).</param>
/// <param name="Materialized">The stored materialized view (may be null if absent).</param>
public sealed record InstanceParityResult(bool InSync, string? Detail, Instance? Rebuilt, Instance? Materialized);

/// <inheritdoc cref="IInstanceRebuildService"/>
public sealed class InstanceRebuildService : IInstanceRebuildService
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IActionResolverService _actionResolver;
    private readonly IInstanceStore _instanceStore;
    private readonly ILogger<InstanceRebuildService> _logger;

    /// <summary>Initialises a new instance of the <see cref="InstanceRebuildService"/> class.</summary>
    public InstanceRebuildService(
        IRegisterServiceClient registerClient,
        IActionResolverService actionResolver,
        IInstanceStore instanceStore,
        ILogger<InstanceRebuildService> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _instanceStore = instanceStore ?? throw new ArgumentNullException(nameof(instanceStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Instance?> RebuildAsync(string registerId, string instanceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var txs = await _registerClient.GetTransactionsByInstanceIdAsync(registerId, instanceId, ct);

        var projected = new List<ProjectedTransaction>();
        string? blueprintId = null;
        string tenantId = string.Empty;

        foreach (var tx in txs)
        {
            var resolved = await InstanceProjectionResolver.ResolveAsync(tx, _actionResolver, _logger, ct);
            // Defend against a by-instance query that returns adjacent rows: only fold txs whose
            // resolved instance id matches the one we're rebuilding.
            if (resolved is null || !string.Equals(resolved.InstanceId, instanceId, StringComparison.Ordinal))
                continue;

            blueprintId ??= resolved.BlueprintId;
            if (string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(resolved.TenantId))
                tenantId = resolved.TenantId;
            projected.Add(resolved.Tx);
        }

        if (blueprintId is null || projected.Count == 0)
        {
            _logger.LogDebug(
                "InstanceRebuildService: no sealed instance-scoped transactions for instance {InstanceId} on register {RegisterId}",
                instanceId, registerId);
            return null;
        }

        // blueprintVersion 1 matches the online projector (InstanceProjector.FoldTransactionAsync)
        // so the rebuild and the materialized view are identical.
        return InstanceProjection.Project(instanceId, registerId, blueprintId, blueprintVersion: 1, tenantId, projected);
    }

    /// <inheritdoc />
    public async Task<InstanceParityResult> CheckParityAsync(string registerId, string instanceId, CancellationToken ct = default)
    {
        var rebuilt = await RebuildAsync(registerId, instanceId, ct);
        var materialized = await _instanceStore.GetAsync(instanceId, ct);

        if (rebuilt is null && materialized is null)
            return new InstanceParityResult(true, null, null, null);

        if (rebuilt is null || materialized is null)
        {
            return new InstanceParityResult(
                false,
                rebuilt is null ? "materialized view exists but ledger rebuild is empty" : "ledger rebuild exists but materialized view is missing",
                rebuilt, materialized);
        }

        var detail = FirstDivergence(rebuilt, materialized);
        return new InstanceParityResult(detail is null, detail, rebuilt, materialized);
    }

    /// <inheritdoc />
    public async Task<Instance?> RebuildAndPersistAsync(string registerId, string instanceId, CancellationToken ct = default)
    {
        var rebuilt = await RebuildAsync(registerId, instanceId, ct);
        if (rebuilt is null)
            return null;

        var existing = await _instanceStore.GetAsync(instanceId, ct);
        if (existing is null)
            await _instanceStore.CreateAsync(rebuilt, ct);
        else
            await _instanceStore.UpdateAsync(rebuilt, ct);

        _logger.LogInformation(
            "InstanceRebuildService: rebuilt + persisted instance {InstanceId} from the ledger — action(s) {Actions}, state {State}",
            instanceId, string.Join(",", rebuilt.CurrentActionIds), rebuilt.State);
        return rebuilt;
    }

    /// <summary>
    /// Compares the control-state fields that the projection owns. Returns a description of the
    /// first divergence, or null when the two views agree.
    /// </summary>
    private static string? FirstDivergence(Instance rebuilt, Instance materialized)
    {
        if (rebuilt.State != materialized.State)
            return $"state differs (rebuilt={rebuilt.State}, materialized={materialized.State})";

        // Feature 194. Checked FIRST among the value comparisons, because a pin divergence is the
        // most consequential kind: the two views would run the instance against different blueprint
        // definitions, and every other field could still agree while they did so.
        if (!string.Equals(rebuilt.BlueprintExecDefHash, materialized.BlueprintExecDefHash, StringComparison.Ordinal))
        {
            return "pinned blueprint definition differs " +
                   $"(rebuilt={Describe(rebuilt.BlueprintExecDefHash)}, materialized={Describe(materialized.BlueprintExecDefHash)})";
        }

        var rebuiltActions = rebuilt.CurrentActionIds.OrderBy(x => x).ToList();
        var materializedActions = materialized.CurrentActionIds.OrderBy(x => x).ToList();
        if (!rebuiltActions.SequenceEqual(materializedActions))
            return $"currentActionIds differ (rebuilt=[{string.Join(",", rebuiltActions)}], materialized=[{string.Join(",", materializedActions)}])";

        foreach (var (participantId, wallet) in rebuilt.ParticipantWallets)
        {
            if (!materialized.ParticipantWallets.TryGetValue(participantId, out var matWallet)
                || !string.Equals(wallet, matWallet, StringComparison.OrdinalIgnoreCase))
            {
                return $"participant binding differs for '{participantId}' (rebuilt={wallet}, materialized={matWallet ?? "<missing>"})";
            }
        }

        if (materialized.ParticipantWallets.Count != rebuilt.ParticipantWallets.Count)
            return $"participant binding count differs (rebuilt={rebuilt.ParticipantWallets.Count}, materialized={materialized.ParticipantWallets.Count})";

        return null;
    }

    private static string Describe(string? execDefHash) =>
        string.IsNullOrWhiteSpace(execDefHash) ? "(unpinned)" : execDefHash;
}
