// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Participant;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>Unit tests for <see cref="WalletInboxWriter"/>. Phase 5 follow-up #2 of Feature 118.</summary>
public class WalletInboxWriterTests
{
    private readonly Mock<IParticipantServiceClient> _participants = new();
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private readonly WalletInboxWriter _sut;

    public WalletInboxWriterTests()
    {
        _sut = new WalletInboxWriter(_participants.Object, _inbox.Object, NullLogger<WalletInboxWriter>.Instance);
    }

    [Fact]
    public async Task WriteCredentialReceivedAsync_HappyPath_PostsExpectedPayload()
    {
        var participant = BuildParticipant();
        var platformUserId = Guid.NewGuid();
        InboxWritePayload? captured = null;

        _participants.Setup(p => p.GetByWalletAddressAsync("recipient-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(participant.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUserId);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Verified Citizen", "Acme Inc.");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(platformUserId);
        captured.Category.Should().Be("Credential");
        captured.CorrelationKey.Should().Be("credential:recipient-wallet:cred-abc");
        captured.DetailHref.Should().Be("/api/v1/wallets/recipient-wallet/credentials/cred-abc");
        captured.Title.Should().Be("Acme Inc. issued you a Verified Citizen");
    }

    [Fact]
    public async Task WriteCredentialReceivedAsync_NoIssuerOrgName_FallsBackToTypeOnly()
    {
        var participant = BuildParticipant();
        InboxWritePayload? captured = null;
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Driving Licence");

        captured!.Title.Should().Be("New credential: Driving Licence");
    }

    [Fact]
    public async Task WriteCredentialReceivedAsync_NoParticipant_SkipsInbox()
    {
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        await _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Verified Citizen");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteCredentialReceivedAsync_DeterministicSourceEventId_CollapsesDuplicates()
    {
        var participant = BuildParticipant();
        var sourceIds = new List<Guid>();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: true));

        await _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Verified Citizen");
        await _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Verified Citizen");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1]);
    }

    [Fact]
    public async Task WriteCredentialReceivedAsync_InboxThrows_DoesNotPropagate()
    {
        var participant = BuildParticipant();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act = () => _sut.WriteCredentialReceivedAsync("recipient-wallet", "cred-abc", "Verified Citizen");

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block credential issuance");
    }

    // -------------------------------------------------------------------------
    // Phase 2b: credential-declined / -deleted / presentation-submitted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WriteCredentialDeclinedAsync_HappyPath_PostsExpectedPayload()
    {
        var participant = BuildParticipant();
        var platformUserId = Guid.NewGuid();
        InboxWritePayload? captured = null;
        _participants.Setup(p => p.GetByWalletAddressAsync("holder-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(participant.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUserId);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteCredentialDeclinedAsync("holder-wallet", "cred-xyz", "Driving Licence");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(platformUserId);
        captured.Category.Should().Be("Credential");
        captured.Severity.Should().Be("Info");
        captured.Title.Should().Be("Declined credential: Driving Licence");
        captured.CorrelationKey.Should().Be("credential:holder-wallet:cred-xyz:declined");
        captured.DetailHref.Should().Be("/api/v1/wallets/holder-wallet/credentials/cred-xyz");
        captured.IconKey.Should().Be("credential.declined");
    }

    [Fact]
    public async Task WriteCredentialDeletedAsync_UsesWarningSeverityAndListingHref()
    {
        var participant = BuildParticipant();
        InboxWritePayload? captured = null;
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteCredentialDeletedAsync("holder-wallet", "cred-xyz", "Driving Licence");

        captured!.Severity.Should().Be("Warning");
        captured.Title.Should().Be("Deleted credential: Driving Licence");
        // The credential resource is gone — point to the listing instead.
        captured.DetailHref.Should().Be("/api/v1/wallets/holder-wallet/credentials");
        captured.IconKey.Should().Be("credential.deleted");
    }

    [Fact]
    public async Task WritePresentationSubmittedAsync_WithVerifierName_NamesItInTitle()
    {
        var participant = BuildParticipant();
        InboxWritePayload? captured = null;
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WritePresentationSubmittedAsync(
            walletAddress: "holder-wallet",
            credentialId: "cred-xyz",
            credentialType: "Driving Licence",
            presentationRequestId: "req-789",
            verifierIdentity: "Strathcarron Council");

        captured!.Title.Should().Be("Shared Driving Licence with Strathcarron Council");
        captured.CorrelationKey.Should().Be("presentation:holder-wallet:req-789");
        captured.DetailHref.Should().Be("/api/v1/wallets/holder-wallet/credentials/cred-xyz");
        captured.IconKey.Should().Be("credential.presented");
        captured.Summary.Should().Contain("Strathcarron Council");
    }

    [Fact]
    public async Task WritePresentationSubmittedAsync_NoVerifierName_FallsBackToCredentialTypeOnly()
    {
        var participant = BuildParticipant();
        InboxWritePayload? captured = null;
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WritePresentationSubmittedAsync("holder-wallet", "cred-xyz", "Driving Licence", "req-789");

        captured!.Title.Should().Be("Shared credential: Driving Licence");
        captured.Summary.Should().BeNull();
    }

    [Fact]
    public async Task NewMethods_ShortCircuit_OnBlankInputs()
    {
        await _sut.WriteCredentialDeclinedAsync("", "cred", "Type");
        await _sut.WriteCredentialDeletedAsync("wallet", "", "Type");
        await _sut.WritePresentationSubmittedAsync("wallet", "cred", "", "req");

        _participants.Verify(
            p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NewMethods_DeterministicSourceIds_CollapseDuplicates()
    {
        var participant = BuildParticipant();
        var sourceIds = new List<Guid>();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: true));

        await _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");
        await _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1]);
    }

    [Fact]
    public async Task NewMethods_DifferentEvents_HaveDifferentSourceIds()
    {
        var participant = BuildParticipant();
        var sourceIds = new List<Guid>();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        // Same wallet + same credential, three different event types should
        // produce three distinct source ids.
        await _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");
        await _sut.WriteCredentialDeletedAsync("wallet", "cred", "Type");
        await _sut.WritePresentationSubmittedAsync("wallet", "cred", "Type", "req");

        sourceIds.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task NewMethods_InboxThrows_DoNotPropagate()
    {
        var participant = BuildParticipant();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act1 = () => _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");
        var act2 = () => _sut.WriteCredentialDeletedAsync("wallet", "cred", "Type");
        var act3 = () => _sut.WritePresentationSubmittedAsync("wallet", "cred", "Type", "req");

        await act1.Should().NotThrowAsync();
        await act2.Should().NotThrowAsync();
        await act3.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NewMethods_NoParticipant_SkipInbox()
    {
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        await _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");
        await _sut.WriteCredentialDeletedAsync("wallet", "cred", "Type");
        await _sut.WritePresentationSubmittedAsync("wallet", "cred", "Type", "req");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewMethods_DetailHrefAlwaysStartsWithApi_PerInternalInboxValidation()
    {
        var participant = BuildParticipant();
        var captured = new List<InboxWritePayload>();
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteCredentialDeclinedAsync("wallet", "cred", "Type");
        await _sut.WriteCredentialDeletedAsync("wallet", "cred", "Type");
        await _sut.WritePresentationSubmittedAsync("wallet", "cred", "Type", "req");

        captured.Should().AllSatisfy(p => p.DetailHref.Should().StartWith("/api/"));
    }

    private static ParticipantInfo BuildParticipant() =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            DisplayName = "Test",
            Email = "test@example.com",
            Status = "Active",
        };
}
