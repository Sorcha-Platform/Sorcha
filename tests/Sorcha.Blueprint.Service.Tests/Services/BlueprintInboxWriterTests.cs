// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Participant;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BlueprintInboxWriter"/>. Phase 5 follow-up of Feature 118.
/// </summary>
public class BlueprintInboxWriterTests
{
    private readonly Mock<IParticipantServiceClient> _participants = new();
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private readonly BlueprintInboxWriter _sut;

    public BlueprintInboxWriterTests()
    {
        _sut = new BlueprintInboxWriter(_participants.Object, _inbox.Object, NullLogger<BlueprintInboxWriter>.Instance);
    }

    [Fact]
    public async Task WriteActionAvailableAsync_HappyPath_PostsExpectedPayload()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        var platformUserId = Guid.NewGuid();
        InboxWritePayload? captured = null;

        _participants.Setup(p => p.GetByWalletAddressAsync("wallet-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(participant.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUserId);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X", "Sign the form");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(platformUserId);
        captured.Category.Should().Be("Action");
        captured.Severity.Should().Be("ActionRequired");
        captured.CorrelationKey.Should().Be("action:instance-A:action-X");
        captured.DetailHref.Should().Be("/api/instances/instance-A/actions/action-X");
        captured.Title.Should().Be("Sign the form");
    }

    [Fact]
    public async Task WriteActionAvailableAsync_NoParticipant_SkipsInbox()
    {
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        await _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteActionAvailableAsync_NoPlatformUserResolution_SkipsInbox()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteActionAvailableAsync_DeterministicSourceEventId_ReusesAcrossCalls()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        var platformUserId = Guid.NewGuid();
        var sourceIds = new List<Guid>();

        _participants.Setup(p => p.GetByWalletAddressAsync("wallet-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUserId);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X");
        await _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1], "duplicate notifications must collapse to the same idempotency key");
    }

    [Fact]
    public async Task WriteActionAvailableAsync_InboxThrows_DoesNotPropagate()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act = () => _sut.WriteActionAvailableAsync("wallet-1", "instance-A", "action-X");

        await act.Should().NotThrowAsync(
            "inbox-write failures must never surface to the caller — SignalR delivery must be unaffected");
    }

    private static ParticipantInfo BuildParticipant(Guid userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationId = Guid.NewGuid(),
            DisplayName = "Test",
            Email = "test@example.com",
            Status = "Active",
        };
}
