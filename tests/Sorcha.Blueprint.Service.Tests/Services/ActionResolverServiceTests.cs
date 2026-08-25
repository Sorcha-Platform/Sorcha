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

    /// <summary>
    /// A stand-in definition pin (Feature 195). Publishing assigns a real publication id;
    /// these tests only need a stable, non-empty one, because the resolver now REQUIRES a pin
    /// rather than falling back to whatever definition this node holds latest.
    /// </summary>
    private const string TestDefinitionPin = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
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

    // ---------------------------------------------------------------------------------------------
    // Feature 195 — resolution on the EXECUTION path is by pin, from the published store only.
    //
    // What these tests replaced, and why it is not a weakening: the previous set asserted draft-first
    // resolution with a fallback to the LATEST published definition, and caching under a bare
    // blueprint id. Each of those is now a defect rather than a feature. The engine validated a
    // payload, evaluated calculations and computed a route against whichever definition that
    // resolution happened to return, then signed a routing decision labelled with the instance's
    // actual pin — and where the two disagreed the submission returned 202 and never sealed,
    // permanently, with no error anywhere.
    //
    // The draft-store test in particular ENCODED the defect: a draft is unpublished work-in-progress
    // and must never decide how a running instance behaves.
    // ---------------------------------------------------------------------------------------------

    private static PublishedBlueprint Publication(string blueprintId, string pin, string title)
        => new()
        {
            BlueprintId = blueprintId,
            PublicationTxId = pin,
            Blueprint = new BlueprintModel { Id = blueprintId, Title = title, Description = "" }
        };

    [Fact]
    public async Task GetBlueprintAsync_ResolvesThePinnedDefinition_FromThePublishedStore()
    {
        const string blueprintId = "replicated-bp";
        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[] { Publication(blueprintId, TestDefinitionPin, "Replicated") });

        var result = await _service.GetBlueprintAsync(blueprintId, TestDefinitionPin);

        result.Should().NotBeNull("a replica resolves the pinned definition from the published store");
        result!.Id.Should().Be(blueprintId);
    }

    /// <summary>
    /// The load-bearing test of this feature: two definitions of one blueprint, and each instance
    /// gets its own.
    /// </summary>
    [Fact]
    public async Task GetBlueprintAsync_WithTwoDefinitions_ResolvesTheOneAsked_NotTheLatest()
    {
        const string blueprintId = "two-definition-bp";
        const string olderPin = "1111111111111111111111111111111111111111111111111111111111111111";
        const string newerPin = "2222222222222222222222222222222222222222222222222222222222222222";

        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[]
            {
                Publication(blueprintId, olderPin, "Older"),
                Publication(blueprintId, newerPin, "Newer")
            });

        var older = await _service.GetBlueprintAsync(blueprintId, olderPin);
        var newer = await _service.GetBlueprintAsync(blueprintId, newerPin);

        older!.Title.Should().Be("Older", "an instance pinned to the older definition must keep it");
        newer!.Title.Should().Be("Newer");
    }

    /// <summary>
    /// A draft must not influence a running instance — the defect the previous suite encoded.
    /// </summary>
    [Fact]
    public async Task GetBlueprintAsync_DoesNotConsultTheDraftStore()
    {
        const string blueprintId = "drafted-bp";
        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockBlueprintStore.Setup(x => x.GetAsync(blueprintId))
            .ReturnsAsync(new BlueprintModel { Id = blueprintId, Title = "DRAFT - must not be used" });
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[] { Publication(blueprintId, TestDefinitionPin, "Published") });

        var result = await _service.GetBlueprintAsync(blueprintId, TestDefinitionPin);

        result!.Title.Should().Be("Published");
        _mockBlueprintStore.Verify(x => x.GetAsync(It.IsAny<string>()), Times.Never,
            "the draft store is not on the execution path at all");
    }

    [Fact]
    public async Task GetBlueprintAsync_UnresolvablePin_ReturnsNull_AndNeverSubstitutesLatest()
    {
        const string blueprintId = "two-definition-bp";
        const string knownPin = "1111111111111111111111111111111111111111111111111111111111111111";
        const string unknownPin = "9999999999999999999999999999999999999999999999999999999999999999";

        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[] { Publication(blueprintId, knownPin, "The only one here") });

        var result = await _service.GetBlueprintAsync(blueprintId, unknownPin);

        result.Should().BeNull(
            "refusing is the point - substituting a definition the instance never agreed to run is " +
            "the defect version pinning exists to remove, and it fails silently");
    }

    [Fact]
    public async Task GetBlueprintAsync_CachesPerDefinition_NotPerBlueprint()
    {
        const string blueprintId = "cached-bp";
        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _mockPublishedStore.Setup(x => x.GetVersionsAsync(blueprintId))
            .ReturnsAsync(new[] { Publication(blueprintId, TestDefinitionPin, "Cached") });

        await _service.GetBlueprintAsync(blueprintId, TestDefinitionPin);

        _mockCache.Verify(x => x.SetAsync(
            $"blueprint:{blueprintId}:{TestDefinitionPin}",
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once,
            "a cache key that omits the pin serves one instance's definition to another");
    }

    [Fact]
    public async Task GetBlueprintAsync_ReturnsTheCachedDefinition_WithoutTouchingTheStore()
    {
        const string blueprintId = "cached-bp";
        var cached = new BlueprintModel { Id = blueprintId, Title = "Cached Blueprint" };
        _mockCache.Setup(x => x.GetAsync(
                $"blueprint:{blueprintId}:{TestDefinitionPin}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cached)));

        var result = await _service.GetBlueprintAsync(blueprintId, TestDefinitionPin);

        result!.Title.Should().Be("Cached Blueprint");
        _mockPublishedStore.Verify(x => x.GetVersionsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetBlueprintAsync_WithNullId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBlueprintAsync(null!, TestDefinitionPin));
    }

    [Fact]
    public async Task GetBlueprintAsync_WithEmptyId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBlueprintAsync("", TestDefinitionPin));
    }

    /// <summary>
    /// An absent pin is a programming error, not a resolvable state. Falling back to "latest" here
    /// would reinstate the defect for every caller that forgot to pass one.
    /// </summary>
    [Fact]
    public async Task GetBlueprintAsync_WithNoPin_ThrowsRatherThanResolvingLatest()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.GetBlueprintAsync("some-bp", ""));
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
