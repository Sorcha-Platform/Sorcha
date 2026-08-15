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

    // ---- The recipient-derived binding must never overwrite an authoritative one. ----
    //
    // The resolver binds "the next action's sender" to a wallet picked out of the transaction's
    // RecipientsWallets. That is a GUESS: a transaction fans out to every participant a disclosure
    // names, so "the first recipient that isn't the sender" is whoever happens to be first in a list
    // whose order carries no meaning. Where the blueprint already states the participant's wallet,
    // or the participant just signed the transaction being folded, the guess is not merely
    // unnecessary — it silently replaces a fact with a coin toss, and the fold's last-writer-wins
    // merge makes the coin toss the value the instance keeps.
    //
    // Found live on n1 by the TradeFinance walkthrough (#1427): actions 2 and 3 share a sender
    // (sales-mgr). Folding action 2 bound sales-mgr to the FIRST recipient of their own
    // transaction — procurement-mgr's wallet — so sales-mgr, the one participant who had to act
    // next, was refused their own instance with "You are not a participant on this instance."

    /// <summary>
    /// The TradeFinance shape, from the real sealed ledger: two consecutive actions with the SAME
    /// pre-bound sender, and a transaction that fans out to three recipients.
    /// </summary>
    private static IActionResolverService ConsecutiveSameSenderResolver()
    {
        var blueprint = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "bp-1",
            Participants =
            [
                new Participant { Id = "procurement-mgr", Name = "Procurement Manager", WalletAddress = null },
                new Participant { Id = "sales-mgr", Name = "Sales Manager", WalletAddress = "ws-sales" },
                new Participant { Id = "site-mgr", Name = "Site Manager", WalletAddress = "ws-site" },
            ],
            Actions =
            [
                new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "procurement-mgr", IsStartingAction = true },
                new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "sales-mgr" },
                new Sorcha.Blueprint.Models.Action { Id = 3, Sender = "sales-mgr" },
                new Sorcha.Blueprint.Models.Action { Id = 4, Sender = "site-mgr" },
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
    public async Task ResolveAsync_ConsecutiveActionsBySameSender_DoesNotRebindThatSenderToARecipient()
    {
        // Action 2, sent by sales-mgr, routes to action 3 — also sent by sales-mgr. The recipient
        // list is exactly what the ledger held on n1: procurement-mgr first, then the sender, then
        // site-mgr.
        var tx = new TransactionModel
        {
            TxId = "tx-action2",
            PrevTxId = "tx-action1",
            SenderWallet = "ws-sales",
            RecipientsWallets = ["ws-procurement", "ws-sales", "ws-site"],
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 2,
                TransactionType = TransactionType.Action,
                RoutingDecision = Decision(2, 3),
            },
        };

        var resolved = await InstanceProjectionResolver.ResolveAsync(
            tx, ConsecutiveSameSenderResolver(), NullLogger.Instance, CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Tx.ParticipantBindings["sales-mgr"].Should().Be(
            "ws-sales",
            "sales-mgr signed this transaction and the blueprint binds them — neither fact may be "
            + "displaced by whichever recipient happens to be first in the fan-out list");
    }

    [Fact]
    public async Task ResolveAsync_NextActionSenderIsPreBoundInBlueprint_KeepsTheBlueprintWallet()
    {
        // Action 1 (open starter) routes to action 4, whose sender site-mgr IS pre-bound in the
        // blueprint. The blueprint is authoritative; the recipient guess must not displace it.
        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-procurement",
            RecipientsWallets = ["ws-sales", "ws-procurement", "ws-site"],
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = 1,
                TransactionType = TransactionType.Action,
                RoutingDecision = Decision(1, 4),
            },
        };

        var resolved = await InstanceProjectionResolver.ResolveAsync(
            tx, ConsecutiveSameSenderResolver(), NullLogger.Instance, CancellationToken.None);

        resolved!.Tx.ParticipantBindings["site-mgr"].Should().Be(
            "ws-site",
            "the blueprint states site-mgr's wallet; 'first recipient that isn't the sender' is a guess");
        // The open starter still late-binds from the signed sender.
        resolved.Tx.ParticipantBindings["procurement-mgr"].Should().Be("ws-procurement");
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousRecipients_DoesNotBindAnOpenNextSender()
    {
        // The next action's sender is genuinely open (no blueprint wallet, has not acted) — but the
        // transaction fans out to TWO candidates, so "the recipient" does not identify anyone.
        // Binding one at random is worse than leaving the participant unbound: they bind
        // authoritatively the moment they act, whereas a wrong binding locks them out until then.
        var blueprint = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "bp-1",
            Participants =
            [
                new Participant { Id = "starter", Name = "Starter", WalletAddress = "ws-starter" },
                new Participant { Id = "open-next", Name = "Open Next", WalletAddress = null },
            ],
            Actions =
            [
                new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "starter", IsStartingAction = true },
                new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "open-next" },
            ],
        };
        var mock = new Mock<IActionResolverService>();
        mock.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        mock.Setup(r => r.GetActionDefinition(blueprint, It.IsAny<string>()))
            .Returns((Sorcha.Blueprint.Models.Blueprint bp, string id) => bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));

        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-starter",
            RecipientsWallets = ["ws-candidate-a", "ws-starter", "ws-candidate-b"],
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
            tx, mock.Object, NullLogger.Instance, CancellationToken.None);

        resolved!.Tx.ParticipantBindings.Should().NotContainKey(
            "open-next",
            "two non-sender recipients means the fan-out does not identify the next actor");
    }

    [Fact]
    public async Task ResolveAsync_SingleRecipientHandOff_StillBindsAnOpenNextSender()
    {
        // The case the recipient rule exists for, and which must keep working: a hand-off to a
        // genuinely open next participant, with exactly ONE non-sender recipient naming them.
        var blueprint = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "bp-1",
            Participants =
            [
                new Participant { Id = "starter", Name = "Starter", WalletAddress = "ws-starter" },
                new Participant { Id = "open-next", Name = "Open Next", WalletAddress = null },
            ],
            Actions =
            [
                new Sorcha.Blueprint.Models.Action { Id = 1, Sender = "starter", IsStartingAction = true },
                new Sorcha.Blueprint.Models.Action { Id = 2, Sender = "open-next" },
            ],
        };
        var mock = new Mock<IActionResolverService>();
        mock.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<CancellationToken>())).ReturnsAsync(blueprint);
        mock.Setup(r => r.GetActionDefinition(blueprint, It.IsAny<string>()))
            .Returns((Sorcha.Blueprint.Models.Blueprint bp, string id) => bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));

        var tx = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-starter",
            RecipientsWallets = ["ws-starter", "ws-the-next-actor"],
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
            tx, mock.Object, NullLogger.Instance, CancellationToken.None);

        resolved!.Tx.ParticipantBindings["open-next"].Should().Be("ws-the-next-actor");
    }

    [Fact]
    public async Task RecipientRebind_WouldLockTheNextActorOutOfTheirOwnInstance()
    {
        // The user-visible consequence, asserted end to end through the fold: the instance's
        // ParticipantWallets is exactly what InstanceParticipantGate and
        // GetPendingActionsByWalletAsync match on. If sales-mgr's entry holds someone else's
        // wallet, sales-mgr cannot read the instance and it never appears in their pending list —
        // while the instance sits waiting for them.
        var action1 = new TransactionModel
        {
            TxId = "tx-action1",
            PrevTxId = string.Empty,
            SenderWallet = "ws-procurement",
            RecipientsWallets = ["ws-procurement", "ws-sales", "ws-site"],
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1", InstanceId = "inst-1", ActionId = 1,
                TransactionType = TransactionType.Action, RoutingDecision = Decision(1, 2),
            },
        };
        var action2 = new TransactionModel
        {
            TxId = "tx-action2",
            PrevTxId = "tx-action1",
            SenderWallet = "ws-sales",
            RecipientsWallets = ["ws-procurement", "ws-sales", "ws-site"],
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1", InstanceId = "inst-1", ActionId = 2,
                TransactionType = TransactionType.Action, RoutingDecision = Decision(2, 3),
            },
        };

        var resolver = ConsecutiveSameSenderResolver();
        var r1 = await InstanceProjectionResolver.ResolveAsync(action1, resolver, NullLogger.Instance, CancellationToken.None);
        var r2 = await InstanceProjectionResolver.ResolveAsync(action2, resolver, NullLogger.Instance, CancellationToken.None);

        var instance = InstanceProjection.Project(
            "inst-1", "reg", "bp-1", 1, "tenant", [r1!.Tx, r2!.Tx]);

        instance!.CurrentActionIds.Should().Equal(3);
        instance.ParticipantWallets["sales-mgr"].Should().Be(
            "ws-sales",
            "action 3 is waiting for sales-mgr; the participant map is what authorises them to see it");
    }
}
