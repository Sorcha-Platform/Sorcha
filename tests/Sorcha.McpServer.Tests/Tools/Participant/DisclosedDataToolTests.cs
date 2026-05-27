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
/// Spec 139 US4: DisclosedDataTool reads via the typed <see cref="IBlueprintServiceClient.GetDisclosedDataAsync"/>
/// (routes pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class DisclosedDataToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly DisclosedDataTool _tool;

    public DisclosedDataToolTests()
    {
        _tool = new DisclosedDataTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<DisclosedDataTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_disclosed_data")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_disclosed_data")).Returns(false);

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithEmptyWorkflowId_ReturnsError()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_disclosed_data")).Returns(true);

        var result = await _tool.GetDisclosedDataAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_disclosed_data")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithDisclosures_ReturnsDisclosedData()
    {
        Allow();

        var response = JsonSerializer.Serialize(new
        {
            disclosures = new[]
            {
                new { actionId = 1, actionTitle = "Initial Submission", disclosedAt = DateTimeOffset.UtcNow.AddDays(-1), data = new Dictionary<string, object> { ["applicantName"] = "John Doe", ["email"] = "john@example.com" } },
                new { actionId = 2, actionTitle = "Manager Review", disclosedAt = DateTimeOffset.UtcNow.AddHours(-2), data = new Dictionary<string, object> { ["reviewerComments"] = "Approved" } }
            }
        });
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync("wf-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Success");
        result.Disclosures.Should().HaveCount(2);
        result.Disclosures[0].ActionId.Should().Be(1);
        result.Disclosures[0].ActionTitle.Should().Be("Initial Submission");
        result.TotalFields.Should().Be(3);
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithNoDisclosures_ReturnsEmptyList()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync("wf-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { disclosures = Array.Empty<object>() }));

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Success");
        result.Disclosures.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithActionFilter_PassesActionInstanceId()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync("wf-123", "action-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { disclosures = Array.Empty<object>() }));

        await _tool.GetDisclosedDataAsync("wf-123", "action-456");

        _blueprintClientMock.Verify(
            c => c.GetDisclosedDataAsync("wf-123", "action-456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithNotFound_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync("wf-invalid", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.GetDisclosedDataAsync("wf-invalid");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetDisclosedDataAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetDisclosedDataAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.GetDisclosedDataAsync("wf-123");

        result.Status.Should().Be("Error");
    }
}
