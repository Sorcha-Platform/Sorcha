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
/// Spec 139 US4: WorkflowStatusTool reads via the typed <see cref="IBlueprintServiceClient.GetWorkflowStatusAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class WorkflowStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly WorkflowStatusTool _tool;

    public WorkflowStatusToolTests()
    {
        _tool = new WorkflowStatusTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<WorkflowStatusTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_workflow_status")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_workflow_status")).Returns(false);

        var result = await _tool.GetWorkflowStatusAsync("wf-123");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithEmptyWorkflowId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_workflow_status")).Returns(true);

        var result = await _tool.GetWorkflowStatusAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_workflow_status")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.GetWorkflowStatusAsync("wf-123");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithActiveWorkflow_ReturnsWorkflowDetails()
    {
        Allow();

        // Shape of Sorcha.Blueprint.Service.Models.Instance, the actual wire body of
        // GET /api/instances/{id} — id/state/currentActionIds/completedActionCount, not a
        // dedicated "workflow status" projection. State serializes as its underlying enum int
        // (Active = 0) since Blueprint Service registers no JsonStringEnumConverter for it.
        var response = JsonSerializer.Serialize(new
        {
            id = "wf-123",
            blueprintId = "bp-1",
            state = 0,
            currentActionIds = new[] { 2 },
            completedActionCount = 1,
            createdAt = DateTimeOffset.UtcNow.AddDays(-1),
            updatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        _blueprintClientMock
            .Setup(c => c.GetWorkflowStatusAsync("wf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetWorkflowStatusAsync("wf-123");

        result.Status.Should().Be("Success");
        result.Workflow!.WorkflowInstanceId.Should().Be("wf-123");
        result.Workflow.CurrentStatus.Should().Be("Active");
        result.Workflow.CurrentActionId.Should().Be(2);
        result.Workflow.CompletedActions.Should().Be(1);
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithStringState_ResolvesCurrentStatus()
    {
        Allow();

        // Defensive coverage: if Blueprint Service ever adds a JsonStringEnumConverter for
        // InstanceState, the string form must still resolve correctly.
        var response = JsonSerializer.Serialize(new
        {
            id = "wf-456",
            blueprintId = "bp-1",
            state = "Completed",
            currentActionIds = Array.Empty<int>(),
            completedActionCount = 4
        });
        _blueprintClientMock
            .Setup(c => c.GetWorkflowStatusAsync("wf-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetWorkflowStatusAsync("wf-456");

        result.Status.Should().Be("Success");
        result.Workflow!.CurrentStatus.Should().Be("Completed");
        result.Workflow.CurrentActionId.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithNotFound_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowStatusAsync("wf-invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.GetWorkflowStatusAsync("wf-invalid");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.GetWorkflowStatusAsync("wf-123");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.GetWorkflowStatusAsync("wf-123");

        result.Status.Should().Be("Error");
    }
}
