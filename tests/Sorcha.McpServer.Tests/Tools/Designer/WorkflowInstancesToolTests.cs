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
/// Task 4 (MCP P0 restore-surface): WorkflowInstancesTool now reads via the typed
/// <see cref="IBlueprintServiceClient.GetWorkflowInstancesAsync"/> pinned to
/// <c>GET /api/instances/</c> — <c>GET /api/workflows</c> it previously targeted is not mapped.
/// These tests mock the client rather than HTTP, and assert against the actual
/// <c>Sorcha.Blueprint.Service.Models.Instance</c> wire shape (id/state/currentActionIds/pageNumber,
/// no blueprintId filter, no blueprint/action title) rather than the old, never-served fields.
/// </summary>
public class WorkflowInstancesToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private WorkflowInstancesTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<WorkflowInstancesTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_workflow_instances")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_workflow_instances")).Returns(false);

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_InvalidStatus_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_workflow_instances")).Returns(true);

        var result = await CreateTool().ListWorkflowInstancesAsync(status: "Bogus");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_workflow_instances")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_WithInstances_ReturnsSuccessResult()
    {
        Allow();

        // Shape of Sorcha.Blueprint.Service.Models.Instance items from GET /api/instances/ —
        // id/state/currentActionIds/completedAt/updatedAt, "pageNumber" not "page", no totalPages.
        // State serializes as its underlying enum int (Active = 0, Completed = 1).
        var listResponse = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { id = "wf-001", blueprintId = "bp-123", state = 0, currentActionIds = new[] { 2 }, createdAt = DateTimeOffset.UtcNow.AddHours(-1), completedAt = (DateTimeOffset?)null, updatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
                new { id = "wf-002", blueprintId = "bp-123", state = 1, currentActionIds = Array.Empty<int>(), createdAt = DateTimeOffset.UtcNow.AddDays(-1), completedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-2), updatedAt = DateTimeOffset.UtcNow.AddHours(-2) }
            },
            totalCount = 2,
            pageNumber = 1,
            pageSize = 20
        });

        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.Status.Should().Be("Success");
        result.Instances.Should().HaveCount(2);
        result.Instances[0].InstanceId.Should().Be("wf-001");
        result.Instances[0].Status.Should().Be("Active");
        result.Instances[0].CurrentActionId.Should().Be(2);
        result.Instances[1].Status.Should().Be("Completed");
        result.Instances[1].CurrentActionId.Should().BeNull();
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(1);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_NoInstances_ReturnsEmptyList()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, pageNumber = 1, pageSize = 20 }));

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.Status.Should().Be("Success");
        result.Instances.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_PassesStatusFilterInQueryString()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, pageNumber = 1, pageSize = 20 }));

        await CreateTool().ListWorkflowInstancesAsync(status: "Active");

        _blueprintClientMock.Verify(
            c => c.GetWorkflowInstancesAsync(
                It.Is<string>(q => q.Contains("status=Active")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_PageSizeExceedsMax_CapsAt100()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, pageNumber = 1, pageSize = 100 }));

        await CreateTool().ListWorkflowInstancesAsync(pageSize: 500);

        _blueprintClientMock.Verify(
            c => c.GetWorkflowInstancesAsync(It.Is<string>(q => q.Contains("pageSize=100")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, pageNumber = 1, pageSize = 20 }));

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
