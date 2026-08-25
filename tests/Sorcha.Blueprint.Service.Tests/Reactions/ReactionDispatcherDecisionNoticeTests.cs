// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Wallet;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Reactions;

/// <summary>
/// Feature 184 — the decision notice fires from the <b>fold</b>, on whichever node hosts the
/// recipient's wallet, and never from the node that merely processed the decider's submission.
/// <para>
/// Note what the dispatcher is given here: the instance, the sealed transaction's metadata, and the
/// blueprint. It is given <b>no payload access at all</b> — deliberately, because on the recipient's
/// node there is none to be had (a background fold holds no delegation token and cannot decrypt a
/// disclosure group). Everything the citizen reads is resolved from the signed reason code plus the
/// replicated blueprint's catalogue.
/// </para>
/// </summary>
public class ReactionDispatcherDecisionNoticeTests
{
    private const string CitizenWallet = "ws-citizen-1";
    private const string AgentWallet = "ws-agent-2";
    private const string SealedTxId = "tx-reject-1";
    private const string RejectRouteId = "rejected-terminal";
    private const string ClaimRouteId = "approved-to-claim";

    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<INotificationService> _notifier = new();
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly IAtomicDistributedCache _claims = new InMemoryAtomicDistributedCache();
    private readonly ReactionDispatcherMetrics _metrics;

    public ReactionDispatcherDecisionNoticeTests()
    {
        var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new ReactionDispatcherMetrics(provider.GetRequiredService<IMeterFactory>());

        // The real resolver reads the blueprint the node already holds (published on the register).
        _actionResolver
            .Setup(r => r.GetActionDefinition(It.IsAny<BlueprintModel>(), It.IsAny<string>()))
            .Returns((BlueprintModel bp, string id) =>
                bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));
    }

    private ReactionDispatcher CreateDispatcher() => new(
        _walletClient.Object,
        _claims,
        _metrics,
        NullLogger<ReactionDispatcher>.Instance,
        _actionResolver.Object,
        _notifier.Object);

    private void HostsWallet(string address) =>
        _walletClient.Setup(w => w.GetWalletAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = address,
                Name = "Citizen",
                PublicKey = "pk",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "owner",
                Tenant = "tenant-1",
            });

    private void DoesNotHostWallet(string address) =>
        _walletClient.Setup(w => w.GetWalletAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

    private void PublishBlueprint(params Route[] routes)
    {
        var blueprint = new BlueprintModel
        {
            Title = "AIAS Assured Identity",
            Participants = [new Participant { Id = "citizen" }, new Participant { Id = "analyst" }],
            Actions =
            [
                new ActionModel { Id = 2, Title = "Verify application", Sender = "analyst", Routes = routes.ToList() },
                new ActionModel { Id = 3, Title = "Claim credential", Sender = "citizen" },
            ],
        };

        _actionResolver.Setup(r => r.GetBlueprintAsync("bp-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
    }

    private static Route RejectRouteWithNotice() => new()
    {
        Id = RejectRouteId,
        NextActionIds = [],
        DecisionNotice = new DecisionNotice
        {
            RecipientParticipantId = "citizen",
            ReasonCodeField = "/reasonCode",
            Title = "AIAS could not assure your identity",
            Severity = "Warning",
            Reasons = new Dictionary<string, string>
            {
                ["postcode-not-found"] = "AIAS could not locate that address on any map.",
                ["profanity"] = "AIAS does not assure identities described in such colourful terms.",
            },
            FallbackMessage = "Your application was not approved.",
        },
    };

    /// <summary>A sealed transaction carrying the signed routing decision — the whole of the input.</summary>
    private static TransactionModel SealedDecision(string? routeId, string? reasonCode, int completedActionId = 2,
        params int[] nextActionIds) => new()
        {
            TxId = SealedTxId,
            SenderWallet = AgentWallet,
            MetaData = new TransactionMetaData
            {
                BlueprintId = "bp-1",
                InstanceId = "inst-1",
                ActionId = (uint)completedActionId,
                TransactionType = TransactionType.Action,
                RoutingDecision = new RoutingDecision
                {
                    CompletedActionId = completedActionId,
                    BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
                    NextActions = nextActionIds.Select(id => new ActionRef { ActionId = id }).ToList(),
                    RouteId = routeId,
                    ReasonCode = reasonCode,
                    Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "sig" },
                },
            },
        };

    private static Instance FoldedInstance(params int[] currentActions) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1",
        CurrentActionIds = currentActions.ToList(),
        ParticipantWallets = new Dictionary<string, string>
        {
            ["citizen"] = CitizenWallet,
            ["analyst"] = AgentWallet,
        },
    };

    [Fact]
    public async Task DecisionNotice_RecipientWalletHostedHere_WritesTheNoticeOnce()
    {
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision(RejectRouteId, "postcode-not-found"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            CitizenWallet, "inst-1", "2",
            "AIAS could not assure your identity",
            "AIAS could not locate that address on any map.",
            "Warning",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecisionNotice_RecipientWalletNotHostedHere_DoesNotFire()
    {
        // The deciding agent's node: it folds the very same sealed transaction, but the citizen's
        // wallet lives elsewhere. Delivery is the citizen's node's job — this node must stay silent.
        PublishBlueprint(RejectRouteWithNotice());
        DoesNotHostWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision(RejectRouteId, "postcode-not-found"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecisionNotice_SameSealedTxFoldedTwice_WritesExactlyOnce()
    {
        // Replay, restart, or a full instance rebuild re-folds the same sealed transaction.
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(FoldedInstance(), SealedDecision(RejectRouteId, "profanity"), default);
        await dispatcher.DispatchAsync(FoldedInstance(), SealedDecision(RejectRouteId, "profanity"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            CitizenWallet, "inst-1", "2", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecisionNotice_UnknownReasonCode_FallsBackToTheDeclaredMessage()
    {
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision(RejectRouteId, "some-code-the-blueprint-never-heard-of"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            CitizenWallet, "inst-1", "2", It.IsAny<string>(),
            "Your application was not approved.", It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecisionNotice_NoReasonCodeCarried_FallsBackToTheDeclaredMessage()
    {
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision(RejectRouteId, reasonCode: null), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            CitizenWallet, "inst-1", "2", It.IsAny<string>(),
            "Your application was not approved.", It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecisionNotice_TakenRouteCarriesNoNotice_DoesNotFire()
    {
        // The approve route: the credential's arrival is the notification. No decision notice.
        PublishBlueprint(
            new Route { Id = ClaimRouteId, NextActionIds = [3] },
            RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(3), SealedDecision(ClaimRouteId, reasonCode: null, completedActionId: 2, nextActionIds: 3),
            default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecisionNotice_RouteIdNotInTheBlueprint_DoesNotFire()
    {
        // Blueprint version drift: the decision names a route this node's blueprint does not have.
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision("a-route-from-another-version", "profanity"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecisionNotice_NoRoutingDecisionCarried_DoesNotFire()
    {
        // A transaction sealed before Feature 184 carries no route id.
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(), SealedDecision(routeId: null, reasonCode: null), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecisionNotice_RecipientParticipantUnbound_DoesNotFire()
    {
        PublishBlueprint(RejectRouteWithNotice());
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        var instance = FoldedInstance();
        instance.ParticipantWallets.Remove("citizen");

        await CreateDispatcher().DispatchAsync(instance, SealedDecision(RejectRouteId, "profanity"), default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DecisionNotice_NonTerminalRoute_StillFires()
    {
        // "Returned for more information": the workflow continues, and the applicant is still told why.
        var returnRoute = new Route
        {
            Id = "returned-for-info",
            NextActionIds = [3],
            DecisionNotice = new DecisionNotice
            {
                RecipientParticipantId = "citizen",
                ReasonCodeField = "/reasonCode",
                Title = "We need more information",
                Reasons = new Dictionary<string, string> { ["portrait-unclear"] = "Your photo was too blurry to use." },
                FallbackMessage = "We need more information before we can continue.",
            },
        };
        PublishBlueprint(returnRoute);
        HostsWallet(CitizenWallet);
        HostsWallet(AgentWallet);

        await CreateDispatcher().DispatchAsync(
            FoldedInstance(3),
            SealedDecision("returned-for-info", "portrait-unclear", completedActionId: 2, nextActionIds: 3),
            default);

        _notifier.Verify(n => n.NotifyDecisionAsync(
            CitizenWallet, "inst-1", "2", "We need more information",
            "Your photo was too blurry to use.", It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
