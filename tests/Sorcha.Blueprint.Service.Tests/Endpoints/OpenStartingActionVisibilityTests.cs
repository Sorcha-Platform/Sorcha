// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Xunit;

using BlueprintAction = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintParticipant = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Issue #1446 — the pending-actions inbox was exactly inverted for a Feature 103 OPEN starting
/// action: every participant who could NOT send it was offered it, and the late-bound citizen who
/// could was not.
/// </summary>
/// <remarks>
/// <para>The fixture is the n1 evidence verbatim: a fresh PropertyInspection instance sitting at
/// action 0, "Report Problem", whose sender <c>tenant</c> is open (no wallet in the definition) and
/// unbound (not in <see cref="Instance.ParticipantWallets"/>), alongside three pre-bound org roles.
/// <see cref="TheReportedTable_IsReproducedExactly"/> asserts the whole table in one place, because
/// the defect was only ever visible as a table — each row on its own reads like a defensible
/// choice.</para>
///
/// <para>The two surfaces are asserted to PARTITION the action, not merely to behave: an action that
/// appeared on neither would look like this fix while still leaving agents unable to start, and one
/// that appeared on both would put the noise straight back.</para>
/// </remarks>
public sealed class OpenStartingActionVisibilityTests
{
    private const string TenantWallet = "ws1qtenant00000000000000000000000000000";
    private const string HousingOfficerWallet = "ws1qhousing0000000000000000000000000000";
    private const string ContractorWallet = "ws1qcontractor0000000000000000000000000";
    private const string InspectorWallet = "ws1qinspector00000000000000000000000000";

    private const int ReportProblem = 0;
    private const int TriageJob = 1;

    private static BlueprintModel PropertyInspection() => new()
    {
        Id = "property-inspection",
        Title = "Property Inspection",
        Participants =
        [
            // The open, late-bound citizen — NO wallet in the definition. That is the Feature 103
            // contract, and what VAL_BP_010 exists to keep true.
            new BlueprintParticipant { Id = "tenant", Name = "Tenant" },
            new BlueprintParticipant { Id = "housing-officer", Name = "Housing Officer", WalletAddress = HousingOfficerWallet },
            new BlueprintParticipant { Id = "contractor", Name = "Contractor", WalletAddress = ContractorWallet },
            new BlueprintParticipant { Id = "building-inspector", Name = "Building Inspector", WalletAddress = InspectorWallet },
        ],
        Actions =
        [
            new BlueprintAction { Id = ReportProblem, Title = "Report Problem", Sender = "tenant", IsStartingAction = true },
            new BlueprintAction { Id = TriageJob, Title = "Triage Job", Sender = "housing-officer" },
        ]
    };

    /// <summary>An instance as POST /api/instances creates it: pre-bound roles seeded, open sender absent.</summary>
    private static Instance FreshInstance(params int[] currentActionIds) => new()
    {
        Id = "inst-property-1",
        BlueprintId = "property-inspection",
        BlueprintVersion = 1,
        BlueprintDefinitionTxId = "pub-tx-1",
        RegisterId = "reg-1",
        TenantId = "org-strathcarron",
        State = InstanceState.Active,
        CurrentActionIds = [.. currentActionIds],
        CompletedActionCount = 0,
        ParticipantWallets = new Dictionary<string, string>
        {
            ["housing-officer"] = HousingOfficerWallet,
            ["contractor"] = ContractorWallet,
            ["building-inspector"] = InspectorWallet,
        }
    };

    private static IActionResolverService Resolver(BlueprintModel blueprint)
    {
        var mock = new Mock<IActionResolverService>();
        mock.Setup(r => r.GetActionDefinition(It.IsAny<BlueprintModel>(), It.IsAny<string>()))
            .Returns((BlueprintModel bp, string id) => bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));
        mock.Setup(r => r.GetBlueprintAsync(blueprint.Id, "pub-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        return mock.Object;
    }

    public static TheoryData<string, string> EveryCaller() => new()
    {
        { "tenant (the open sender, unbound)", TenantWallet },
        { "housing-officer", HousingOfficerWallet },
        { "contractor", ContractorWallet },
        { "building-inspector", InspectorWallet },
    };

    [Theory]
    [MemberData(nameof(EveryCaller))]
    public void NobodysPersonalInbox_CarriesAnOpenStartingAction(string who, string wallet)
    {
        var blueprint = PropertyInspection();
        var instance = FreshInstance(ReportProblem);

        var offered = EfCoreInstanceStore.IsActionForWallet(
            blueprint, Resolver(blueprint), instance, ReportProblem, wallet);

        offered.Should().BeFalse(
            "{0} cannot be assigned an action whose sender has not been late-bound to anyone", who);
    }

    [Fact]
    public void TheReportedTable_IsReproducedExactly()
    {
        var blueprint = PropertyInspection();
        var resolver = Resolver(blueprint);
        // Both the starting action and a pre-bound follow-on are current at once, so the assertions
        // below distinguish "hides the open action" from "hides everything".
        var instance = FreshInstance(ReportProblem, TriageJob);

        List<string> InboxOf(string wallet) => instance.CurrentActionIds
            .Where(a => EfCoreInstanceStore.IsActionForWallet(blueprint, resolver, instance, a, wallet))
            .Select(a => blueprint.Actions.First(x => x.Id == a).Title!)
            .ToList();

        InboxOf(TenantWallet).Should().BeEmpty();
        // ContainSingle().Which, not Equal(...) — Equal's params overload swallows a "because"
        // string as another expected element.
        InboxOf(HousingOfficerWallet).Should().ContainSingle(
            "the housing officer keeps their OWN action — n1 listed seven copies of the tenant's instead")
            .Which.Should().Be("Triage Job");
        InboxOf(ContractorWallet).Should().BeEmpty();
        InboxOf(InspectorWallet).Should().BeEmpty();
    }

    [Fact]
    public void TheOpenStartingSurface_CarriesIt()
    {
        var blueprint = PropertyInspection();

        ActionEndpoints.IsUnboundOpenSender(blueprint, Resolver(blueprint), FreshInstance(ReportProblem), ReportProblem)
            .Should().BeTrue("an unbound open starting action is exactly what this surface exists to publish");
    }

    [Fact]
    public void TheTwoSurfaces_PartitionEveryCurrentAction()
    {
        var blueprint = PropertyInspection();
        var resolver = Resolver(blueprint);
        var instance = FreshInstance(ReportProblem, TriageJob);
        var everyWallet = new[] { TenantWallet, HousingOfficerWallet, ContractorWallet, InspectorWallet };

        foreach (var actionId in instance.CurrentActionIds)
        {
            var inSomeonesInbox = everyWallet.Any(w =>
                EfCoreInstanceStore.IsActionForWallet(blueprint, resolver, instance, actionId, w));
            var onTheOpenSurface = ActionEndpoints.IsUnboundOpenSender(blueprint, resolver, instance, actionId);

            (inSomeonesInbox ^ onTheOpenSurface).Should().BeTrue(
                "action {0} must be discoverable on exactly one of the two surfaces — on neither and it is "
                + "unreachable, on both and the noise is back", actionId);
        }
    }

    [Fact]
    public void OnceLateBound_TheCitizensOwnInboxCarriesIt_AndTheOpenSurfaceDoesNot()
    {
        var blueprint = PropertyInspection();
        var resolver = Resolver(blueprint);
        var instance = FreshInstance(ReportProblem);
        instance.ParticipantWallets["tenant"] = TenantWallet;   // the late-bind the projector folds

        EfCoreInstanceStore.IsActionForWallet(blueprint, resolver, instance, ReportProblem, TenantWallet)
            .Should().BeTrue("after binding it is ordinary assigned work");
        EfCoreInstanceStore.IsActionForWallet(blueprint, resolver, instance, ReportProblem, HousingOfficerWallet)
            .Should().BeFalse();
        ActionEndpoints.IsUnboundOpenSender(blueprint, resolver, instance, ReportProblem)
            .Should().BeFalse("a bound action is no longer awaiting anyone to start it");
    }

    [Fact]
    public void AnUnresolvableDefinition_StaysInclusive_RatherThanHidingRealWork()
    {
        // Case 3: a replica that has not finished replicating the blueprint. Unknown is not "open".
        EfCoreInstanceStore.IsActionForWallet(
            blueprint: null, actionResolver: null, FreshInstance(TriageJob), TriageJob, HousingOfficerWallet)
            .Should().BeTrue();
    }
}
