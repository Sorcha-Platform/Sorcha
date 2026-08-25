// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Publishing;

/// <summary>
/// Feature 195 — a definition's identity is assigned by the register, and the Blueprint Service
/// RECORDS it rather than computing it.
/// </summary>
public sealed class PublicationIdentityTests
{
    private const string RegisterId = "reg-195";

    private static BlueprintModel MinimalBlueprint(string id = "bp-195") => new()
    {
        Id = id,
        Title = "Publication identity",
        Description = "A blueprint used to exercise publication identity.",
        CreatedAt = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
        Participants =
        [
            new() { Id = "sender", Name = "Sender" },
            new() { Id = "receiver", Name = "Receiver" }
        ],
        Actions =
        [
            new()
            {
                Id = 0,
                Title = "Submit",
                Sender = "sender",
                IsStartingAction = true,
                Routes = [new() { Id = "done", NextActionIds = [], IsDefault = true }]
            }
        ]
    };

    private static (PublishService Service, List<PublishedBlueprint> Stored) CreateService(
        BlueprintModel blueprint,
        IRegisterServiceClient registerClient)
    {
        var draftStore = new Mock<IBlueprintStore>();
        draftStore.Setup(s => s.GetAsync(blueprint.Id)).ReturnsAsync(blueprint);

        var stored = new List<PublishedBlueprint>();
        var publishedStore = new Mock<IPublishedBlueprintStore>();
        publishedStore.Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .Callback<PublishedBlueprint>(stored.Add)
            .ReturnsAsync((PublishedBlueprint p) => p);
        publishedStore.Setup(s => s.GetVersionsAsync(It.IsAny<string>()))
            .ReturnsAsync(() => stored);

        return (new PublishService(draftStore.Object, publishedStore.Object, registerClient), stored);
    }

    [Fact]
    public async Task PublishAsync_RecordsTheIdentityTheRegisterAssigned()
    {
        var blueprint = MinimalBlueprint();
        var (service, stored) = CreateService(blueprint, FakePublishingRegister.Client());

        var result = await service.PublishAsync(blueprint.Id, RegisterId);

        result.IsSuccess.Should().BeTrue();
        stored.Should().ContainSingle();

        // The recorded id must be the one the register would assign for exactly the bytes stored —
        // that equality is what makes the pin resolvable on a node that only ever replicated.
        var expected = BlueprintPublicationId.ComputeFromDefinition(
            RegisterId, blueprint.Id, JsonSerializer.Serialize(stored[0].Blueprint));

        stored[0].PublicationTxId.Should().Be(expected);
        result.PublishedBlueprint!.PublicationTxId.Should().Be(expected);
    }

    /// <summary>
    /// The ledger is written BEFORE the local store, because the register is what assigns the
    /// identity. A refused publish must therefore leave nothing behind.
    /// </summary>
    [Fact]
    public async Task PublishAsync_RegisterRefuses_StoresNothingLocally()
    {
        var refusing = new Mock<IRegisterServiceClient>();
        refusing.Setup(c => c.PublishBlueprintToRegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlueprintPublicationResult?)null);

        var blueprint = MinimalBlueprint();
        var (service, stored) = CreateService(blueprint, refusing.Object);

        var result = await service.PublishAsync(blueprint.Id, RegisterId);

        result.IsSuccess.Should().BeFalse();
        stored.Should().BeEmpty(
            "a definition recorded locally after a failed ledger write exists on ONE node and " +
            "nowhere else — resolvable here, unresolvable everywhere else, and indistinguishable " +
            "from a healthy publish until something needs it");
    }

    [Fact]
    public async Task PublishAsync_NoRegisterClient_Fails()
    {
        var blueprint = MinimalBlueprint();
        var draftStore = new Mock<IBlueprintStore>();
        draftStore.Setup(s => s.GetAsync(blueprint.Id)).ReturnsAsync(blueprint);
        var publishedStore = new Mock<IPublishedBlueprintStore>();

        var service = new PublishService(draftStore.Object, publishedStore.Object, registerClient: null);

        var result = await service.PublishAsync(blueprint.Id, RegisterId);

        result.IsSuccess.Should().BeFalse(
            "without a register there is nothing to assign an identity, so there is no definition " +
            "to record — this is a misconfiguration, not a degraded mode");
        publishedStore.Verify(s => s.AddAsync(It.IsAny<PublishedBlueprint>()), Times.Never);
    }

    /// <summary>
    /// A behavioural republish is a DIFFERENT definition and must get a different identity.
    /// </summary>
    [Fact]
    public async Task PublishAsync_BehaviouralRepublish_GetsADistinctIdentity()
    {
        var v1 = MinimalBlueprint();
        var (service1, stored1) = CreateService(v1, FakePublishingRegister.Client());
        await service1.PublishAsync(v1.Id, RegisterId);

        // Same blueprint id, behaviourally changed: the action now declares a required field.
        var v2 = MinimalBlueprint();
        v2.Actions[0].DataSchemas =
        [
            JsonDocument.Parse("""{"type":"object","required":["note"],"properties":{"note":{"type":"string"}}}""")
        ];
        var (service2, stored2) = CreateService(v2, FakePublishingRegister.Client());
        await service2.PublishAsync(v2.Id, RegisterId);

        stored2[0].PublicationTxId.Should().NotBe(stored1[0].PublicationTxId,
            "this is the defect #1563 made invisible — a republished definition that shares its " +
            "predecessor's identity is silently dropped by the ledger's idempotency check");

        // The counterfactual for the presentational test below: a BEHAVIOURAL edit must also move
        // the executable-definition hash, or the pair of tests would agree for the wrong reason.
        stored2[0].ExecDefHash.Should().NotBe(stored1[0].ExecDefHash,
            "adding a required field changes what payloads validate, so a fresh rehearsal is owed");
    }

    /// <summary>
    /// A presentational republish is also a distinct PUBLICATION — relabels must ship — while
    /// remaining the same executable definition, so the rehearsal pass stays valid.
    /// </summary>
    [Fact]
    public async Task PublishAsync_PresentationalRepublish_NewIdentity_SameExecutableDefinition()
    {
        var v1 = MinimalBlueprint();
        var (service1, stored1) = CreateService(v1, FakePublishingRegister.Client());
        await service1.PublishAsync(v1.Id, RegisterId);

        var v2 = MinimalBlueprint();
        v2.Actions[0].Title = "Submit your application";   // presentation only
        var (service2, stored2) = CreateService(v2, FakePublishingRegister.Client());
        await service2.PublishAsync(v2.Id, RegisterId);

        stored2[0].PublicationTxId.Should().NotBe(stored1[0].PublicationTxId,
            "a relabel is a new publication, or the new wording never reaches the ledger and " +
            "therefore never reaches a citizen");

        stored2[0].ExecDefHash.Should().Be(stored1[0].ExecDefHash,
            "behaviour did not change, so the F142 rehearsal pass must stay valid — this is the " +
            "one job the executable-definition hash keeps");
    }

    /// <summary>
    /// The identity is register-scoped, so the same definition on two registers is two publications.
    /// </summary>
    [Fact]
    public async Task PublishAsync_SameDefinitionToTwoRegisters_GetsTwoIdentities()
    {
        var a = MinimalBlueprint();
        var (serviceA, storedA) = CreateService(a, FakePublishingRegister.Client());
        await serviceA.PublishAsync(a.Id, "register-one");

        var b = MinimalBlueprint();
        var (serviceB, storedB) = CreateService(b, FakePublishingRegister.Client());
        await serviceB.PublishAsync(b.Id, "register-two");

        // Deliberately byte-identical definitions — the counterfactual has to be executed, or this
        // passes for the wrong reason.
        JsonSerializer.Serialize(storedA[0].Blueprint)
            .Should().Be(JsonSerializer.Serialize(storedB[0].Blueprint));

        storedA[0].PublicationTxId.Should().NotBe(storedB[0].PublicationTxId);
    }
}
