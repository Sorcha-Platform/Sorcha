// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Designer;

/// <summary>
/// Spec 139 US4: BlueprintCreateTool writes via the typed <see cref="IBlueprintServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintCreateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintCreateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintCreateTool>>());

    private static string GetValidBlueprintJson() => JsonSerializer.Serialize(new
    {
        title = "Test Blueprint",
        description = "A test blueprint for testing purposes",
        participants = new[]
        {
            new { id = "p1", name = "Participant 1", walletAddress = "0x123" },
            new { id = "p2", name = "Participant 2", walletAddress = "0x456" }
        },
        actions = new[]
        {
            new { id = 0, title = "Action 1", sender = "p1" }
        }
    });

    [Fact]
    public async Task CreateBlueprintAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(false);

        var result = await CreateTool().CreateBlueprintAsync(GetValidBlueprintJson());

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.CreatedBlueprint.Should().BeNull();
    }

    [Fact]
    public async Task CreateBlueprintAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().CreateBlueprintAsync(GetValidBlueprintJson());

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task CreateBlueprintAsync_EmptyJson_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);

        var result = await CreateTool().CreateBlueprintAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task CreateBlueprintAsync_InvalidJson_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);

        var result = await CreateTool().CreateBlueprintAsync("{ invalid json }");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task CreateBlueprintAsync_MissingTitle_ReturnsValidationError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);

        var json = JsonSerializer.Serialize(new
        {
            description = "Test description here",
            participants = new[] { new { id = "p1", name = "Participant 1" }, new { id = "p2", name = "Participant 2" } },
            actions = new[] { new { id = 0, title = "Action 1" } }
        });

        var result = await CreateTool().CreateBlueprintAsync(json);

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Contain(e => e.Contains("title"));
    }

    [Fact]
    public async Task CreateBlueprintAsync_InsufficientParticipants_ReturnsValidationError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);

        var json = JsonSerializer.Serialize(new
        {
            title = "Test Blueprint",
            description = "Test description here",
            participants = new[] { new { id = "p1", name = "Participant 1" } },
            actions = new[] { new { id = 0, title = "Action 1" } }
        });

        var result = await CreateTool().CreateBlueprintAsync(json);

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Contain(e => e.Contains("participants") && e.Contains("at least 2"));
    }

    [Fact]
    public async Task CreateBlueprintAsync_NoActions_ReturnsValidationError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);

        var json = JsonSerializer.Serialize(new
        {
            title = "Test Blueprint",
            description = "Test description here",
            participants = new[] { new { id = "p1", name = "Participant 1" }, new { id = "p2", name = "Participant 2" } },
            actions = Array.Empty<object>()
        });

        var result = await CreateTool().CreateBlueprintAsync(json);

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().Contain(e => e.Contains("actions") && e.Contains("at least 1"));
    }

    [Fact]
    public async Task CreateBlueprintAsync_ValidBlueprint_ReturnsSuccessResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        var createdResponse = JsonSerializer.Serialize(new
        {
            Id = "bp-new-123",
            Title = "Test Blueprint",
            Description = "A test blueprint for testing purposes",
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants = new[] { new { id = "p1", name = "Participant 1" }, new { id = "p2", name = "Participant 2" } },
            Actions = new[] { new { id = 0, title = "Action 1" } }
        });

        _blueprintClientMock
            .Setup(c => c.CreateBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdResponse);

        var result = await CreateTool().CreateBlueprintAsync(GetValidBlueprintJson());

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("bp-new-123");
        result.CreatedBlueprint.Should().NotBeNull();
        result.CreatedBlueprint!.Id.Should().Be("bp-new-123");
        result.CreatedBlueprint.Version.Should().Be(1);
        result.CreatedBlueprint.ParticipantCount.Should().Be(2);
        result.CreatedBlueprint.ActionCount.Should().Be(1);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task CreateBlueprintAsync_ServiceReturnsNull_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        _blueprintClientMock
            .Setup(c => c.CreateBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().CreateBlueprintAsync(GetValidBlueprintJson());

        result.Status.Should().Be("Error");
        result.CreatedBlueprint.Should().BeNull();
    }

    [Fact]
    public async Task CreateBlueprintAsync_ResponseTimeIsRecorded()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_create")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        _blueprintClientMock
            .Setup(c => c.CreateBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Id = "bp-123", Title = "Test", Description = "Test", Version = 1, Participants = Array.Empty<object>(), Actions = Array.Empty<object>() }));

        var result = await CreateTool().CreateBlueprintAsync(GetValidBlueprintJson());

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
