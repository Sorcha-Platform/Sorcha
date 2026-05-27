// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: CredentialSuspendTool routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
public class CredentialSuspendToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private CredentialSuspendTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<CredentialSuspendTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_suspend")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task Suspend_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_suspend")).Returns(false);

        var result = await CreateTool().SuspendAsync("cred-1", "ws1");

        result.Status.Should().Be("Unauthorized");
        _blueprintClientMock.Verify(c => c.SuspendCredentialAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Suspend_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_suspend")).Returns(true);

        var result = await CreateTool().SuspendAsync("", "ws1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task Suspend_Success_ReturnsResultJson()
    {
        Allow();
        const string body = "{\"status\":\"Suspended\"}";
        _blueprintClientMock
            .Setup(c => c.SuspendCredentialAsync("cred-1", "ws1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

        var result = await CreateTool().SuspendAsync("cred-1", "ws1");

        result.Status.Should().Be("Success");
        result.ResultJson.Should().Be(body);
    }
}
