// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.Invitation;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyInvitationsTool lists the caller's organisation's register
/// invitations via the typed <see cref="IRegisterInvitationServiceClient"/> (consumer tier).
/// The organisation is derived from the caller's org_id — there is no cross-citizen param.
/// </summary>
public class MyInvitationsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterInvitationServiceClient> _invitationClientMock = new();
    private readonly Mock<ICallerContext> _callerMock = new();

    private MyInvitationsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _invitationClientMock.Object,
        _callerMock.Object,
        Mock.Of<ILogger<MyInvitationsTool>>());

    private readonly Guid _orgId = Guid.NewGuid();

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_invitations")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
        _callerMock.SetupGet(c => c.OrganizationId).Returns(_orgId.ToString());
    }

    private static InvitationSummary Summary(string id) => new()
    {
        InvitationId = id,
        RegisterId = "reg-1",
        RegisterName = "Assured Identity Register",
        SourceOrgDid = "did:sorcha:org:source",
        TargetOrgDid = "did:sorcha:org:target",
        Direction = "received",
        Status = "pending",
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ListAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_invitations")).Returns(false);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Unauthorized");
        _invitationClientMock.Verify(c => c.ListAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_InvalidDirection_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_invitations")).Returns(true);

        var result = await CreateTool().ListAsync("bogus");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("received");
    }

    [Fact]
    public async Task ListAsync_NoOrgId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_invitations")).Returns(true);
        _callerMock.SetupGet(c => c.OrganizationId).Returns((string?)null);

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("org_id");
        _invitationClientMock.Verify(c => c.ListAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_Success_DerivesOrgAndMaps()
    {
        Allow();
        _invitationClientMock.Setup(c => c.ListAsync(_orgId, "received", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationListResponse
            {
                Invitations = [Summary("inv-1")],
                TotalCount = 1
            });

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Success");
        result.Invitations.Should().ContainSingle();
        result.Invitations[0].InvitationId.Should().Be("inv-1");
        result.Invitations[0].RegisterName.Should().Be("Assured Identity Register");
        _invitationClientMock.Verify(c => c.ListAsync(_orgId, "received", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ApiException_ReturnsError()
    {
        Allow();
        _invitationClientMock.Setup(c => c.ListAsync(_orgId, "received", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvitationApiException(HttpStatusCode.Forbidden, "nope"));

        var result = await CreateTool().ListAsync();

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("nope");
    }
}
