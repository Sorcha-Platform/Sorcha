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
/// Spec 139 US4: BlueprintListTool reads via the typed <see cref="IBlueprintServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintListToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintListTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintListTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    private static string PagedJson(object items, int page, int pageSize, int totalCount, int totalPages) =>
        JsonSerializer.Serialize(new { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages });

    [Fact]
    public async Task ListBlueprintsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_list")).Returns(false);

        var result = await CreateTool().ListBlueprintsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.Blueprints.Should().BeEmpty();
    }

    [Fact]
    public async Task ListBlueprintsAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_list")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().ListBlueprintsAsync();

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ListBlueprintsAsync_ReturnsBlueprints_ReturnsSuccessResult()
    {
        Allow();

        var items = new[]
        {
            new { Id = "bp-1", Title = "Test Blueprint 1", Description = "First", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5), ParticipantCount = 3, ActionCount = 5 },
            new { Id = "bp-2", Title = "Test Blueprint 2", Description = "Second", CreatedAt = DateTimeOffset.UtcNow.AddDays(-8), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2), ParticipantCount = 2, ActionCount = 3 }
        };
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedJson(items, 1, 20, 2, 1));

        var result = await CreateTool().ListBlueprintsAsync();

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("2 of 2 blueprints");
        result.Blueprints.Should().HaveCount(2);
        result.Blueprints[0].Id.Should().Be("bp-1");
        result.Blueprints[0].ParticipantCount.Should().Be(3);
        result.TotalCount.Should().Be(2);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task ListBlueprintsAsync_NoBlueprints_ReturnsEmptyResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedJson(Array.Empty<object>(), 1, 20, 0, 0));

        var result = await CreateTool().ListBlueprintsAsync();

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("No blueprints found");
        result.Blueprints.Should().BeEmpty();
    }

    [Fact]
    public async Task ListBlueprintsAsync_PassesFilterAndPaginationToClient()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(3, 10, "Loan", "Published", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedJson(Array.Empty<object>(), 3, 10, 0, 0));

        await CreateTool().ListBlueprintsAsync(page: 3, pageSize: 10, search: "Loan", status: "Published");

        _blueprintClientMock.Verify(
            c => c.ListBlueprintsAsync(3, 10, "Loan", "Published", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListBlueprintsAsync_ClampsPageSizeTo100()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(It.IsAny<int>(), 100, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedJson(Array.Empty<object>(), 1, 100, 0, 0));

        await CreateTool().ListBlueprintsAsync(pageSize: 500);

        _blueprintClientMock.Verify(
            c => c.ListBlueprintsAsync(It.IsAny<int>(), 100, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListBlueprintsAsync_ServiceReturnsNull_ReturnsErrorResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ListBlueprintsAsync();

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Failed to retrieve");
    }

    [Fact]
    public async Task ListBlueprintsAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.ListBlueprintsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedJson(Array.Empty<object>(), 1, 20, 0, 0));

        var result = await CreateTool().ListBlueprintsAsync();

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
