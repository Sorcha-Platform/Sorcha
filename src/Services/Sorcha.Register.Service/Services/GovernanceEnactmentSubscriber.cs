// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Events;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Raises the enactment for any proposal whose approvals have reached quorum, on every sealed docket.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the trigger is a sealed docket.</b> An approval counts when it seals, not when it is
/// accepted — so the moment a proposal can become enactable is exactly the moment a docket confirms.
/// Every node holding the register folds the same event and reaches the same answer from the same
/// sealed content (R-009); the enactment's transaction id is deterministic, so the several nodes that
/// can carry it dedupe rather than fork, and a node that governs nothing on the register declines.
/// </para>
/// <para>
/// <b>Why it re-scans open proposals rather than reading the docket's transactions.</b>
/// <see cref="DocketConfirmedEvent"/> carries transaction ids, so the approvals in the docket could be
/// found by loading each one — but that is a round trip per transaction on every docket, and it makes
/// the trigger depend on never missing an event. Control transactions are rare compared with workflow
/// traffic, so listing them is cheap, and re-evaluating every open proposal is <b>self-healing</b>: a
/// dropped event costs a delay until the register's next docket, not a proposal stuck at quorum
/// forever. That is a better failure mode than a precise trigger with no recovery.
/// </para>
/// <para>
/// <b>Its own subscriber, deliberately not <see cref="RegisterEventBridgeService"/>.</b> That one
/// bridges events to SignalR; an exception escaping a shared handler would take the bridge down with
/// it, so a governance failure would stop register notifications. Nothing here is allowed to throw
/// out of the handler.
/// </para>
/// <para>
/// A proposal that never reaches quorum simply stays open until it expires. Nothing here decides
/// authority — <see cref="IGovernanceEnactmentService"/> decides whether to try, and the Validator
/// decides whether the change is authorised.
/// </para>
/// </remarks>
public sealed class GovernanceEnactmentSubscriber : BackgroundService
{
    private readonly IEventSubscriber _subscriber;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GovernanceEnactmentSubscriber> _logger;

    /// <summary>Initialises a new instance of the <see cref="GovernanceEnactmentSubscriber"/> class.</summary>
    public GovernanceEnactmentSubscriber(
        IEventSubscriber subscriber,
        IServiceScopeFactory scopeFactory,
        ILogger<GovernanceEnactmentSubscriber> logger)
    {
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GovernanceEnactmentSubscriber watching {Channel} for proposals that have reached quorum",
            RegisterEventChannels.DocketConfirmed);

        await _subscriber.SubscribeAsync<DocketConfirmedEvent>(
            RegisterEventChannels.DocketConfirmed,
            e => OnDocketConfirmedAsync(e, stoppingToken),
            stoppingToken);
    }

    /// <summary>
    /// Evaluates every open proposal on the register the docket belongs to.
    /// </summary>
    /// <remarks>
    /// Internal so the behaviour can be driven directly in tests — a <c>BackgroundService</c> whose
    /// logic is reachable only through a live event bus is a component nothing can assert about.
    /// </remarks>
    internal async Task OnDocketConfirmedAsync(DocketConfirmedEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.RegisterId))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IGovernanceProposalReader>();
            var enactment = scope.ServiceProvider.GetRequiredService<IGovernanceEnactmentService>();

            var open = await reader.ListOpenAsync(e.RegisterId, ct);
            if (open.Count == 0)
            {
                return;
            }

            _logger.LogDebug(
                "Docket {DocketId} sealed on register {RegisterId}: re-evaluating {Count} open proposal(s)",
                e.DocketId, e.RegisterId, open.Count);

            foreach (var proposal in open)
            {
                ct.ThrowIfCancellationRequested();

                var proposalId = proposal.Transaction?.TxId;
                if (string.IsNullOrWhiteSpace(proposalId))
                {
                    continue;
                }

                // One proposal's failure must not stop the others being evaluated — a malformed or
                // unenactable proposal would otherwise block every later one on the register.
                try
                {
                    var result = await enactment.TryEnactAsync(e.RegisterId, proposalId, ct);

                    if (result.Outcome == EnactmentOutcome.Submitted)
                    {
                        _logger.LogInformation(
                            "Proposal {ProposalId} on register {RegisterId} enacted as {TxId} after docket {DocketId}",
                            proposalId, e.RegisterId, result.TxId, e.DocketId);
                    }
                    else
                    {
                        // Debug: on a busy register most proposals are simply still short of quorum,
                        // and logging that at information level once per docket is noise, not signal.
                        _logger.LogDebug(
                            "Proposal {ProposalId} on register {RegisterId} not enacted ({Outcome}): {Detail}",
                            proposalId, e.RegisterId, result.Outcome, result.Detail);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Enactment attempt for proposal {ProposalId} on register {RegisterId} threw",
                        proposalId, e.RegisterId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a fault.
        }
        catch (Exception ex)
        {
            // Never allowed to escape: an exception out of an event handler kills the subscription,
            // and a dead subscription is silent — governance would simply stop enacting with nothing
            // to show for it.
            _logger.LogError(ex,
                "Governance enactment sweep failed for register {RegisterId} after docket {DocketId}",
                e.RegisterId, e.DocketId);
        }
    }
}
