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
/// Feature 140 Wave 3: MyPresentationsTool lists the caller's own presentation history
/// via the typed <see cref="ICitizenWalletClient"/> (consumer tier).
/// </summary>
public class MyPresentationsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ICitizenWalletClient> _walletClientMock = new();

    private MyPresentationsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _walletClientMock.Object,
        Mock.Of<ILogger<MyPresentationsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_presentations")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Wallet")).Returns(true);
    }

    [Fact]
    public async Task ListAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_presentations")).Returns(false);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Unauthorized");
        _walletClientMock.Verify(c => c.ListPresentationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_Success_MapsEntries()
    {
        Allow();
        var id = Guid.NewGuid();
        var credId = Guid.NewGuid();
        _walletClientMock.Setup(c => c.ListPresentationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PresentationLogEntry
                {
                    Id = id,
                    CredentialId = credId,
                    VerifierLabel = "Acme Council",
                    DisclosedClaims = ["given_name", "family_name"],
                    PresentedAt = DateTimeOffset.UtcNow,
                    Outcome = PresentationLogOutcome.Acknowledged
                }
            ]);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Success");
        result.Presentations.Should().ContainSingle();
        result.Presentations[0].Id.Should().Be(id);
        result.Presentations[0].CredentialId.Should().Be(credId);
        result.Presentations[0].Outcome.Should().Be("Acknowledged");
        result.Presentations[0].DisclosedClaims.Should().BeEquivalentTo("given_name", "family_name");
    }

    [Fact]
    public async Task ListAsync_Empty_ReturnsSuccessWithEmpty()
    {
        Allow();
        _walletClientMock.Setup(c => c.ListPresentationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Success");
        result.Presentations.Should().BeEmpty();
    }
}
