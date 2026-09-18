// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// #1664 — reading which blueprint roles are bound to which wallets on a register. This is how a caller
/// confirms a participant record has sealed before submitting that role's action, instead of submitting
/// and waiting for a validator refusal it cannot see.
/// </summary>
public sealed class ParticipantListToolTests
{
    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<IRegisterServiceClient> _register = new();
    private readonly ParticipantListTool _tool;

    public ParticipantListToolTests()
    {
        _tool = new ParticipantListTool(
            _auth.Object, _availability.Object, _register.Object, Mock.Of<ILogger<ParticipantListTool>>());
    }

    private void Allow()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_participant_list")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Register")).Returns(true);
    }

    private static PublishedParticipantRecord Record(string name, string wallet, string status = "Active") => new()
    {
        ParticipantId = "9f50e29b-guid", ParticipantName = name, OrganizationName = "Provider Org",
        Status = status, Version = 1, LatestTxId = "tx-1",
        Addresses = [new ParticipantAddressInfo
        {
            WalletAddress = wallet, PublicKey = "pk", Algorithm = "ED25519", Primary = true,
        }],
    };

    private void RegisterReturns(params PublishedParticipantRecord[] records) =>
        _register.Setup(r => r.GetPublishedParticipantsAsync(
                "reg-1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantPage { Page = 1, PageSize = 20, Total = records.Length, Participants = [.. records] });

    [Fact]
    public async Task List_WhenUnauthorized_ReturnsUnauthorized()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_participant_list")).Returns(false);

        var result = await _tool.ListParticipantsAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task List_WithNoRegisterId_ReturnsError()
    {
        Allow();

        var result = await _tool.ListParticipantsAsync("");

        result.Status.Should().Be("Error");
        _register.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_ReportsTheRoleNameAndItsWallets()
    {
        Allow();
        RegisterReturns(Record("provider", "ws1qprovider"));

        var result = await _tool.ListParticipantsAsync("reg-1");

        result.Status.Should().Be("Success");
        var participant = result.Participants.Should().ContainSingle().Subject;
        participant.ParticipantName.Should().Be("provider", "the name is what a blueprint role is matched against");
        participant.WalletAddresses.Should().ContainSingle().Which.Should().Be("ws1qprovider");
        participant.RecordId.Should().Be("9f50e29b-guid");
        participant.Status.Should().Be("Active");
    }

    [Fact]
    public async Task List_WhenNothingIsPublished_SaysNoRoleIsBound_NotThatTheRegisterIsUnreachable()
    {
        // An empty list is a fact about the register, not an outage — the mistake #1646 made.
        Allow();
        RegisterReturns();

        var result = await _tool.ListParticipantsAsync("reg-1");

        result.Status.Should().Be("Success");
        result.Participants.Should().BeEmpty();
        result.Message.Should().Contain("No participant records").And.Contain("before it can send an action");
    }

    [Fact]
    public async Task List_ByDefault_AsksForActiveRecordsOnly()
    {
        // Only an active record binds a role, so a revoked one must not read as a binding.
        Allow();
        RegisterReturns(Record("provider", "ws1qprovider"));

        await _tool.ListParticipantsAsync("reg-1");

        _register.Verify(r => r.GetPublishedParticipantsAsync(
            "reg-1", 0, 20, "active", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_WithIncludeRevoked_AsksForAll()
    {
        Allow();
        RegisterReturns(Record("provider", "ws1qprovider", status: "Revoked"));

        await _tool.ListParticipantsAsync("reg-1", includeRevoked: true);

        _register.Verify(r => r.GetPublishedParticipantsAsync(
            "reg-1", 0, 20, "all", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_PagesFromOne()
    {
        Allow();
        RegisterReturns(Record("provider", "ws1qprovider"));

        await _tool.ListParticipantsAsync("reg-1", page: 3, pageSize: 10);

        _register.Verify(r => r.GetPublishedParticipantsAsync(
            "reg-1", 20, 10, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_OnTransportFailure_RecordsAnOutage()
    {
        Allow();
        _register.Setup(r => r.GetPublishedParticipantsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.ListParticipantsAsync("reg-1");

        result.Status.Should().Be("Error");
        _availability.Verify(x => x.RecordFailure("Register", It.IsAny<Exception?>()), Times.Once);
    }
}
