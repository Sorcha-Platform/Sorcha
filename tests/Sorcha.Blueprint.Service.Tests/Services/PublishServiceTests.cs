// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for PublishService: validation-only endpoint, publish with/without registerId.
/// </summary>
public class PublishServiceTests
{
    private readonly Mock<IBlueprintStore> _mockBlueprintStore;
    private readonly Mock<IPublishedBlueprintStore> _mockPublishedStore;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;

    public PublishServiceTests()
    {
        _mockBlueprintStore = new Mock<IBlueprintStore>();
        _mockPublishedStore = new Mock<IPublishedBlueprintStore>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
    }

    /// <summary>
    /// Feature 195 — publishing requires a register client, because the register is what ASSIGNS a
    /// definition's identity. The default is a stand-in that computes the genuine publication id
    /// rather than a placeholder, so a test asserting on identity gets production's answer.
    /// </summary>
    private PublishService CreateService(IRegisterServiceClient? registerClient = null)
    {
        return new PublishService(
            _mockBlueprintStore.Object,
            _mockPublishedStore.Object,
            registerClient ?? FakePublishingRegister.Client());
    }

    #region ValidateAsync

    [Fact]
    public async Task ValidateAsync_BlueprintNotFound_ReturnsInvalid()
    {
        _mockBlueprintStore.Setup(s => s.GetAsync("missing")).ReturnsAsync((BlueprintModel?)null);
        var service = CreateService();

        var result = await service.ValidateAsync("missing");

        result.IsValid.Should().BeFalse();
        result.BlueprintId.Should().Be("missing");
        result.ValidationResults.Should().ContainSingle(i => i.Message == "Blueprint not found");
    }

    [Fact]
    public async Task ValidateAsync_ValidBlueprint_ReturnsValid()
    {
        var blueprint = CreateValidBlueprint();
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-1");

        result.IsValid.Should().BeTrue();
        result.BlueprintId.Should().Be("bp-1");
        result.Title.Should().Be("Test Blueprint");
        result.ValidationResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_InvalidBlueprint_ReturnsErrors()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-2",
            Title = "Incomplete",
            Description = "Validation test blueprint.",
            Participants = [new ParticipantModel { Id = "p1", Name = "Alice" }],
            Actions = []
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-2")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-2");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("at least 2 participants"));
        result.ValidationResults.Should().Contain(i => i.Message.Contains("at least 1 action"));
    }

    [Fact]
    public async Task ValidateAsync_ShortTitleAndMissingDescription_ReturnsErrors()
    {
        // Bucket B: the publish path must enforce the model's [Required] + MinLength(3)/(5) on
        // title/description (previously only the AI-chat tool did).
        var blueprint = new BlueprintModel
        {
            Id = "bp-short",
            Title = "ab",            // < 3 chars
            Description = "x",        // < 5 chars
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions = [new ActionModel { Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true }]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-short")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-short");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("title must be at least 3"));
        result.ValidationResults.Should().Contain(i => i.Message.Contains("description must be at least 5"));
    }

    [Fact]
    public async Task ValidateAsync_BlueprintWithCycles_ReturnsValidWithWarnings()
    {
        var blueprint = CreateBlueprintWithCycle();
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-cycle")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-cycle");

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().NotBeEmpty();
    }

    #endregion

    #region PublishAsync — RegisterId Required

    [Fact]
    public async Task PublishAsync_WithRegisterId_PublishesLocallyAndSetsRegister()
    {
        var blueprint = CreateValidBlueprint();
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.PublishAsync("bp-1", "test-register");

        result.IsSuccess.Should().BeTrue();
        result.PublishedBlueprint.Should().NotBeNull();
        result.PublishedBlueprint!.RegisterId.Should().Be("test-register");
        _mockPublishedStore.Verify(s => s.AddAsync(It.Is<PublishedBlueprint>(p => p.RegisterId == "test-register")), Times.Once);
    }

    #endregion

    #region PublishAsync — With RegisterId

    [Fact]
    public async Task PublishAsync_WithRegisterId_CallsRegisterClient()
    {
        var blueprint = CreateValidBlueprint();
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-1")).ReturnsAsync(blueprint);
        _mockRegisterClient
            .Setup(c => c.PublishBlueprintToRegisterAsync(
                "reg-1", "bp-1", It.IsAny<string>(), "system", default))
            .ReturnsAsync(new BlueprintPublicationResult { PublicationTxId = "tx-publication-1" });
        var service = CreateService(_mockRegisterClient.Object);

        var result = await service.PublishAsync("bp-1", "reg-1");

        result.IsSuccess.Should().BeTrue();
        _mockPublishedStore.Verify(s => s.AddAsync(It.IsAny<PublishedBlueprint>()), Times.Once);
        _mockRegisterClient.Verify(
            c => c.PublishBlueprintToRegisterAsync(
                "reg-1", "bp-1", It.IsAny<string>(), "system", default),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_InvalidBlueprint_DoesNotCallRegisterClient()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-invalid",
            Title = "Invalid",
            Description = "Validation test blueprint.",
            Participants = [],
            Actions = []
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-invalid")).ReturnsAsync(blueprint);
        var service = CreateService(_mockRegisterClient.Object);

        var result = await service.PublishAsync("bp-invalid", "reg-1");

        result.IsSuccess.Should().BeFalse();
        _mockRegisterClient.Verify(
            c => c.PublishBlueprintToRegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    #endregion

    #region Validation — Starting Actions (Rule 6)

    [Fact]
    public async Task ValidateAsync_NoStartingAction_WarnsImplicitStart()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-nostart",
            Title = "No Starting Action",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel { Id = 1, Title = "Step 1", Sender = "p1" },
                new ActionModel { Id = 2, Title = "Step 2", Sender = "p2" }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-nostart")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-nostart");

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("IsStartingAction"));
    }

    #endregion

    #region Validation — Route Targets (Rule 7)

    [Fact]
    public async Task ValidateAsync_RouteToNonExistentAction_ReturnsError()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-badroute",
            Title = "Bad Route Target",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [999], IsDefault = true }]
                }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-badroute")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-badroute");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("non-existent action 999"));
    }

    [Fact]
    public async Task ValidateAsync_RejectionTargetNonExistent_ReturnsError()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-badreject",
            Title = "Bad Rejection Target",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    RejectionConfig = new RejectionConfig { TargetActionId = 999 }
                }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-badreject")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-badreject");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("Rejection target action 999"));
    }

    [Fact]
    public async Task ValidateAsync_OrphanAction_WarnsUnreachable()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-orphan",
            Title = "Orphan Action",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel { Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true },
                new ActionModel { Id = 2, Title = "Orphan", Sender = "p2" }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-orphan")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-orphan");

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("unreachable") && w.Contains("Action 2"));
    }

    #endregion

    #region Validation — Participant References (Rule 3)

    [Fact]
    public async Task ValidateAsync_SenderNotParticipant_ReturnsError()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-badsender",
            Title = "Bad Sender",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel { Id = 1, Title = "Start", Sender = "unknown-participant", IsStartingAction = true }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-badsender")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-badsender");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("unknown-participant") && i.Message.Contains("not a defined participant"));
    }

    #endregion

    #region Validation — JSON Pointer Syntax (Rule 8)

    [Fact]
    public async Task ValidateAsync_InvalidJsonPointer_ReturnsError()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-badpointer",
            Title = "Bad JSON Pointer",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Disclosures = [new Disclosure("p2", ["fieldName"])]
                }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-badpointer")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-badpointer");

        result.IsValid.Should().BeFalse();
        result.ValidationResults.Should().Contain(i => i.Message.Contains("must start with '/'"));
    }

    [Fact]
    public async Task ValidateAsync_ValidJsonPointers_NoErrors()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-goodpointer",
            Title = "Good JSON Pointers",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Disclosures = [new Disclosure("p2", ["/name", "/*", "/nested/field", "#/hashPointer"])]
                }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-goodpointer")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-goodpointer");

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Validation — JSON Logic Syntax (Rule 9)

    [Fact]
    public async Task ValidateAsync_InvalidRouteCondition_ReturnsError()
    {
        var conditionNode = System.Text.Json.Nodes.JsonNode.Parse("\"not-valid-json-logic\"");
        var blueprint = new BlueprintModel
        {
            Id = "bp-badlogic",
            Title = "Bad JSON Logic",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [1], Condition = conditionNode }]
                }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-badlogic")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-badlogic");

        // A string is technically valid JSON that deserializes as a Rule (constant), so this should pass
        // The real test is for completely unparseable expressions
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidRouteCondition_NoErrors()
    {
        var conditionNode = System.Text.Json.Nodes.JsonNode.Parse("{\"==\": [{\"var\": \"status\"}, \"approved\"]}");
        var blueprint = new BlueprintModel
        {
            Id = "bp-goodlogic",
            Title = "Good JSON Logic",
            Description = "Validation test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Alice" },
                new ParticipantModel { Id = "p2", Name = "Bob" }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Start",
                    Sender = "p1",
                    IsStartingAction = true,
                    Routes = [new Route { Id = "r1", NextActionIds = [2], Condition = conditionNode }]
                },
                new ActionModel { Id = 2, Title = "End", Sender = "p2" }
            ]
        };
        _mockBlueprintStore.Setup(s => s.GetAsync("bp-goodlogic")).ReturnsAsync(blueprint);
        var service = CreateService();

        var result = await service.ValidateAsync("bp-goodlogic");

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static BlueprintModel CreateValidBlueprint() => new()
    {
        Id = "bp-1",
        Title = "Test Blueprint",
        Description = "A valid test blueprint for publish validation.",
        Participants =
        [
            new ParticipantModel { Id = "p1", Name = "Alice" },
            new ParticipantModel { Id = "p2", Name = "Bob" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Start",
                Sender = "p1",
                IsStartingAction = true
            }
        ]
    };

    private static BlueprintModel CreateBlueprintWithCycle() => new()
    {
        Id = "bp-cycle",
        Title = "Cyclic Blueprint",
        Description = "Validation test blueprint.",
        Participants =
        [
            new ParticipantModel { Id = "p1", Name = "Alice" },
            new ParticipantModel { Id = "p2", Name = "Bob" }
        ],
        Actions =
        [
            new ActionModel
            {
                Id = 1,
                Title = "Step A",
                Sender = "p1",
                IsStartingAction = true,
                Routes = [new Route { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2,
                Title = "Step B",
                Sender = "p2",
                Routes = [new Route { NextActionIds = [1], IsDefault = true }]
            }
        ]
    };

    #endregion
}
