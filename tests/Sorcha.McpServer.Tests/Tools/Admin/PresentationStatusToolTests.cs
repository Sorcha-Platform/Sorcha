// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: PresentationStatusTool routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
public class PresentationStatusToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private PresentationStatusTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<PresentationStatusTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task GetStatus_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_status")).Returns(false);

        var result = await CreateTool().GetStatusAsync("req-1");

        result.Status.Should().Be("Unauthorized");
        _blueprintClientMock.Verify(c => c.GetPresentationStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_MissingId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_status")).Returns(true);

        var result = await CreateTool().GetStatusAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetStatus_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_status")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().GetStatusAsync("req-1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task GetStatus_NullBody_ReturnsNotFound()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetPresentationStatusAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().GetStatusAsync("req-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetStatus_Success_ReturnsStatusJson()
    {
        Allow();
        const string body = "{\"state\":\"success\"}";
        _blueprintClientMock
            .Setup(c => c.GetPresentationStatusAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

        var result = await CreateTool().GetStatusAsync("req-1");

        result.Status.Should().Be("Success");
        result.StatusJson.Should().Be(body);
    }
}
