// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WalletWorkflowInboxWriter"/> — Phase 2 of the
/// Snackbar retirement plan. Companion to <see cref="WalletInboxWriterTests"/>;
/// covers the four wallet-lifecycle events that currently surface as toasts:
/// created, recovered, deleted, and address-registered.
/// </summary>
public class WalletWorkflowInboxWriterTests
{
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private readonly WalletWorkflowInboxWriter _sut;
    private readonly Guid _ownerUserIdentityId = Guid.NewGuid();
    private readonly Guid _platformUserId = Guid.NewGuid();

    public WalletWorkflowInboxWriterTests()
    {
        _sut = new WalletWorkflowInboxWriter(_inbox.Object, NullLogger<WalletWorkflowInboxWriter>.Instance);

        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(_ownerUserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_platformUserId);
    }

    [Fact]
    public async Task WriteWalletCreatedAsync_HappyPath_PostsExpectedPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "My Wallet", _ownerUserIdentityId);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_platformUserId);
        captured.Category.Should().Be("Workflow");
        captured.Severity.Should().Be("Info");
        captured.CorrelationKey.Should().Be("wallet:WALLET-ADDR");
        captured.DetailHref.Should().Be("/api/v1/wallets/WALLET-ADDR");
        captured.Title.Should().Be("Wallet created: My Wallet");
        captured.IconKey.Should().Be("wallet.created");
    }

    [Fact]
    public async Task WriteWalletRecoveredAsync_HappyPath_PostsExpectedPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletRecoveredAsync("WALLET-ADDR", "My Wallet", _ownerUserIdentityId);

        captured!.Severity.Should().Be("Info");
        captured.Title.Should().Be("Wallet recovered: My Wallet");
        captured.CorrelationKey.Should().Be("wallet:WALLET-ADDR:recovered");
        captured.IconKey.Should().Be("wallet.recovered");
    }

    [Fact]
    public async Task WriteWalletDeletedAsync_PostsWarningSeverity()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletDeletedAsync("WALLET-ADDR", "Old Wallet", _ownerUserIdentityId);

        // Destructive action gets a louder treatment in the bell drawer.
        captured!.Severity.Should().Be("Warning");
        captured.Title.Should().Be("Wallet deleted: Old Wallet");
        // The wallet's resource URL would 404 after delete; direct to listing.
        captured.DetailHref.Should().Be("/api/v1/wallets");
        captured.IconKey.Should().Be("wallet.deleted");
    }

    [Fact]
    public async Task WriteAddressRegisteredAsync_HappyPath_PostsExpectedPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteAddressRegisteredAsync("PARENT-ADDR", "DERIVED-ADDR", _ownerUserIdentityId);

        captured!.CorrelationKey.Should().Be("wallet:PARENT-ADDR:address-registered:DERIVED-ADDR");
        captured.DetailHref.Should().Be("/api/v1/wallets/PARENT-ADDR/addresses");
        captured.Title.Should().Be("New derived address");
        captured.Summary.Should().Contain("DERIVED-ADDR");
        captured.IconKey.Should().Be("wallet.address.registered");
    }

    [Fact]
    public async Task BlankWalletAddress_ShortCircuits_WithNoCalls()
    {
        await _sut.WriteWalletCreatedAsync("", "Test", _ownerUserIdentityId);
        await _sut.WriteWalletRecoveredAsync(" ", "Test", _ownerUserIdentityId);
        await _sut.WriteWalletDeletedAsync(null!, "Test", _ownerUserIdentityId);
        await _sut.WriteAddressRegisteredAsync("", "DERIVED", _ownerUserIdentityId);

        _inbox.Verify(
            i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmptyOwnerUserIdentityId_ShortCircuits_WithoutResolving()
    {
        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "Test", Guid.Empty);

        _inbox.Verify(
            i => i.ResolvePlatformUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UnresolvedPlatformUserId_SkipsWrite()
    {
        var unknownOwner = Guid.NewGuid();
        _inbox.Setup(i => i.ResolvePlatformUserIdAsync(unknownOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "Test", unknownOwner);

        _inbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeterministicSourceEventId_CollapsesDuplicates()
    {
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: true));

        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "Same", _ownerUserIdentityId);
        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "Same", _ownerUserIdentityId);

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1]);
    }

    [Fact]
    public async Task DifferentEventsForSameWallet_HaveDifferentSourceIds()
    {
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => sourceIds.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "X", _ownerUserIdentityId);
        await _sut.WriteWalletRecoveredAsync("WALLET-ADDR", "X", _ownerUserIdentityId);
        await _sut.WriteWalletDeletedAsync("WALLET-ADDR", "X", _ownerUserIdentityId);

        // Three distinct events on the same wallet → three distinct source ids.
        sourceIds.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var act = () => _sut.WriteWalletCreatedAsync("WALLET-ADDR", "Test", _ownerUserIdentityId);

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block wallet operations");
    }

    [Fact]
    public async Task EmptyName_StillProducesUsableTitle()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletCreatedAsync("WALLET-ADDR", "", _ownerUserIdentityId);

        captured!.Title.Should().Be("Wallet created");
    }

    [Fact]
    public async Task DetailHref_AlwaysStartsWithApi_PerInternalInboxValidation()
    {
        var captured = new List<InboxWritePayload>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured.Add(p))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _sut.WriteWalletCreatedAsync("W", "N", _ownerUserIdentityId);
        await _sut.WriteWalletRecoveredAsync("W", "N", _ownerUserIdentityId);
        await _sut.WriteWalletDeletedAsync("W", "N", _ownerUserIdentityId);
        await _sut.WriteAddressRegisteredAsync("W", "D", _ownerUserIdentityId);

        captured.Should().AllSatisfy(p => p.DetailHref.Should().StartWith("/api/"));
    }
}
