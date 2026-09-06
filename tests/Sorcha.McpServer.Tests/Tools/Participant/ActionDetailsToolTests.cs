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
/// Task 4 (MCP P0 restore-surface): ActionDetailsTool now reads via the typed
/// <see cref="IBlueprintServiceClient.GetActionDetailsAsync(string, string, CancellationToken)"/>
/// overload pinned to <c>GET /api/instances/{instanceId}/actions/{actionId}</c> — the route
/// <c>GET /api/actions/{id}</c> it previously targeted is not mapped by Blueprint Service. These
/// tests mock the client rather than HTTP, and assert against
/// <see cref="InstanceActionSchemaResponse"/>'s actual (narrower) shape rather than the old,
/// never-served <c>ActionDetailsResponse</c> fields.
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

        var result = await _tool.GetActionDetailsAsync("instance-123", "1");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithEmptyInstanceId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);

        var result = await _tool.GetActionDetailsAsync("", "1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithEmptyActionId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);

        var result = await _tool.GetActionDetailsAsync("instance-123", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_details")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.GetActionDetailsAsync("instance-123", "1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithSuccessfulResponse_ReturnsActionDetails()
    {
        Allow();

        // Shape of Sorcha.Blueprint.Service.Models.Responses.InstanceActionSchemaResponse —
        // deliberately narrow (no workflowInstanceId/blueprintId/status/disclosedData; those never
        // existed on this endpoint).
        var response = JsonSerializer.Serialize(new
        {
            actionId = 1,
            title = "Submit Application",
            form = (object?)null,
            dataSchemas = new[] { new { type = "object", required = new[] { "name", "email" } } },
            calculations = (object?)null,
            credentialRequirements = Array.Empty<object>(),
            credentialIssuanceConfig = (object?)null
        });
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync("instance-123", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetActionDetailsAsync("instance-123", "1");

        result.Status.Should().Be("Success");
        result.Action.Should().NotBeNull();
        result.Action!.InstanceId.Should().Be("instance-123");
        result.Action.ActionId.Should().Be(1);
        result.Action.Title.Should().Be("Submit Application");
        result.Action.InputSchemas.Should().HaveCount(1);
        result.Action.HasCredentialRequirements.Should().BeFalse();
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithCredentialRequirements_ReportsGatePresence()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            actionId = 2,
            title = "Claim Credential",
            dataSchemas = Array.Empty<object>(),
            credentialRequirements = new[] { new { type = "SomeCredentialType" } }
        });
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync("instance-123", "2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetActionDetailsAsync("instance-123", "2");

        result.Status.Should().Be("Success");
        result.Action!.HasCredentialRequirements.Should().BeTrue();
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithNotFound_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync("instance-invalid", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.GetActionDetailsAsync("instance-invalid", "1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.GetActionDetailsAsync("instance-123", "1");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetActionDetailsAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetActionDetailsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.GetActionDetailsAsync("instance-123", "1");

        result.Status.Should().Be("Error");
    }
}
