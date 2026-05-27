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
/// Spec 139 US4: BlueprintExportTool reads via the typed <see cref="IBlueprintServiceClient.GetBlueprintAsync"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintExportToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintExportTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintExportTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_export")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    private static readonly string BlueprintJson = JsonSerializer.Serialize(new
    {
        id = "bp-123",
        title = "Test Blueprint",
        description = "A test blueprint"
    });

    [Fact]
    public async Task ExportBlueprintAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_export")).Returns(false);

        var result = await CreateTool().ExportBlueprintAsync("bp-123");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task ExportBlueprintAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_export")).Returns(true);

        var result = await CreateTool().ExportBlueprintAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ExportBlueprintAsync_InvalidFormat_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_export")).Returns(true);

        var result = await CreateTool().ExportBlueprintAsync("bp-123", format: "xml");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ExportBlueprintAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_export")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().ExportBlueprintAsync("bp-123");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ExportBlueprintAsync_JsonFormat_ReturnsJsonContent()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BlueprintJson);

        var result = await CreateTool().ExportBlueprintAsync("bp-123", format: "json");

        result.Status.Should().Be("Success");
        result.Format.Should().Be("json");
        result.ContentLength.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportBlueprintAsync_YamlFormat_ReturnsYamlContent()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BlueprintJson);

        var result = await CreateTool().ExportBlueprintAsync("bp-123", format: "yaml");

        result.Status.Should().Be("Success");
        result.Format.Should().Be("yaml");
        result.ContentLength.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportBlueprintAsync_DefaultsToJson()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BlueprintJson);

        var result = await CreateTool().ExportBlueprintAsync("bp-123");

        result.Format.Should().Be("json");
    }

    [Fact]
    public async Task ExportBlueprintAsync_BlueprintNotFound_ReturnsErrorResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().ExportBlueprintAsync("bp-123");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task ExportBlueprintAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BlueprintJson);

        var result = await CreateTool().ExportBlueprintAsync("bp-123");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
