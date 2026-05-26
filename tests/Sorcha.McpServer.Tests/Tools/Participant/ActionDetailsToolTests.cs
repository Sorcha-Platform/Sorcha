// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Spec 139 US4: ActionDetailsTool reads via the typed <see cref="IBlueprintServiceClient.GetActionDetailsAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class ActionDetailsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly ActionDetailsTool _tool;

    public ActionDetailsToolTests()
    {
        _tool = new ActionDetailsTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<ActionDetailsTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task GetActionDetailsAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(false);

        var result = await _tool.GetActionDetailsAsync("action-123");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithEmptyActionId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);

        var result = await _tool.GetActionDetailsAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.GetActionDetailsAsync("action-123");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithSuccessfulResponse_ReturnsActionDetails()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            actionInstanceId = "action-123",
            blueprintId = "bp-1",
            title = "Submit Application",
            description = "Submit your application details",
            workflowInstanceId = "wf-1",
            actionId = 1,
            status = "Pending",
            inputSchema = "{\"type\":\"object\"}",
            requiredFields = new[] { "name", "email" },
            assignedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync("action-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetActionDetailsAsync("action-123");

        result.Status.Should().Be("Success");
        result.Action.Should().NotBeNull();
        result.Action!.ActionInstanceId.Should().Be("action-123");
        result.Action.Title.Should().Be("Submit Application");
        result.Action.Status.Should().Be("Pending");
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithNotFound_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync("action-invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.GetActionDetailsAsync("action-invalid");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.GetActionDetailsAsync("action-123");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.GetActionDetailsAsync("action-123");

        result.Status.Should().Be("Error");
    }
}
