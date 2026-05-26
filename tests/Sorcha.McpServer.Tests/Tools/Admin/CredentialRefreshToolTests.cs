// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: CredentialRefreshTool routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
public class CredentialRefreshToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private CredentialRefreshTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<CredentialRefreshTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_refresh")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task Refresh_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_refresh")).Returns(false);

        var result = await CreateTool().RefreshAsync("cred-1", "ws1");

        result.Status.Should().Be("Unauthorized");
        _blueprintClientMock.Verify(c => c.RefreshCredentialAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_refresh")).Returns(true);

        var result = await CreateTool().RefreshAsync("", "ws1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task Refresh_Success_PassesDurationAndReturnsResultJson()
    {
        Allow();
        const string body = "{\"newCredential\":{\"status\":\"Active\"}}";
        _blueprintClientMock
            .Setup(c => c.RefreshCredentialAsync("cred-1", "ws1", "P30D", It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

        var result = await CreateTool().RefreshAsync("cred-1", "ws1", "P30D");

        result.Status.Should().Be("Success");
        result.ResultJson.Should().Be(body);
        _blueprintClientMock.Verify(c => c.RefreshCredentialAsync("cred-1", "ws1", "P30D", It.IsAny<CancellationToken>()), Times.Once);
    }
}
