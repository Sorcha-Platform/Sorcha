// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyCredentialsTool is a consumer-tier citizen tool that lists the
/// caller's own credentials via the typed <see cref="ICitizenWalletClient"/>.
/// </summary>
public class MyCredentialsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private MyCredentialsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<MyCredentialsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_credentials")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task ListAsync_Unauthorized_ReturnsUnauthorized()
    {
        // A platform-admin-only context (or any caller without the consumer-tier entitlement)
        // is refused: this is a consumer tool, so CanInvokeTool returns false there.
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_credentials")).Returns(false);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.ListCredentialsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_credentials")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(false);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task ListAsync_Success_MapsCredentials()
    {
        Allow();
        _walletClientMock.Setup(c => c.ListCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialListResponse
            {
                Credentials =
                [
                    new CachedCredentialPayload
                    {
                        Id = "urn:credential:abc",
                        Vct = "AssuredIdentity",
                        IssuerDid = "did:sorcha:org:ws1abc",
                        IssuedAt = DateTimeOffset.UtcNow,
                        Jwt = "header.payload.sig"
                    }
                ]
            });

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Success");
        result.Credentials.Should().ContainSingle();
        result.Credentials[0].Id.Should().Be("urn:credential:abc");
        result.Credentials[0].Vct.Should().Be("AssuredIdentity");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Wallet"), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ClientThrows_ReturnsError()
    {
        Allow();
        _walletClientMock.Setup(c => c.ListCredentialsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Wallet", It.IsAny<Exception>()), Times.Once);
    }
}
