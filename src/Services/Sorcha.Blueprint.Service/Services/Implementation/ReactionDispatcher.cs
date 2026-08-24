// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.AtomicCache;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 US2 — owns the workflow's at-least-once, role-gated <b>side effects</b>
/// (notifications + durable inbox writes), cleanly separated from the pure
/// <see cref="InstanceProjector"/> which only advances state. The projector invokes this
/// in-process after it folds + persists an instance, handing over the freshly-folded state.
/// </summary>
public interface IReactionDispatcher
{
    /// <summary>
    /// Dispatches the side-effect reactions for an instance that has just advanced by folding the
    /// sealed transaction <paramref name="tx"/>. Each reaction is gated by node
    /// <b>entitlement</b> (only the node hosting the target wallet fires it — cross-node dedup) and
    /// is <b>idempotent</b> on <c>(sealedTxId, kind, target)</c> (replay/restart dedup). Best-effort:
    /// a reaction failure is logged and swallowed so it never disturbs the committed projection.
    /// </summary>
    /// <param name="instance">The freshly-folded instance.</param>
    /// <param name="tx">
    /// The sealed transaction just folded. Feature 184 reads its carried, sender-signed
    /// <see cref="RoutingDecision"/> to decide whether a decision notice is owed — so the dispatcher
    /// needs the transaction itself, not merely its id. It reads only the transaction's <b>clear</b>
    /// metadata; it never touches payload.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync(Instance instance, TransactionModel tx, CancellationToken ct);
}

/// <inheritdoc cref="IReactionDispatcher"/>
public sealed class ReactionDispatcher : IReactionDispatcher
{
    // Reaction kinds — also the OTel tag + part of the idempotency key.
    private const string KindActionAvailable = "action-available";
    private const string KindWorkflowCompleted = "workflow-completed";
    private const string KindDecisionNotice = "decision-notice";

    // Once a reaction has fired we never want to fire it again for the same sealed tx. The sealed
    // tx is immutable, so a long TTL is purely a bound on cache growth, not a correctness window.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromDays(7);

    private readonly INotificationService? _notifier;
    private readonly IWalletServiceClient _walletClient;
    private readonly IAtomicDistributedCache _claims;
    private readonly IActionResolverService _actionResolver;
    private readonly ReactionDispatcherMetrics _metrics;
    private readonly ILogger<ReactionDispatcher> _logger;

    /// <summary>Initialises a new instance of the <see cref="ReactionDispatcher"/> class.</summary>
    public ReactionDispatcher(
        IWalletServiceClient walletClient,
        IAtomicDistributedCache claims,
        ReactionDispatcherMetrics metrics,
        ILogger<ReactionDispatcher> logger,
        IActionResolverService actionResolver,
        INotificationService? notifier = null)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _claims = claims ?? throw new ArgumentNullException(nameof(claims));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _notifier = notifier;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(Instance instance, TransactionModel tx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var sealedTxId = tx?.TxId;
        if (_notifier is null || string.IsNullOrEmpty(sealedTxId))
        {
            return;
        }

        try
        {
            // Feature 184 — the decision notice. Runs BEFORE the terminal/active branching: a notice
            // is owed by the route that was taken, whether or not that route ends the workflow.
            await DispatchDecisionNoticeAsync(instance, tx!, sealedTxId, ct);

            if (instance.CurrentActionIds.Count == 0)
            {
                // Terminal: notify every participant wallet the workflow completed.
                var wallets = instance.ParticipantWallets.Values
                    .Where(w => !string.IsNullOrEmpty(w))
                    .Distinct();

                var completedWallets = new List<string>();
                foreach (var wallet in wallets)
                {
                    if (await ShouldFireAsync(sealedTxId, KindWorkflowCompleted, wallet, ct))
                    {
                        completedWallets.Add(wallet);
                    }
                }

                if (completedWallets.Count > 0)
                {
                    await _notifier.NotifyWorkflowCompletedAsync(instance.Id, completedWallets, ct);
                    foreach (var _ in completedWallets)
                    {
                        _metrics.RecordDispatched(KindWorkflowCompleted);
                    }
                }
                return;
            }

            // Active: a current action is available. Resolve the assignee wallet with the same
            // heuristic the projector used (single bound participant ⇒ that wallet; otherwise the
            // assignee is ambiguous and no per-wallet signal fires — unchanged from pre-US2).
            var assigneeWallet = instance.ParticipantWallets.Count == 1
                ? instance.ParticipantWallets.Values.First()
                : null;

            if (string.IsNullOrEmpty(assigneeWallet))
            {
                return;
            }

            foreach (var actionId in instance.CurrentActionIds)
            {
                if (await ShouldFireAsync(sealedTxId, $"{KindActionAvailable}:{actionId}", assigneeWallet, ct))
                {
                    await _notifier.NotifyActionAvailableAsync(instance.Id, assigneeWallet, actionId.ToString(), ct);
                    _metrics.RecordDispatched(KindActionAvailable);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "ReactionDispatcher: instance {InstanceId} advanced + persisted, but a side-effect reaction for tx {TxId} failed",
                instance.Id, sealedTxId);
        }
    }

    /// <summary>
    /// Feature 184 — writes the durable decision notice the taken route declares, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole point of moving the notice onto the fold. The deciding participant (an
    /// autonomous agent, say) submits on <i>its</i> node; the recipient (a citizen) has an account and
    /// an inbox on <i>theirs</i>. Every node holding the register folds the same sealed transaction,
    /// and <see cref="ShouldFireAsync"/> lets exactly the one hosting the recipient's wallet act on it.
    /// </para>
    /// <para>
    /// Note what is NOT needed here: no payload, no decryption, no delegation token. The sender signed
    /// the route it took and a non-sensitive reason code onto the transaction's clear metadata; the
    /// citizen-facing wording comes from the blueprint this node already holds. That is what lets the
    /// notice work on an encrypted register, where a background fold could read nothing else.
    /// </para>
    /// </remarks>
    private async Task DispatchDecisionNoticeAsync(
        Instance instance, TransactionModel tx, string sealedTxId, CancellationToken ct)
    {
        if (tx.MetaData is null)
            return;

        var decision = InstanceProjectionResolver.ResolveRoutingDecision(tx.MetaData, _logger);
        if (decision is null || string.IsNullOrEmpty(decision.RouteId))
            return;   // Pre-Feature-184 transaction, or an action that routed nowhere by name.

        var blueprint = await _actionResolver.GetBlueprintAsync(instance.BlueprintId, instance.BlueprintDefinitionTxId, ct);
        if (blueprint is null)
        {
            _logger.LogDebug(
                "Decision notice: blueprint {BlueprintId} not resolvable on this node; skipping.",
                instance.BlueprintId);
            return;
        }

        var completedAction = _actionResolver.GetActionDefinition(
            blueprint, decision.CompletedActionId.ToString());

        var notice = completedAction?.Routes
            ?.FirstOrDefault(r => string.Equals(r.Id, decision.RouteId, StringComparison.Ordinal))
            ?.DecisionNotice;

        if (notice is null || string.IsNullOrWhiteSpace(notice.RecipientParticipantId))
            return;   // The ordinary case: the taken route declares no notice.

        if (!instance.ParticipantWallets.TryGetValue(notice.RecipientParticipantId, out var recipientWallet) ||
            string.IsNullOrWhiteSpace(recipientWallet))
        {
            _logger.LogDebug(
                "Decision notice: recipient participant {ParticipantId} is not bound to a wallet on instance "
                + "{InstanceId}; skipping.",
                notice.RecipientParticipantId, instance.Id);
            return;
        }

        if (!await ShouldFireAsync(sealedTxId, KindDecisionNotice, recipientWallet, ct))
            return;   // Another node hosts this recipient, or the notice already fired.

        var message = notice.ResolveMessage(decision.ReasonCode);
        var title = string.IsNullOrWhiteSpace(notice.Title) ? "Application decision" : notice.Title;
        var severity = string.IsNullOrWhiteSpace(notice.Severity) ? "Warning" : notice.Severity!;

        await _notifier!.NotifyDecisionAsync(
            recipientWallet, instance.Id, decision.CompletedActionId.ToString(),
            title, message, severity, ct);

        _metrics.RecordDispatched(KindDecisionNotice);
        _logger.LogInformation(
            "Decision notice written for instance {InstanceId} (route {RouteId}, reason {ReasonCode}) to the "
            + "recipient wallet hosted on this node.",
            instance.Id, decision.RouteId, decision.ReasonCode ?? "(none)");
    }

    /// <summary>
    /// Returns true iff this node should fire the reaction: it hosts the target wallet (entitlement)
    /// AND it successfully claims the <c>(sealedTxId, kind, wallet)</c> key (idempotency).
    /// </summary>
    private async Task<bool> ShouldFireAsync(string sealedTxId, string kind, string walletAddress, CancellationToken ct)
    {
        // Entitlement: only the node whose Wallet Service hosts this wallet reacts. A null result
        // means the wallet is not local — another node owns this reaction.
        WalletInfo? wallet;
        try
        {
            wallet = await _walletClient.GetWalletAsync(walletAddress, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ReactionDispatcher: entitlement probe failed for wallet {Wallet} (tx {TxId}); treating as not-entitled",
                walletAddress, sealedTxId);
            wallet = null;
        }

        if (wallet is null)
        {
            _metrics.RecordEntitlementSkip(kind);
            return false;
        }

        // Idempotency: claim the reaction once. A failed claim means it already fired
        // (cross-replica on this node, replay, or a rebuild re-fold).
        var claimKey = $"react:{sealedTxId}:{kind}:{walletAddress}";
        var claimed = await _claims.TrySetIfAbsentAsync(claimKey, "1", ClaimTtl, ct);
        if (!claimed)
        {
            _metrics.RecordIdempotentSkip(kind);
            return false;
        }

        return true;
    }
}
