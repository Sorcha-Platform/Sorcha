// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: CredentialRevokeTool routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
public class CredentialRevokeToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private CredentialRevokeTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<CredentialRevokeTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task Revoke_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_revoke")).Returns(false);

        var result = await CreateTool().RevokeAsync("cred-1", "ws1");

        result.Status.Should().Be("Unauthorized");
        _blueprintClientMock.Verify(c => c.RevokeCredentialAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_MissingArgs_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_revoke")).Returns(true);

        var result = await CreateTool().RevokeAsync("cred-1", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task Revoke_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_revoke")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().RevokeAsync("cred-1", "ws1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task Revoke_NullBody_ReturnsError()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.RevokeCredentialAsync("cred-1", "ws1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().RevokeAsync("cred-1", "ws1");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("not accepted");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task Revoke_Success_ReturnsResultJson()
    {
        Allow();
        const string body = "{\"status\":\"Revoked\"}";
        _blueprintClientMock
            .Setup(c => c.RevokeCredentialAsync("cred-1", "ws1", "compromise", It.IsAny<CancellationToken>()))
            .ReturnsAsync(body);

        var result = await CreateTool().RevokeAsync("cred-1", "ws1", "compromise");

        result.Status.Should().Be("Success");
        result.ResultJson.Should().Be(body);
        _blueprintClientMock.Verify(c => c.RevokeCredentialAsync("cred-1", "ws1", "compromise", It.IsAny<CancellationToken>()), Times.Once);
    }
}
