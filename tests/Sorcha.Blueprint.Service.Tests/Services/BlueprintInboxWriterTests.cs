// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BlueprintInboxWriter"/>. Phase 5 follow-up of Feature 118.
/// </summary>
public class BlueprintInboxWriterTests
{
    private readonly Mock<IParticipantServiceClient> _participants = new();
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private readonly Mock<IWalletServiceClient> _wallets = new();
    private readonly BlueprintInboxWriter _sut;

    public BlueprintInboxWriterTests()
    {
        _sut = new BlueprintInboxWriter(
            _participants.Object, _inbox.Object, _wallets.Object, NullLogger<BlueprintInboxWriter>.Instance);
    }

    private static WalletInfo Wallet(string owner) => new()
    {
        Address = "citizen-wallet",
        Name = "Citizen Wallet",
        PublicKey = "pk",
        Algorithm = "ED25519",
        Status = "Active",
        Owner = owner,
        Tenant = "default",
    };

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

    // ---- Feature 183 (US2) — WriteDecisionAsync (reject-visibility decision notice) ----

    [Fact]
    public async Task WriteDecisionAsync_HappyPath_PostsWorkflowEntryCarryingTheReason()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        var platformUserId = Guid.NewGuid();
        InboxWritePayload? captured = null;

        _participants.Setup(p => p.GetByWalletAddressAsync("citizen-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(participant.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformUserId);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDecisionAsync(
            "citizen-wallet", "instance-A", "2",
            title: "AIAS could not assure your identity",
            reason: "AIAS needs a verified email before it can assure you.",
            severity: "Warning");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(platformUserId);
        captured.Category.Should().Be("Workflow");
        captured.Severity.Should().Be("Warning");
        captured.Title.Should().Be("AIAS could not assure your identity");
        captured.Summary.Should().Be("AIAS needs a verified email before it can assure you.");
        captured.CorrelationKey.Should().Be("decision:instance-A:2");
        captured.DetailHref.Should().Be("/api/instances/instance-A");
    }

    [Fact]
    public async Task WriteDecisionAsync_NoParticipantNoWallet_SkipsInbox()
    {
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);
        _wallets.Setup(w => w.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        await _sut.WriteDecisionAsync("citizen-wallet", "instance-A", "2", "Title", "reason", "Warning");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteDecisionAsync_LateBoundCitizen_ResolvesViaSendingWalletOwner()
    {
        // A late-bound open participant (citizen) has NO participant-registry record — so we must
        // resolve the recipient from the sending wallet's owner (a consumer wallet's Owner IS the
        // PlatformUserId). GetWalletAsync is inherently local-only, so this also skips gracefully
        // (no misfire) when the citizen's wallet is hosted on another node.
        var platformUserId = Guid.NewGuid();
        InboxWritePayload? captured = null;

        _participants.Setup(p => p.GetByWalletAddressAsync("citizen-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);
        _wallets.Setup(w => w.GetWalletAsync("citizen-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wallet(owner: platformUserId.ToString()));
        // Owner is already a PlatformUserId, so the UserIdentity resolution misses — and we then
        // CONFIRM it is a platform user before using it (#1506). The miss alone is not evidence.
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(platformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _inbox.Setup(i => i.PlatformUserExistsAsync(platformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDecisionAsync(
            "citizen-wallet", "instance-A", "2",
            title: "AIAS could not assure your identity",
            reason: "AIAS needs a verified email before it can assure you.",
            severity: "Warning");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(platformUserId);
        captured.Category.Should().Be("Workflow");
        captured.Summary.Should().Be("AIAS needs a verified email before it can assure you.");
    }

    /// <summary>
    /// Issue #1506 — a wallet owner that is neither a resolvable UserIdentity nor a known
    /// PlatformUser must produce NO inbox write.
    /// </summary>
    /// <remarks>
    /// The writer used to end in <c>?? ownerId</c>: if the UserIdentity lookup missed, it assumed
    /// the GUID must therefore be a PlatformUserId. Every id here is a GUID, so a wrong one is
    /// indistinguishable by shape and only the foreign key noticed — as a 500 on a best-effort
    /// notification write, which on n1 tripped a circuit breaker that then blocked credential
    /// issuance outright. A missing bell is strictly better than that.
    /// </remarks>
    [Fact]
    public async Task WriteDecisionAsync_OwnerIsNeitherIdentityNorPlatformUser_SkipsInbox()
    {
        var strayId = Guid.NewGuid();

        _participants.Setup(p => p.GetByWalletAddressAsync("org-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);
        _wallets.Setup(w => w.GetWalletAsync("org-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wallet(owner: strayId.ToString()));
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(strayId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _inbox.Setup(i => i.PlatformUserExistsAsync(strayId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _sut.WriteDecisionAsync("org-wallet", "instance-A", "2", "Title", "reason", "Warning");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The existence check fails CLOSED: if it cannot be made, the id stays unverified and nothing
    /// is written. "I could not check" is not "it exists" — treating it as existence is what put an
    /// unverified id in front of the foreign key.
    /// </summary>
    [Fact]
    public async Task WriteDecisionAsync_ExistenceCheckUnavailable_SkipsInbox()
    {
        var unknownId = Guid.NewGuid();

        _participants.Setup(p => p.GetByWalletAddressAsync("org-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);
        _wallets.Setup(w => w.GetWalletAsync("org-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wallet(owner: unknownId.ToString()));
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _inbox.Setup(i => i.PlatformUserExistsAsync(unknownId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tenant unreachable"));

        var act = async () => await _sut.WriteDecisionAsync(
            "org-wallet", "instance-A", "2", "Title", "reason", "Warning");

        // Best-effort by contract: it must not throw INTO the caller either.
        await act.Should().NotThrowAsync();
        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WriteDecisionAsync_NoPlatformUserResolution_SkipsInbox()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await _sut.WriteDecisionAsync("citizen-wallet", "instance-A", "2", "Title", "reason", "Warning");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteDecisionAsync_EmptyRecipientWallet_SkipsInbox()
    {
        await _sut.WriteDecisionAsync("", "instance-A", "2", "Title", "reason", "Warning");

        _participants.Verify(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteDecisionAsync_DeterministicSourceEventId_ReusesAcrossCalls()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        var sourceIds = new List<Guid>();

        _participants.Setup(p => p.GetByWalletAddressAsync("citizen-wallet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteDecisionAsync("citizen-wallet", "instance-A", "2", "Title", "reason", "Warning");
        await _sut.WriteDecisionAsync("citizen-wallet", "instance-A", "2", "Title", "reason", "Warning");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1], "duplicate decision notices must collapse to the same idempotency key");
    }

    [Fact]
    public async Task WriteDecisionAsync_InboxThrows_DoesNotPropagate()
    {
        var participant = BuildParticipant(Guid.NewGuid());
        _participants.Setup(p => p.GetByWalletAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participant);
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act = () => _sut.WriteDecisionAsync("citizen-wallet", "instance-A", "2", "Title", "reason", "Warning");

        await act.Should().NotThrowAsync(
            "a decision-notice write failure must never affect sealing/routing or surface to the caller");
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
