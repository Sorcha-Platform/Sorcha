// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 145 US6 — the shared <see cref="InstanceProjectionResolver"/> must treat presentation
/// intra-action-lifecycle terminals correctly: fold a SUCCESS outcome (carries a signed
/// RoutingDecision → advances) but SKIP a decline / abandonment (no decision → action stays
/// current), while genuine action terminals (no decision, not intra-action-lifecycle) still fold.
/// </summary>
public class InstanceProjectionResolverTests
{
    private static readonly IActionResolverService NoActionResolver =
        new Mock<IActionResolverService>().Object; // GetBlueprintAsync → null ⇒ best-effort empty bindings

    private static TransactionModel Tx(
        TransactionType type,
        RoutingDecision? decision,
        uint actionId = 2) => new()
    {
        TxId = "tx-outcome",
        PrevTxId = "tx-initiated",
        SenderWallet = "ws-submitter",
        MetaData = new TransactionMetaData
        {
            BlueprintId = "bp-1",
            InstanceId = "inst-1",
            ActionId = actionId,
            TransactionType = type,
            RoutingDecision = decision,
        },
    };

    private static RoutingDecision Decision(int completed, params int[] next) => new()
    {
        CompletedActionId = completed,
        NextActions = next.Select(n => new ActionRef { ActionId = n }).ToList(),
        Attestation = new Attestation { Kind = AttestationKind.SenderSigned },
    };

    [Fact]
    public async Task ResolveAsync_PresentationOutcomeDecline_NoDecision_Skipped()
    {
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.PresentationOutcome, decision: null),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().BeNull("a declined presentation carries no RoutingDecision and must not advance");
    }

    [Fact]
    public async Task ResolveAsync_PresentationInitiated_NoDecision_Skipped()
    {
        // PresentationInitiated is a lifecycle marker — the gated action became current via the
        // previous action's routing fold, so folding Initiated (empty next-action set) would wrongly
        // retire the action before the outcome arrives. Must be skipped.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.PresentationInitiated, decision: null),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().BeNull("PresentationInitiated carries no RoutingDecision and must not change instance state");
    }

    [Fact]
    public async Task ResolveAsync_PresentationAbandoned_NoDecision_Skipped()
    {
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.PresentationAbandoned, decision: null),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().BeNull("an abandoned presentation carries no RoutingDecision and must not advance");
    }

    [Fact]
    public async Task ResolveAsync_PresentationOutcomeSuccess_WithDecision_FoldsAndAdvances()
    {
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.PresentationOutcome, Decision(2, 3)),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull("a successful outcome carries a RoutingDecision and advances via the projector");
        resolved!.Tx.CompletedActionId.Should().Be(2);
        resolved.Tx.NextActionIds.Should().Equal(3);
    }

    [Fact]
    public async Task ResolveAsync_ActionTerminal_NoDecision_FoldsAsTerminal()
    {
        // A plain Action tx with no decision is NOT an intra-action-lifecycle terminal, so it is NOT
        // skipped — it folds with an empty next-action set (terminal). This guards against the skip
        // over-reaching to ordinary action transactions.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.Action, decision: null),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Tx.NextActionIds.Should().BeEmpty();
    }
}
