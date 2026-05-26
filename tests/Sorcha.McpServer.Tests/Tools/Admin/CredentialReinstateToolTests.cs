// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: CredentialReinstateTool routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
public class CredentialReinstateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private CredentialReinstateTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<CredentialReinstateTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_reinstate")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task Reinstate_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_reinstate")).Returns(false);

        var result = await CreateTool().ReinstateAsync("cred-1", "ws1");

        result.Status.Should().Be("Unauthorized");
        _blueprintClientMock.Verify(c => c.ReinstateCredentialAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reinstate_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_reinstate")).Returns(true);

        var result = await CreateTool().ReinstateAsync("cred-1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task Reinstate_Success_ReturnsResultJson()
    {
        Allow();
        const string body = "{\"status\":\"Active\"}";
        _blueprintClientMock
            .Setup(c => c.ReinstateCredentialAsync("cred-1", "ws1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

        var result = await CreateTool().ReinstateAsync("cred-1", "ws1");

        result.Status.Should().Be("Success");
        result.ResultJson.Should().Be(body);
    }
}
