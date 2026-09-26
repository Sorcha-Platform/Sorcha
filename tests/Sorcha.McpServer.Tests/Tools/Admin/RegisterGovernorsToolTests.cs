// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// #1647 — an agent refused by a governance/publish 403 had no MCP path to the register's Owner
/// and decoded a genesis transaction over SSH to find it. These pin that
/// <see cref="RegisterGovernorsTool"/> reports every named role holder (Owner first), that a
/// missing sealed roster is reported as exactly that (never "does not exist"), and that a 403 is
/// reported as a refusal — never counted against the Register service's availability, matching
/// #1673's lesson.
/// </summary>
public class RegisterGovernorsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private RegisterGovernorsTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<RegisterGovernorsTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_governors")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetGovernorsAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_governors")).Returns(false);

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        result.Members.Should().BeNull();
        _registerClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetGovernorsAsync_EmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_governors")).Returns(true);

        var result = await CreateTool().GetGovernorsAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetGovernorsAsync_Success_ReturnsMembersWithOwnerFirst()
    {
        Allow();
        var roster = new GovernanceRosterResponse
        {
            RegisterId = "reg-1",
            Members =
            [
                new RosterMember { Subject = "did:sorcha:w:admin1", Role = "Admin", Algorithm = "ed25519", GrantedAt = DateTimeOffset.UtcNow },
                new RosterMember { Subject = "did:sorcha:w:owner1", Role = "Owner", Algorithm = "ed25519", GrantedAt = DateTimeOffset.UtcNow },
            ],
            MemberCount = 2,
            ControlTransactionCount = 1,
            LastControlTxId = "tx-1"
        };
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.Found(roster));

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Success");
        result.RegisterId.Should().Be("reg-1");
        result.MemberCount.Should().Be(2);
        result.LastControlTxId.Should().Be("tx-1");
        result.Members.Should().HaveCount(2);
        result.Members![0].Role.Should().Be("Owner");
        result.Members![0].Subject.Should().Be("did:sorcha:w:owner1");
        result.Members![1].Role.Should().Be("Admin");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>(), It.IsAny<Exception?>()), Times.Never);
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGovernorsAsync_NotFound_ReportsNoSealedRoster_NotDoesNotExist()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.NotFound());

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("NotFound");
        result.Message.Should().Contain("no sealed governance roster");
        result.Message.Should().NotContain("does not exist");
        result.Members.Should().BeNull();
    }

    [Fact]
    public async Task GetGovernorsAsync_403_IsRefused_NotAnOutage()
    {
        // #1673: a 4xx is the service answering, not evidence it is down. RecordFailure must NOT
        // be called, or three deterministic refusals would trip the availability breaker and take
        // out every other Register MCP tool behind a false "service unavailable".
        Allow();
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.Unavailable(403));

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("403");
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>(), It.IsAny<Exception?>()), Times.Never);
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Never);
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetGovernorsAsync_401_IsUnauthorized_NotAnOutage()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.Unavailable(401));

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>(), It.IsAny<Exception?>()), Times.Never);
        _availabilityTrackerMock.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGovernorsAsync_5xx_IsAnOutage_RecordsFailure()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.Unavailable(503));

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Register"), Times.Once);
    }

    [Fact]
    public async Task GetGovernorsAsync_TransportFailure_IsAnOutage_RecordsFailure()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetGovernanceRosterAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GovernanceRosterLookup.Unavailable());

        var result = await CreateTool().GetGovernorsAsync("reg-1");

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Register"), Times.Once);
    }
}
