// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;
using RejectionConfigModel = Sorcha.Blueprint.Models.RejectionConfig;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for graph cycle detection in blueprint publish validation.
/// Cycles produce warnings (not errors) — cyclic blueprints publish successfully.
/// Exercises the DetectCycles/DfsCycleDetect methods through PublishService.PublishAsync().
/// </summary>
public class CycleDetectionTests
{
    private readonly Mock<IBlueprintStore> _mockBlueprintStore;
    private readonly Mock<IPublishedBlueprintStore> _mockPublishedStore;
    private readonly PublishService _publishService;

    public CycleDetectionTests()
    {
        _mockBlueprintStore = new Mock<IBlueprintStore>();
        _mockPublishedStore = new Mock<IPublishedBlueprintStore>();
        _publishService = new PublishService(_mockBlueprintStore.Object, _mockPublishedStore.Object, FakePublishingRegister.Client());
    }

    [Fact]
    public async Task PublishAsync_LinearGraph_ReturnsSuccess()
    {
        // Arrange: 1 → 2 → 3 (no cycles)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Middle", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [3], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 3, Title = "End", Sender = "p1"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_SimpleCycle_ReturnsSuccessWithWarnings()
    {
        // Arrange: 1 → 2 → 1 (cycle — valid for ping-pong workflows)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Review", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [1], IsDefault = true }]
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — cycles now produce warnings, not errors
        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("Cyclic route detected"));
        result.Warnings.Should().Contain(w => w.Contains("Action 1") && w.Contains("Action 2"));
    }

    [Fact]
    public async Task PublishAsync_SimpleCycle_SetsHasCyclesMetadata()
    {
        // Arrange: 1 → 2 → 1 (cycle)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Ping", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Pong", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [1], IsDefault = true }]
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — hasCycles metadata is set on the published blueprint
        result.IsSuccess.Should().BeTrue();
        blueprint.Metadata.Should().ContainKey("hasCycles");
        blueprint.Metadata!["hasCycles"].Should().Be("true");
    }

    [Fact]
    public async Task PublishAsync_NoCycles_DoesNotSetHasCyclesMetadata()
    {
        // Arrange: 1 → 2 (no cycle)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "End", Sender = "p2"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        (blueprint.Metadata == null || !blueprint.Metadata.ContainsKey("hasCycles")).Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_SelfReference_ReturnsSuccessWithWarning()
    {
        // Arrange: Action 1 routes to itself (self-cycle — still a warning)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Self-Loop", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [1], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Other", Sender = "p2"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — self-reference is a cycle warning, not an error
        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("Self-referencing route detected: Action 1 routes to itself"));
    }

    [Fact]
    public async Task PublishAsync_ComplexCycleWithBranches_DetectsCycleWarning()
    {
        // Arrange: 1 → 2, 1 → 3, 2 → 4, 3 → 4, 4 → 1 (cycle through branches)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2, 3], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Branch A", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [4], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 3, Title = "Branch B", Sender = "p1",
                Routes = [new RouteModel { NextActionIds = [4], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 4, Title = "Converge", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [1], IsDefault = true }]
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — cycle detected as warning, publishes successfully
        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("Cyclic route detected"));
    }

    [Fact]
    public async Task PublishAsync_ParallelBranchesNoCycle_ReturnsSuccess()
    {
        // Arrange: 1 → [2, 3] (parallel, no cycles)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2, 3], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Parallel A", Sender = "p2"
            },
            new ActionModel
            {
                Id = 3, Title = "Parallel B", Sender = "p1"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_RejectionConfigCycle_DetectedAsWarning()
    {
        // Arrange: 1 → 2, but 2 rejects back to 1 (creates cycle via rejection)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Submit", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Review", Sender = "p2",
                RejectionConfig = new RejectionConfigModel
                {
                    TargetActionId = 1,
                    RequireReason = true
                }
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — rejection cycles are valid (resubmission pattern)
        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("Cyclic route detected"));
    }

    [Fact]
    public async Task PublishAsync_NoRoutes_ReturnsSuccess()
    {
        // Arrange: Simple blueprint with no routes at all
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Action 1", Sender = "p1", IsStartingAction = true
            },
            new ActionModel
            {
                Id = 2, Title = "Action 2", Sender = "p2"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_DiamondGraph_NoCycle_ReturnsSuccess()
    {
        // Arrange: Diamond shape 1 → [2, 3] → 4 (converge, no back-edges)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { NextActionIds = [2, 3], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 2, Title = "Left", Sender = "p2",
                Routes = [new RouteModel { NextActionIds = [4], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 3, Title = "Right", Sender = "p1",
                Routes = [new RouteModel { NextActionIds = [4], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 4, Title = "End", Sender = "p2"
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_CycleWithOtherValidationErrors_StillFailsOnErrors()
    {
        // Arrange: Cycle present BUT also missing participants (hard error)
        var blueprint = new BlueprintModel
        {
            Id = "test-invalid",
            Title = "Invalid Blueprint",
            Participants = [new ParticipantModel { Id = "p1", Name = "Only One" }], // < 2 participants
            Actions =
            [
                new ActionModel
                {
                    Id = 1, Title = "Start", Sender = "p1", IsStartingAction = true,
                    Routes = [new RouteModel { NextActionIds = [2], IsDefault = true }]
                },
                new ActionModel
                {
                    Id = 2, Title = "Loop", Sender = "p1",
                    Routes = [new RouteModel { NextActionIds = [1], IsDefault = true }]
                }
            ]
        };

        _mockBlueprintStore.Setup(x => x.GetAsync(blueprint.Id)).ReturnsAsync(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — hard validation error takes precedence, publish fails
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least 2 participants"));
    }

    [Fact]
    public async Task PublishAsync_PingPongPattern_PublishesSuccessfully()
    {
        // Arrange: Ping-Pong pattern — 0 → 1 → 0 (the canonical use case)
        var blueprint = CreateBlueprint(
            new ActionModel
            {
                Id = 0, Title = "Ping", Sender = "p1", IsStartingAction = true,
                Routes = [new RouteModel { Id = "ping-to-pong", NextActionIds = [1], IsDefault = true }]
            },
            new ActionModel
            {
                Id = 1, Title = "Pong", Sender = "p2",
                Routes = [new RouteModel { Id = "pong-to-ping", NextActionIds = [0], IsDefault = true }]
            });

        SetupStore(blueprint);

        // Act
        var result = await _publishService.PublishAsync(blueprint.Id, "test-register");

        // Assert — ping-pong publishes with cycle warning
        result.IsSuccess.Should().BeTrue();
        result.Warnings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("Cyclic route detected"));
        blueprint.Metadata.Should().ContainKey("hasCycles");
        blueprint.Metadata!["hasCycles"].Should().Be("true");
    }

    #region Helpers

    private BlueprintModel CreateBlueprint(params ActionModel[] actions)
    {
        return new BlueprintModel
        {
            Id = "test-blueprint",
            Title = "Test Blueprint",
            Description = "Cycle-detection test blueprint.",
            Participants =
            [
                new ParticipantModel { Id = "p1", Name = "Participant 1" },
                new ParticipantModel { Id = "p2", Name = "Participant 2" }
            ],
            Actions = actions.ToList()
        };
    }

    private void SetupStore(BlueprintModel blueprint)
    {
        _mockBlueprintStore
            .Setup(x => x.GetAsync(blueprint.Id))
            .ReturnsAsync(blueprint);

        _mockPublishedStore
            .Setup(x => x.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint p) => p);
    }

    #endregion
}
