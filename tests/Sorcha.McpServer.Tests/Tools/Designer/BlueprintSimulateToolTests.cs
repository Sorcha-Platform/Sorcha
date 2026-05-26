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
/// Spec 139 US4: BlueprintSimulateTool reads via the typed <see cref="IBlueprintServiceClient"/>
/// route/calculate endpoints (routes pinned, caller token forwarded), so these tests mock the client.
/// </summary>
public class BlueprintSimulateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintSimulateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintSimulateTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_simulate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task SimulateActionAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_simulate")).Returns(false);

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task SimulateActionAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_simulate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task SimulateActionAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_simulate")).Returns(true);

        var result = await CreateTool().SimulateActionAsync("", "0", "{}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SimulateActionAsync_InvalidJson_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_simulate")).Returns(true);

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{ invalid }");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SimulateActionAsync_SuccessfulSimulation_ReturnsSuccessResult()
    {
        Allow();

        var routeResponse = JsonSerializer.Serialize(new
        {
            nextActions = new[]
            {
                new { actionId = 1, title = "Review", isTerminal = false },
                new { actionId = 2, title = "Approve", isTerminal = true }
            },
            matchedRoute = "default",
            routeDescription = "Routes to review or approval"
        });

        var calculateResponse = JsonSerializer.Serialize(new
        {
            processedData = new Dictionary<string, object> { ["amount"] = 1000, ["tax"] = 100, ["total"] = 1100 },
            calculatedFields = new[] { "tax", "total" }
        });

        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(routeResponse);
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(calculateResponse);

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{\"amount\": 1000}");

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("2 next action");
        result.Routing.Should().NotBeNull();
        result.Routing!.NextActions.Should().HaveCount(2);
        result.Routing.NextActions[0].ActionId.Should().Be(1);
        result.Routing.NextActions[0].Title.Should().Be("Review");
        result.Routing.NextActions[1].IsTerminal.Should().BeTrue();
        result.Routing.MatchedRoute.Should().Be("default");
        result.Calculations.Should().NotBeNull();
        result.Calculations!.CalculatedFields.Should().Contain("tax");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task SimulateActionAsync_NoRouting_ReturnsSuccessWithNoRoutes()
    {
        Allow();

        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { nextActions = Array.Empty<object>(), matchedRoute = (string?)null }));
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { processedData = new Dictionary<string, object>(), calculatedFields = Array.Empty<string>() }));

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.Status.Should().Be("Success");
        result.Routing!.NextActions.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateActionAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { nextActions = Array.Empty<object>() }));
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { processedData = new Dictionary<string, object>(), calculatedFields = Array.Empty<string>() }));

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
