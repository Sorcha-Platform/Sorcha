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
/// Spec 139 US4: BlueprintUpdateTool writes via the typed <see cref="IBlueprintServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintUpdateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintUpdateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintUpdateTool>>());

    private static string ValidJson() => JsonSerializer.Serialize(new
    {
        title = "Updated Blueprint",
        participants = new[] { new { id = "p1", name = "P1" }, new { id = "p2", name = "P2" } },
        actions = new[] { new { id = 0, title = "Action 1" } }
    });

    [Fact]
    public async Task UpdateBlueprintAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(false);

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);

        var result = await CreateTool().UpdateBlueprintAsync("", ValidJson());

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_EmptyJson_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_InvalidJson_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", "{ invalid }");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_MissingTitle_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);

        var json = JsonSerializer.Serialize(new
        {
            participants = new[] { new { id = "p1", name = "P1" }, new { id = "p2", name = "P2" } },
            actions = new[] { new { id = 0, title = "Action 1" } }
        });

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", json);

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_SuccessfulUpdate_ReturnsSuccessResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        // Field names as Sorcha.Blueprint.Models.Blueprint actually serializes them: there is no
        // "status" property at all, and the timestamp field is "updatedAt", not "modifiedAt".
        var updatedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        _blueprintClientMock
            .Setup(c => c.UpdateBlueprintAsync("bp-123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { id = "bp-123", title = "Updated Blueprint", version = 2, updatedAt }));

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        result.Status.Should().Be("Success");
        result.Blueprint!.Id.Should().Be("bp-123");
        result.Blueprint.Title.Should().Be("Updated Blueprint");
        result.Blueprint.Version.Should().Be(2);
        result.Blueprint.ModifiedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task UpdateBlueprintAsync_ServiceReturnsNull_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        _blueprintClientMock
            .Setup(c => c.UpdateBlueprintAsync("bp-123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task UpdateBlueprintAsync_PassesBlueprintIdToClient()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        _blueprintClientMock
            .Setup(c => c.UpdateBlueprintAsync("bp-123", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Id = "bp-123", Title = "Updated Blueprint", Version = 2 }));

        await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        _blueprintClientMock.Verify(
            c => c.UpdateBlueprintAsync("bp-123", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateBlueprintAsync_ResponseTimeIsRecorded()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_update")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);

        _blueprintClientMock
            .Setup(c => c.UpdateBlueprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Id = "bp-123", Title = "T", Version = 2 }));

        var result = await CreateTool().UpdateBlueprintAsync("bp-123", ValidJson());

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// Fixture-level tests against the real wire shape of <c>Sorcha.Blueprint.Models.Blueprint</c>
/// (see <c>src/Common/Sorcha.Blueprint.Models/Blueprint.cs</c>) — the type
/// <c>PUT /api/blueprints/{id}</c> returns. That type has no <c>status</c> property at all, and
/// its modification timestamp serializes as <c>updatedAt</c>, never <c>modifiedAt</c>. A fixture
/// written with the tool's original (wrong) field names would pass against the tool's own bug;
/// this one uses the server's real names, so it fails against the pre-fix mapping.
/// </summary>
public sealed class BlueprintUpdateResponseParsingTests
{
    private const string ServerBody = """
        {
          "id": "bp-123",
          "title": "Updated Blueprint",
          "version": 2,
          "updatedAt": "2026-09-01T12:00:00Z"
        }
        """;

    [Fact]
    public void Parse_ServerBody_ReadsUpdatedAtNotModifiedAt()
    {
        var parsed = BlueprintUpdateTool.ParseBlueprintResponse(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.UpdatedAt.Should().Be(DateTimeOffset.Parse("2026-09-01T12:00:00Z"));
    }

    [Fact]
    public void Parse_ServerBody_HasNoStatusProperty()
    {
        // Sorcha.Blueprint.Models.Blueprint has no "status" property at all — the DTO must not
        // declare one either, since it can only ever deserialize to a misleading always-null value.
        var properties = typeof(BlueprintUpdateTool.BlueprintResponse).GetProperties();

        properties.Should().NotContain(p => p.Name == "Status");
    }
}
