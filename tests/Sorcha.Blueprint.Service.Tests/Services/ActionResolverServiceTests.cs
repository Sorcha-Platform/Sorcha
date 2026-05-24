// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using System.Text;
using System.Text.Json;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class ActionResolverServiceTests
{
    private readonly Mock<IBlueprintStore> _mockBlueprintStore;
    private readonly Mock<IPublishedBlueprintStore> _mockPublishedStore;
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<ILogger<ActionResolverService>> _mockLogger;
    private readonly ActionResolverService _service;

    public ActionResolverServiceTests()
    {
        _mockBlueprintStore = new Mock<IBlueprintStore>();
        _mockPublishedStore = new Mock<IPublishedBlueprintStore>();
        _mockCache = new Mock<IDistributedCache>();
        _mockLogger = new Mock<ILogger<ActionResolverService>>();
        _service = new ActionResolverService(
            _mockBlueprintStore.Object,
            _mockPublishedStore.Object,
            _mockCache.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetBlueprintAsync_DraftMiss_FallsBackToPublishedStore()
    {
        // Feature 137: on a replica the blueprint exists only in the replicated published
        // store, so action execution must fall back to it when the draft store misses.
        var blueprintId = "replicated-bp";
        var blueprint = new BlueprintModel { Id = blueprintId, Title = "Replicated", Description = "" };

        _mockCache.Setup(x => x.GetAsync($"blueprint:{blueprintId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockBlueprintStore.Setup(x => x.GetAsync(blueprintId))
            .ReturnsAsync((BlueprintModel?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[] { new PublishedBlueprint { Version = 2, Blueprint = blueprint } });

        var result = await _service.GetBlueprintAsync(blueprintId);

        result.Should().NotBeNull("a replica resolves the blueprint from the published store");
        result!.Id.Should().Be(blueprintId);
        _mockPublishedStore.Verify(x => x.GetVersionsAsync(blueprintId), Times.Once);
    }

    [Fact]
    public async Task GetBlueprintAsync_NotInEitherStore_ReturnsNull()
    {
        var blueprintId = "ghost-bp";
        _mockCache.Setup(x => x.GetAsync($"blueprint:{blueprintId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockBlueprintStore.Setup(x => x.GetAsync(blueprintId)).ReturnsAsync((BlueprintModel?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(Array.Empty<PublishedBlueprint>());

        var result = await _service.GetBlueprintAsync(blueprintId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBlueprintAsync_WithValidId_ReturnsBlueprint()
    {
        // Arrange
        var blueprintId = "test-blueprint-1";
        var blueprint = new BlueprintModel
        {
            Id = blueprintId,
            Title = "Test Blueprint",
            Description = "A test blueprint"
        };

        _mockCache.Setup(x => x.GetAsync(
            It.Is<string>(k => k == $"blueprint:{blueprintId}"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _mockBlueprintStore.Setup(x => x.GetAsync(blueprintId))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _service.GetBlueprintAsync(blueprintId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(blueprintId);
        result.Title.Should().Be("Test Blueprint");

        _mockBlueprintStore.Verify(x => x.GetAsync(blueprintId), Times.Once);
        _mockCache.Verify(x => x.SetAsync(
            It.Is<string>(k => k == $"blueprint:{blueprintId}"),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBlueprintAsync_WithCachedBlueprint_ReturnsCachedVersion()
    {
        // Arrange
        var blueprintId = "test-blueprint-1";
        var blueprint = new BlueprintModel
        {
            Id = blueprintId,
            Title = "Cached Blueprint"
        };

        var cachedData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blueprint));
        _mockCache.Setup(x => x.GetAsync(
            It.Is<string>(k => k == $"blueprint:{blueprintId}"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedData);

        // Act
        var result = await _service.GetBlueprintAsync(blueprintId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(blueprintId);
        result.Title.Should().Be("Cached Blueprint");

        _mockBlueprintStore.Verify(x => x.GetAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetBlueprintAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var blueprintId = "non-existent";

        _mockCache.Setup(x => x.GetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        _mockBlueprintStore.Setup(x => x.GetAsync(blueprintId))
            .ReturnsAsync((BlueprintModel?)null);

        // Act
        var result = await _service.GetBlueprintAsync(blueprintId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBlueprintAsync_WithNullId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBlueprintAsync(null!));
    }

    [Fact]
    public async Task GetBlueprintAsync_WithEmptyId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBlueprintAsync(""));
    }

    [Fact]
    public void GetActionDefinition_WithValidAction_ReturnsAction()
    {
        // Arrange
        var actionId = 1;
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Actions = new List<ActionModel>
            {
                new ActionModel { Id = actionId, Title = "Test Action" },
                new ActionModel { Id = 2, Title = "Another Action" }
            }
        };

        // Act
        var result = _service.GetActionDefinition(blueprint, actionId.ToString());

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(actionId);
        result.Title.Should().Be("Test Action");
    }

    [Fact]
    public void GetActionDefinition_WithNonExistentAction_ReturnsNull()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Actions = new List<ActionModel>
            {
                new ActionModel { Id = 1, Title = "Test Action" }
            }
        };

        // Act
        var result = _service.GetActionDefinition(blueprint, "999");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetActionDefinition_WithNullBlueprint_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => _service.GetActionDefinition(null!, "action-1"));
    }

    [Fact]
    public void GetActionDefinition_WithNullActionId_ThrowsArgumentException()
    {
        // Arrange
        var blueprint = new BlueprintModel { Id = "blueprint-1" };

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => _service.GetActionDefinition(blueprint, null!));
    }

    [Fact]
    public void GetActionDefinition_WithInvalidActionIdFormat_ReturnsNull()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Actions = new List<ActionModel>
            {
                new ActionModel { Id = 1, Title = "Test Action" }
            }
        };

        // Act - non-numeric action ID should return null (can't parse)
        var result = _service.GetActionDefinition(blueprint, "non-numeric");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveParticipantWalletsAsync_WithValidParticipants_ReturnsWalletMap()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "participant-1", Name = "Alice", WalletAddress = "wallet-alice" },
                new ParticipantModel { Id = "participant-2", Name = "Bob", WalletAddress = "wallet-bob" }
            }
        };

        var participantIds = new[] { "participant-1", "participant-2" };

        // Act
        var result = await _service.ResolveParticipantWalletsAsync(blueprint, participantIds);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("participant-1");
        result.Should().ContainKey("participant-2");
        result["participant-1"].Should().Be("wallet-alice");
        result["participant-2"].Should().Be("wallet-bob");
    }

    [Fact]
    public async Task ResolveParticipantWalletsAsync_WithNonExistentParticipant_SkipsInvalid()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "participant-1", Name = "Alice", WalletAddress = "wallet-alice" }
            }
        };

        var participantIds = new[] { "participant-1", "non-existent" };

        // Act
        var result = await _service.ResolveParticipantWalletsAsync(blueprint, participantIds);

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("participant-1");
        result.Should().NotContainKey("non-existent");
    }

    [Fact]
    public async Task ResolveParticipantWalletsAsync_WithEmptyList_ReturnsEmpty()
    {
        // Arrange
        var blueprint = new BlueprintModel
        {
            Id = "blueprint-1",
            Participants = new List<ParticipantModel>()
        };

        var participantIds = Array.Empty<string>();

        // Act
        var result = await _service.ResolveParticipantWalletsAsync(blueprint, participantIds);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveParticipantWalletsAsync_WithNullBlueprint_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveParticipantWalletsAsync(null!, new[] { "p1" }));
    }

    [Fact]
    public async Task ResolveParticipantWalletsAsync_WithNullParticipantIds_ThrowsArgumentNullException()
    {
        // Arrange
        var blueprint = new BlueprintModel { Id = "blueprint-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveParticipantWalletsAsync(blueprint, null!));
    }
}
