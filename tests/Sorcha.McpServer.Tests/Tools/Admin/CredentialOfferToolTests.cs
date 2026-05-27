// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Haip;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: CredentialOfferTool routes through the typed <see cref="IHaipServiceClient"/>.
/// </summary>
public class CredentialOfferToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IHaipServiceClient> _haipClientMock = new();

    private CredentialOfferTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _haipClientMock.Object,
        Mock.Of<ILogger<CredentialOfferTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_offer")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task CreateOrStatus_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_offer")).Returns(false);

        var result = await CreateTool().CreateOrStatusAsync("ws1", "tenant-1", "IdCred", "{}");

        result.Status.Should().Be("Unauthorized");
        _haipClientMock.Verify(c => c.CreateCredentialOfferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrStatus_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_credential_offer")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().CreateOrStatusAsync("ws1", "tenant-1", "IdCred", "{}");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task CreateOrStatus_MissingCreateArgs_ReturnsError()
    {
        Allow();

        var result = await CreateTool().CreateOrStatusAsync(issuerWalletAddress: "", tenantId: "tenant-1", credentialType: "IdCred");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CreateOrStatus_InvalidClaimsJson_ReturnsError()
    {
        Allow();

        var result = await CreateTool().CreateOrStatusAsync("ws1", "tenant-1", "IdCred", "{not-json");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task CreateOrStatus_Create_Succeeds()
    {
        Allow();
        var offerId = Guid.NewGuid();
        _haipClientMock
            .Setup(c => c.CreateCredentialOfferAsync("ws1", "tenant-1", "IdCred",
                It.IsAny<Dictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateOfferResult(offerId, "openid-credential-offer://x", "pac-1", DateTimeOffset.UtcNow.AddMinutes(10)));

        var result = await CreateTool().CreateOrStatusAsync("ws1", "tenant-1", "IdCred", "{\"name\":\"Ada\"}");

        result.Status.Should().Be("Success");
        result.OfferId.Should().Be(offerId);
        result.CredentialOfferUri.Should().Be("openid-credential-offer://x");
        result.PreAuthorizedCode.Should().Be("pac-1");
    }

    [Fact]
    public async Task CreateOrStatus_StatusWhenOfferIdSupplied_Succeeds()
    {
        Allow();
        var offerId = Guid.NewGuid();
        _haipClientMock
            .Setup(c => c.GetOfferStatusAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OfferStatusResult(offerId, "IdCred", "claimed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5)));

        var result = await CreateTool().CreateOrStatusAsync(offerId: offerId.ToString());

        result.Status.Should().Be("Success");
        result.OfferStatus.Should().Be("claimed");
        _haipClientMock.Verify(c => c.CreateCredentialOfferAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrStatus_StatusNotFound_ReturnsNotFound()
    {
        Allow();
        var offerId = Guid.NewGuid();
        _haipClientMock
            .Setup(c => c.GetOfferStatusAsync(offerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OfferStatusResult?)null);

        var result = await CreateTool().CreateOrStatusAsync(offerId: offerId.ToString());

        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateOrStatus_BadOfferGuid_ReturnsError()
    {
        Allow();

        var result = await CreateTool().CreateOrStatusAsync(offerId: "not-a-guid");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("GUID");
    }
}
