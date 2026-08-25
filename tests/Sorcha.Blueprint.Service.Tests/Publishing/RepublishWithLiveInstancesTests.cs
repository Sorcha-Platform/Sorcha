// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Publishing;

/// <summary>
/// Feature 194 Story 2 — a publisher upgrades freely, and a live instance is not disturbed.
/// </summary>
/// <remarks>
/// Without this half, Story 1 could be satisfied by simply refusing to republish — which would make
/// long-running workflows unupgradable, since they may never have a quiet moment. Safety and
/// usability are the same requirement here, and only both together are worth having.
/// </remarks>
public class RepublishWithLiveInstancesTests
{
    [Fact]
    public async Task RepublishingWhileAnInstanceIsInFlight_Succeeds_AndLeavesThatInstanceUntouched()
    {
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        var service = new PublishService(draftStore, publishedStore, FakePublishingRegister.Client());

        var v1 = await service.PublishAsync(blueprint.Id, "register-1");
        var pinnedToV1 = v1.PublishedBlueprint!.ExecDefHash;

        // An instance mid-flow on v1, exactly as the projector would have folded it.
        var live = InstanceProjection.Project(
            "inst-live", "register-1", blueprint.Id, 1, "tenant-1",
        [
            new ProjectedTransaction(
                TxId: "tx1", PreviousTransactionId: null, CompletedActionId: 1,
                NextActionIds: [2], ParticipantBindings: new Dictionary<string, string>(),
                BlueprintDefinitionTxId: pinnedToV1),
        ])!;

        var stateBefore = live.State;
        var actionsBefore = live.CurrentActionIds.ToList();

        // A BEHAVIOURAL republish while that instance is live.
        var draft = await draftStore.GetAsync(blueprint.Id);
        draft!.Actions.Add(new ActionModel { Id = 3, Title = "Extra step", Sender = "p2" });
        var v2 = await service.PublishAsync(blueprint.Id, "register-1");

        v2.IsSuccess.Should().BeTrue(
            "an upgrade must never be blocked, warned against, or delayed because instances are live");
        v2.PublishedBlueprint!.ExecDefHash.Should().NotBe(pinnedToV1);

        live.BlueprintDefinitionTxId.Should().Be(pinnedToV1, "the pin is immutable");
        live.State.Should().Be(stateBefore);
        live.CurrentActionIds.Should().Equal(actionsBefore);
    }

    [Fact]
    public async Task BothDefinitions_RemainResolvableOnTheRegisterAtTheSameTime()
    {
        // Story 2's other half: two definitions govern instances on one register simultaneously,
        // and neither disturbs the other.
        var draftStore = new InMemoryBlueprintStore();
        var publishedStore = new InMemoryPublishedBlueprintStore();
        var blueprint = CreateBlueprint();
        await draftStore.AddAsync(blueprint);
        var service = new PublishService(draftStore, publishedStore, FakePublishingRegister.Client());

        var v1 = await service.PublishAsync(blueprint.Id, "register-1");

        var draft = await draftStore.GetAsync(blueprint.Id);
        draft!.Actions.Add(new ActionModel { Id = 3, Title = "Extra step", Sender = "p2" });
        var v2 = await service.PublishAsync(blueprint.Id, "register-1");

        var resolvedV1 = await publishedStore.GetByPublicationAsync(
            blueprint.Id, v1.PublishedBlueprint!.PublicationTxId);
        var resolvedV2 = await publishedStore.GetByPublicationAsync(
            blueprint.Id, v2.PublishedBlueprint!.PublicationTxId);

        resolvedV1.Should().NotBeNull();
        resolvedV2.Should().NotBeNull();
        resolvedV1!.Blueprint.Actions.Should().HaveCount(2);
        resolvedV2!.Blueprint.Actions.Should().HaveCount(3);
    }

    private static BlueprintModel CreateBlueprint() => new()
    {
        Id = "bp-upgrade-1",
        Title = "Upgradable Workflow",
        Description = "Exercises republishing while an instance is in flight.",
        Participants =
        [
            new ParticipantModel { Id = "p1", Name = "Applicant" },
            new ParticipantModel { Id = "p2", Name = "Officer" }
        ],
        Actions =
        [
            new ActionModel { Id = 1, Title = "Apply", Sender = "p1", IsStartingAction = true },
            new ActionModel { Id = 2, Title = "Decide", Sender = "p2" }
        ]
    };
}
