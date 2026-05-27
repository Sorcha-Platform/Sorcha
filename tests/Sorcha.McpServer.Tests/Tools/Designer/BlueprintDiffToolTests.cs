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
/// Spec 139 US4: BlueprintDiffTool reads via the typed <see cref="IBlueprintServiceClient.GetBlueprintDiffAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintDiffToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintDiffTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintDiffTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_diff")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_diff")).Returns(false);

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1);

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_diff")).Returns(true);

        var result = await CreateTool().CompareBlueprintVersionsAsync("", 1);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_InvalidFromVersion_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_diff")).Returns(true);

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 0);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_diff")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1);

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_WithChanges_ReturnsSuccessResult()
    {
        Allow();

        var diff = JsonSerializer.Serialize(new
        {
            FromVersion = 1,
            ToVersion = 2,
            Changes = new[]
            {
                new { Path = "/title", ChangeType = "Modified", OldValue = "Old Title", NewValue = "New Title" },
                new { Path = "/description", ChangeType = "Modified", OldValue = "Old", NewValue = "New" }
            }
        });
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync("bp-123", 1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(diff);

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1, 2);

        result.Status.Should().Be("Success");
        result.FromVersion.Should().Be(1);
        result.ToVersion.Should().Be(2);
        result.Changes[0].Path.Should().Be("/title");
        result.Changes[0].ChangeType.Should().Be("Modified");
        result.Changes[0].OldValue.Should().Be("Old Title");
        result.Changes[0].NewValue.Should().Be("New Title");
        result.TotalChanges.Should().Be(2);
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_NoChanges_ReturnsSuccessResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync("bp-123", 1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { FromVersion = 1, ToVersion = 2, Changes = Array.Empty<object>() }));

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1, 2);

        result.Status.Should().Be("Success");
        result.Changes.Should().BeEmpty();
        result.TotalChanges.Should().Be(0);
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_ToLatest_PassesNullToVersion()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync("bp-123", 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { FromVersion = 1, ToVersion = 3, Changes = Array.Empty<object>() }));

        await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1);

        _blueprintClientMock.Verify(
            c => c.GetBlueprintDiffAsync("bp-123", 1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_ToSpecificVersion_PassesToVersion()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync("bp-123", 1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { FromVersion = 1, ToVersion = 2, Changes = Array.Empty<object>() }));

        await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1, 2);

        _blueprintClientMock.Verify(
            c => c.GetBlueprintDiffAsync("bp-123", 1, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_ServiceReturnsNull_ReturnsErrorResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CompareBlueprintVersionsAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintDiffAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { FromVersion = 1, ToVersion = 2, Changes = Array.Empty<object>() }));

        var result = await CreateTool().CompareBlueprintVersionsAsync("bp-123", 1);

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
