// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Models;
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

    // ---- Feature 145 (#912): pre-baked participant wallets are seeded into the projection so a
    // closed participant (e.g. a verification analyst) is discoverable BEFORE they act. ----

    /// <summary>
    /// Builds an AssuredIdentity-shaped resolver: action 1 (open applicant, starting) → action 2
    /// (pre-baked analyst). The analyst's wallet is baked into the published blueprint; the
    /// applicant late-binds at runtime (WalletAddress null).
    /// </summary>
    private static IActionResolverService PreBakedAnalystResolver(string analystWallet)
    {
        var blueprint = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "bp-1",
            Participants =
            [
                new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
                new Participant { Id = "analyst", Name = "Verification Analyst", WalletAddress = analystWallet },
            ],
            Actions =
            [
                new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "applicant", IsStartingAction = true },
                new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "analyst" },
            ],
        };

        var mock = new Mock<IActionResolverService>();
        mock.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        mock.Setup(r => r.GetActionDefinition(blueprint, It.IsAny<string>()))
            .Returns((Sorcha.Blueprint.Models.Blueprint bp, string id) => bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));
        return mock.Object;
    }

    [Fact]
    public async Task ResolveAsync_SeedsPreBakedParticipantWallet_BeforeThatParticipantActs()
    {
        // Citizen submits the starting action (action 1). The analyst (action 2) has not acted, and
        // the tx carries no recipient wallet — so the ONLY way the analyst's wallet enters the
        // bindings is the pre-baked seed. This is the chicken-and-egg that issue #912 fixes.
        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-citizen",
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 1,
                TransactionType = TransactionType.Action,
                RoutingDecision = Decision(1, 2),
            },
        };

        var resolved = await InstanceProjectionResolver.ResolveAsync(
            tx, PreBakedAnalystResolver("ws-analyst"), NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull();
        // Open applicant late-bound from the tx sender.
        resolved!.Tx.ParticipantBindings.Should().Contain("applicant", "ws-citizen");
        // Pre-baked analyst seeded from the blueprint even though they have not yet acted.
        resolved.Tx.ParticipantBindings.Should().Contain("analyst", "ws-analyst");
    }

    [Fact]
    public async Task ResolveAsync_DoesNotSeedOpenParticipantWithNullWallet()
    {
        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-citizen",
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 1,
                TransactionType = TransactionType.Action,
                RoutingDecision = Decision(1, 2),
            },
        };

        var resolved = await InstanceProjectionResolver.ResolveAsync(
            tx, PreBakedAnalystResolver("ws-analyst"), NullLogger.Instance, CancellationToken.None);

        // The open applicant's binding must come from the tx sender (late-bind), never from a
        // pre-baked seed — there is exactly one applicant entry, the live wallet.
        resolved!.Tx.ParticipantBindings["applicant"].Should().Be("ws-citizen");
    }

    [Fact]
    public async Task ResolveAsync_PreBakedSeed_FlowsIntoInstanceParticipantWallets_MakingItDiscoverable()
    {
        // End-to-end of the two pure pieces: resolve the starting tx, then fold it. The projected
        // instance's ParticipantWallets must contain the analyst wallet so
        // EfCoreInstanceStore.GetPendingActionsByWalletAsync(analystWallet) — which matches on
        // ParticipantWallets — surfaces action 2 for the rules agent the moment action 1 seals.
        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-citizen",
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 1,
                TransactionType = TransactionType.Action,
                RoutingDecision = Decision(1, 2),
            },
        };

        var resolved = await InstanceProjectionResolver.ResolveAsync(
            tx, PreBakedAnalystResolver("ws-analyst"), NullLogger.Instance, CancellationToken.None);

        var instance = InstanceProjection.Project(
            "inst-1", "reg", "bp-1", 1, "tenant", [resolved!.Tx]);

        instance.Should().NotBeNull();
        instance!.CurrentActionIds.Should().Equal(2);
        instance.ParticipantWallets.Should().Contain("analyst", "ws-analyst");
    }

    // ---- Feature 186: the decision (route + reason code) must survive the resolver into the fold. ----
    //
    // These are deliberately written against ResolveAsync rather than a hand-built
    // ProjectedTransaction. The sibling field IsRejection shows why: the fold handles it correctly
    // and InstanceProjectionTests proves that, but NOTHING in src/ ever sets it, so
    // InstanceState.Rejected is unreachable in production and no test noticed. A fold-only test
    // proves the fold; only a test through the resolver proves the join.

    private static RoutingDecision DecisionWithReason(
        int completed, string? routeId, string? reasonCode, params int[] next)
    {
        var decision = Decision(completed, next);
        decision.RouteId = routeId;
        decision.ReasonCode = reasonCode;
        return decision;
    }

    [Fact]
    public async Task ResolveAsync_CarriesRouteIdAndReasonCode_FromTheSignedDecision()
    {
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.Action, DecisionWithReason(2, "route-refuse", "DOC_UNREADABLE")),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Tx.RouteId.Should().Be("route-refuse");
        resolved.Tx.ReasonCode.Should().Be("DOC_UNREADABLE");
    }

    [Fact]
    public async Task ResolveAsync_RouteWithNoReasonCode_CarriesRouteIdOnly()
    {
        // A route may declare a notice with no reasonCodeField — the notice then always resolves to
        // its fallback message. The route id still has to arrive, because it is the only way a
        // reader can find the taken route and learn the outcome was adverse.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.Action, DecisionWithReason(2, "route-refuse", reasonCode: null)),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved!.Tx.RouteId.Should().Be("route-refuse");
        resolved.Tx.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_PreFeature184Decision_CarriesNeither()
    {
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.Action, Decision(2, 3)),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        resolved!.Tx.RouteId.Should().BeNull();
        resolved.Tx.ReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_DecisionFlowsIntoTheProjectedInstance()
    {
        // The join that matters: resolver → fold → instance. This is what the read surface reads.
        var resolved = await InstanceProjectionResolver.ResolveAsync(
            Tx(TransactionType.Action, DecisionWithReason(2, "route-refuse", "DOC_UNREADABLE")),
            NoActionResolver, NullLogger.Instance, CancellationToken.None);

        var instance = InstanceProjection.Project(
            "inst-1", "reg", "bp-1", 1, "tenant", [resolved!.Tx]);

        instance.Should().NotBeNull();
        instance!.DecisionRouteId.Should().Be("route-refuse");
        instance.DecisionReasonCode.Should().Be("DOC_UNREADABLE");
    }
}
