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

        // Shape as ExecutionRoutingEndpoint (POST /api/execution/route) actually writes it: actionId
        // is a STRING (RoutedAction.ActionId), and the route identity fields are matchedRouteId /
        // matchedRouteDescription — not matchedRoute / routeDescription (#1681).
        var routeResponse = JsonSerializer.Serialize(new
        {
            nextActions = new[]
            {
                new { actionId = "1", title = "Review", isTerminal = false },
                new { actionId = "2", title = "Approve", isTerminal = true }
            },
            isWorkflowComplete = false,
            matchedRouteId = "default",
            matchedRouteDescription = "Routes to review or approval"
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

        // Genuinely no routing configured: no next actions AND no matched route id. This must
        // still read as "No routing configured", not the terminal-route message below — the two
        // are semantically different (#1681).
        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                nextActions = Array.Empty<object>(),
                isWorkflowComplete = true,
                matchedRouteId = (string?)null
            }));
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { processedData = new Dictionary<string, object>(), calculatedFields = Array.Empty<string>() }));

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.Status.Should().Be("Success");
        result.Routing!.NextActions.Should().BeEmpty();
        result.Message.Should().Contain("No routing configured for this action");
    }

    [Fact]
    public async Task SimulateActionAsync_MatchedTerminalRoute_ReportsWorkflowCompleteNotNoRouting()
    {
        // #1681 — a Route with an empty nextActionIds list is a DESIGNED end-of-workflow step
        // (isWorkflowComplete + matchedRouteId both set), not an authoring gap. Conflating the two
        // is exactly what made the simulator cry "No routing configured" for a perfectly correct
        // terminal branch.
        Allow();

        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                nextActions = Array.Empty<object>(),
                isWorkflowComplete = true,
                matchedRouteId = "issued",
                matchedRouteDescription = "Posture credential issued — workflow complete"
            }));
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { processedData = new Dictionary<string, object>(), calculatedFields = Array.Empty<string>() }));

        var result = await CreateTool().SimulateActionAsync("bp-123", "0", "{}");

        result.Status.Should().Be("Success");
        result.Routing!.NextActions.Should().BeEmpty();
        result.Routing.IsWorkflowComplete.Should().BeTrue();
        result.Routing.MatchedRoute.Should().Be("issued");
        result.Message.Should().Contain("terminal step");
        result.Message.Should().NotContain("No routing configured",
            "a matched terminal route is a designed outcome, not the absence of routing rules");
    }

    [Fact]
    public async Task SimulateActionAsync_ConditionalRouteMatches_ReportsNextActionNotNoRouting()
    {
        // Reproduces cold-start run #7 (#1681): an action with two routes, one conditional, whose
        // condition matched. The server-side wire shape below is exactly what the FIXED
        // ExecutionRoutingEndpoint produces for that case (see ExecutionRoutingEndpointTests).
        Allow();

        _blueprintClientMock
            .Setup(c => c.SimulateRouteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new
            {
                nextActions = new[]
                {
                    new { actionId = "1", participantId = "assessor", branchId = (string?)null, matchedRouteId = "compliant-issue", title = "Issue Posture Credential", isTerminal = true }
                },
                isWorkflowComplete = false,
                matchedRouteId = "compliant-issue",
                matchedRouteDescription = "UAC requirements met — proceed to issue the posture credential"
            }));
        _blueprintClientMock
            .Setup(c => c.SimulateCalculateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { processedData = new Dictionary<string, object>(), calculatedFields = Array.Empty<string>() }));

        var result = await CreateTool().SimulateActionAsync("bp-uac-1", "0", "{\"computedCompliant\": true}");

        result.Status.Should().Be("Success");
        result.Routing!.NextActions.Should().HaveCount(1);
        result.Routing.NextActions[0].ActionId.Should().Be(1);
        result.Message.Should().Contain("Routes to 1 next action");
        result.Message.Should().NotContain("No routing configured",
            "the action demonstrably has routes and one of them matched — reporting no routing here is the #1681 defect");
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
