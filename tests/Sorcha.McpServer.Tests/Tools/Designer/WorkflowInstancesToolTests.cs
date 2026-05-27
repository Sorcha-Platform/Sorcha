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
/// Spec 139 US4: WorkflowInstancesTool reads via the typed <see cref="IBlueprintServiceClient.GetWorkflowInstancesAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
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

        var listResponse = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { instanceId = "wf-001", blueprintId = "bp-123", blueprintTitle = "Approval", status = "Active", currentActionId = 2, currentActionTitle = "Review", startedAt = DateTimeOffset.UtcNow.AddHours(-1), completedAt = (DateTimeOffset?)null, lastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
                new { instanceId = "wf-002", blueprintId = "bp-123", blueprintTitle = "Approval", status = "Completed", currentActionId = 3, currentActionTitle = "Complete", startedAt = DateTimeOffset.UtcNow.AddDays(-1), completedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-2), lastActivityAt = DateTimeOffset.UtcNow.AddHours(-2) }
            },
            totalCount = 2,
            page = 1,
            pageSize = 20,
            totalPages = 1
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
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_NoInstances_ReturnsEmptyList()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20, totalPages = 0 }));

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.Status.Should().Be("Success");
        result.Instances.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_PassesFiltersInQueryString()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20, totalPages = 0 }));

        await CreateTool().ListWorkflowInstancesAsync(blueprintId: "bp-123", status: "Active");

        _blueprintClientMock.Verify(
            c => c.GetWorkflowInstancesAsync(
                It.Is<string>(q => q.Contains("blueprintId=bp-123") && q.Contains("status=Active")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListWorkflowInstancesAsync_PageSizeExceedsMax_CapsAt100()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 100, totalPages = 0 }));

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
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20, totalPages = 0 }));

        var result = await CreateTool().ListWorkflowInstancesAsync();

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
