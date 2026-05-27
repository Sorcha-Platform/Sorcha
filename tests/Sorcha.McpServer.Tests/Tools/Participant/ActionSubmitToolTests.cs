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
/// Spec 139 US4: ActionSubmitTool executes via the typed <see cref="IBlueprintServiceClient.ExecuteActionAsync"/>
/// (real execute route, caller token forwarded). The tool now takes instanceId + actionId (the old
/// single actionInstanceId targeted a non-existent /submit route). These tests mock the client.
/// </summary>
public sealed class ActionSubmitToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly ActionSubmitTool _tool;

    public ActionSubmitToolTests()
    {
        _tool = new ActionSubmitTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<ActionSubmitTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task SubmitActionAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{}");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyInstanceId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("", "1", "{}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyActionId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "", "{}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithEmptyData_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithInvalidJson_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "not valid json {");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_action_submit")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"test\"}");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task SubmitActionAsync_WithSuccessfulSubmission_ReturnsSuccess()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            message = "Action submitted successfully.",
            transactionId = "tx-789",
            nextActions = new[] { new { actionId = 2, title = "Next Action", assignedTo = "participant-2" } }
        });
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"name\":\"John Doe\"}");

        result.Status.Should().Be("Success");
        result.TransactionId.Should().Be("tx-789");
        result.NextActions.Should().HaveCount(1);
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task SubmitActionAsync_ExecutePathUsesInstanceAndActionId()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { transactionId = "tx-1" }));

        await _tool.SubmitActionAsync("wf-1", "2", "{\"x\":1}");

        _blueprintClientMock.Verify(
            c => c.ExecuteActionAsync("wf-1", "2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitActionAsync_WithNull_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task SubmitActionAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task SubmitActionAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ExecuteActionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.SubmitActionAsync("wf-1", "1", "{\"data\":\"test\"}");

        result.Status.Should().Be("Error");
    }
}
