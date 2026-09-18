// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// #1664 — publishing a participant record is what binds a blueprint role to a wallet on a register.
/// Without one, a later action by that role is accepted and then refused by the validator with no
/// readable reason, which is where cold-start run #4 stalled.
/// </summary>
public sealed class ParticipantPublishToolTests
{
    private const string Org = "11111111-1111-1111-1111-111111111111";
    private const string Mine = "ws1qmine";
    private const string AlsoMine = "ws1qalso";

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<ITenantServiceClient> _tenant = new();
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly Mock<ICallerContext> _caller = new();
    private readonly ParticipantPublishTool _tool;

    public ParticipantPublishToolTests()
    {
        _tool = new ParticipantPublishTool(
            _auth.Object, _availability.Object, _tenant.Object, _wallet.Object, _caller.Object,
            Mock.Of<ILogger<ParticipantPublishTool>>());
    }

    private void Allow(string? org = Org)
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_participant_publish")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Tenant")).Returns(true);
        _caller.Setup(c => c.OrganizationId).Returns(org);
        _caller.Setup(c => c.PlatformUserId).Returns("user-1");
    }

    private static Sorcha.ServiceClients.Wallet.WalletInfo Wallet(string address) => new()
    {
        Address = address, Name = "w", PublicKey = "pk-" + address, Algorithm = "ED25519",
        Status = "Active", Owner = "user-1", Tenant = "t",
    };

    private void CallerHolds(params string[] addresses) =>
        _wallet.Setup(w => w.GetWalletsByOwnerAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(addresses.Select(Wallet).ToList());

    private void TenantReturns(HttpStatusCode status, string body) =>
        _tenant.Setup(t => t.PublishParticipantRecordAsync(Org, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((status, body));

    private const string Accepted = """{"transactionId":"tx-participant-1","participantId":"9f50e29b"}""";

    [Fact]
    public async Task Publish_WhenUnauthorized_ReturnsUnauthorized()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_participant_publish")).Returns(false);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Unauthorized");
        _tenant.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "provider", "Provider Org")]
    [InlineData("reg-1", "", "Provider Org")]
    [InlineData("reg-1", "provider", "")]
    public async Task Publish_WithMissingArguments_ReturnsErrorAndPublishesNothing(string reg, string participant, string org)
    {
        Allow();

        var result = await _tool.PublishParticipantAsync(reg, participant, org);

        result.Status.Should().Be("Error");
        _tenant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Publish_WhenTokenCarriesNoOrganisation_RefusesWithoutPublishing()
    {
        // The record binds a role to an ORGANISATION's wallet, so only that organisation may publish it.
        Allow(org: null);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("org_id");
        _tenant.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Publish_SendsTheRoleIdAsTheRecordName_BecauseThatIsWhatTheValidatorMatches()
    {
        // The record's own participantId is a GUID the Tenant Service assigns, so a role resolves by NAME.
        Allow();
        CallerHolds(Mine);
        string? sentJson = null;
        _tenant.Setup(t => t.PublishParticipantRecordAsync(Org, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string json, CancellationToken _) => sentJson = json)
            .ReturnsAsync((HttpStatusCode.Accepted, Accepted));

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Success");
        result.TransactionId.Should().Be("tx-participant-1");
        using var doc = JsonDocument.Parse(sentJson!);
        doc.RootElement.GetProperty("participantName").GetString().Should().Be("provider");
        doc.RootElement.GetProperty("organizationName").GetString().Should().Be("Provider Org");
        doc.RootElement.GetProperty("registerId").GetString().Should().Be("reg-1");
        doc.RootElement.GetProperty("signerWalletAddress").GetString().Should().Be(Mine);
        var address = doc.RootElement.GetProperty("addresses")[0];
        address.GetProperty("walletAddress").GetString().Should().Be(Mine);
        address.GetProperty("publicKey").GetString().Should().Be("pk-" + Mine);
        address.GetProperty("primary").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Publish_SuccessMessage_SaysTheBindingIsNotEffectiveUntilItSeals()
    {
        // The lag is the whole trap: publishing is a register transaction, and the role cannot act until
        // it seals. Run #4's walkthrough helper waits for exactly this.
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.Accepted, Accepted);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Message.Should().Contain("seal").And.Contain("sorcha_participant_list");
    }

    [Fact]
    public async Task Publish_UsesTheCallersOwnWalletWhenOnlyOne()
    {
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.Accepted, Accepted);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.WalletAddress.Should().Be(Mine);
    }

    [Fact]
    public async Task Publish_WhenCallerHoldsSeveralWallets_AsksWhichAndPublishesNothing()
    {
        Allow();
        CallerHolds(Mine, AlsoMine);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain(Mine).And.Contain(AlsoMine).And.Contain("walletAddress");
        _tenant.Verify(t => t.PublishParticipantRecordAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_WithNamedWallet_ReadsThatWalletsKey()
    {
        Allow();
        _wallet.Setup(w => w.GetWalletAsync(AlsoMine, It.IsAny<CancellationToken>())).ReturnsAsync(Wallet(AlsoMine));
        TenantReturns(HttpStatusCode.Accepted, Accepted);

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org", walletAddress: AlsoMine);

        result.Status.Should().Be("Success");
        result.WalletAddress.Should().Be(AlsoMine);
        _wallet.Verify(w => w.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_WhenCallerHoldsNoWallet_SaysSoAndPublishesNothing()
    {
        Allow();
        CallerHolds();

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("no wallet");
        _tenant.Verify(t => t.PublishParticipantRecordAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_WhenTheWalletIsAlreadyClaimed_IsRefusedWithTheReason()
    {
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.Conflict, "\"Wallet ws1qmine is already claimed by participant 'requester'\"");

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("already claimed").And.Contain("different wallet");
    }

    [Fact]
    public async Task Publish_OnValidationFailure_ReportsTheFields()
    {
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.BadRequest,
            """{"errors":{"organizationName":["Organization name is required"]}}""");

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("organizationName").And.Contain("required");
    }

    [Fact]
    public async Task Publish_OnForbidden_IsRefused_AndNotCountedAsAnOutage()
    {
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.Forbidden, """{"detail":"Administrator role required."}""");

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("Administrator role required.");
        _availability.Verify(x => x.RecordFailure("Tenant", It.IsAny<Exception?>()), Times.Never);
    }

    [Fact]
    public async Task Publish_OnTransportFailure_RecordsAnOutage()
    {
        Allow();
        CallerHolds(Mine);
        _tenant.Setup(t => t.PublishParticipantRecordAsync(Org, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        result.Status.Should().Be("Error");
        _availability.Verify(x => x.RecordFailure("Tenant", It.IsAny<Exception?>()), Times.Once);
    }

    [Fact]
    public async Task Publish_AlwaysPublishesForTheCallersOwnOrganisation()
    {
        Allow();
        CallerHolds(Mine);
        TenantReturns(HttpStatusCode.Accepted, Accepted);

        await _tool.PublishParticipantAsync("reg-1", "provider", "Provider Org");

        _tenant.Verify(t => t.PublishParticipantRecordAsync(Org, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
