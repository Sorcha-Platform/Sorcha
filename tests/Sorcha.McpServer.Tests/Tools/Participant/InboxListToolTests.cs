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
/// Spec 139 US4: InboxListTool reads via the typed <see cref="IBlueprintServiceClient.GetInboxAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public sealed class InboxListToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly InboxListTool _tool;

    public InboxListToolTests()
    {
        _tool = new InboxListTool(
            _authServiceMock.Object,
            _availabilityTrackerMock.Object,
            _blueprintClientMock.Object,
            Mock.Of<ILogger<InboxListTool>>());
    }

    private void Allow()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_inbox_list")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task ListInboxAsync_WhenUnauthorized_ReturnsUnauthorizedStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_inbox_list")).Returns(false);

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ListInboxAsync_WhenServiceUnavailable_ReturnsUnavailableStatus()
    {
        _authServiceMock.Setup(x => x.CanInvokeTool("sorcha_inbox_list")).Returns(true);
        _availabilityTrackerMock.Setup(x => x.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListInboxAsync_WithSuccessfulResponse_ReturnsActions()
    {
        Allow();

        // Shape of Sorcha.Blueprint.Service.Models.PendingActionSummary, the actual wire body of
        // GET /api/actions/pending — instanceId/actionId, not a single actionInstanceId; urgency,
        // not status.
        var response = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { instanceId = "instance-1", actionId = 1, blueprintId = "bp-1", actionTitle = "Review Document", urgency = "normal", receivedAt = DateTimeOffset.UtcNow.AddHours(-1) },
                new { instanceId = "instance-2", actionId = 2, blueprintId = "bp-2", actionTitle = "Approve Request", urgency = "urgent", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-30) }
            },
            totalCount = 2,
            page = 1,
            pageSize = 20
        });
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Success");
        result.Items[0].InstanceId.Should().Be("instance-1");
        result.Items[0].ActionId.Should().Be(1);
        result.Items[0].ActionTitle.Should().Be("Review Document");
        result.Items[1].Urgency.Should().Be("urgent");
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(x => x.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ListInboxAsync_WithEmptyInbox_ReturnsSuccessWithEmptyList()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0 }));

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Success");
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInboxAsync_WithPagination_PassesQueryParameters()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { items = Array.Empty<object>(), totalCount = 0 }));

        await _tool.ListInboxAsync(page: 2, pageSize: 10);

        _blueprintClientMock.Verify(
            c => c.GetInboxAsync(It.Is<string>(q => q.Contains("page=2") && q.Contains("pageSize=10")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListInboxAsync_WithNull_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ListInboxAsync_WithTimeout_ReturnsTimeoutStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Timeout");
    }

    [Fact]
    public async Task ListInboxAsync_WithHttpException_ReturnsErrorStatus()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetInboxAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.ListInboxAsync();

        result.Status.Should().Be("Error");
    }
}
