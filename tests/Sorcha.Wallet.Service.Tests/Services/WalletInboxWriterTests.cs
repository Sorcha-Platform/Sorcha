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
