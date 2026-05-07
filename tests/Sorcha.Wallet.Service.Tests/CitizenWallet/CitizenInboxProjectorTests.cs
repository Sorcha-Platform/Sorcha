// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Hubs;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US4 — unit coverage for <see cref="CitizenInboxProjector"/>.
/// Verifies the projection rules, hub-emit shape, and Seq monotonicity. The
/// org-credential pipeline must be unaffected when the recipient is not a
/// citizen-PWA holder.
/// </summary>
public class CitizenInboxProjectorTests
{
    private static (
        CitizenInboxProjector projector,
        TestCitizenWalletDbContext db,
        Mock<IHolderAddressLookup> lookup,
        Mock<IHubContext<WalletHub, IWalletHubClient>> hub,
        Mock<IWalletHubClient> hubClient,
        List<(string group, string credentialId)> emissions
    ) CreateSut([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"projector-{testName}-{Guid.NewGuid():N}")
            .Options;
        var db = new TestCitizenWalletDbContext(options);

        var lookup = new Mock<IHolderAddressLookup>();
        var hub = new Mock<IHubContext<WalletHub, IWalletHubClient>>();
        var hubClients = new Mock<IHubClients<IWalletHubClient>>();
        var hubClient = new Mock<IWalletHubClient>();
        var emissions = new List<(string, string)>();

        // Capture the group name + credentialId pair every time CredentialAvailable is invoked.
        hub.SetupGet(h => h.Clients).Returns(hubClients.Object);
        hubClients.Setup(c => c.Group(It.IsAny<string>()))
            .Returns((string group) =>
            {
                hubClient.Setup(c => c.CredentialAvailable(It.IsAny<string>()))
                    .Callback<string>(credentialId => emissions.Add((group, credentialId)))
                    .Returns(Task.CompletedTask);
                return hubClient.Object;
            });

        var projector = new CitizenInboxProjector(
            db, lookup.Object, hub.Object, NullLogger<CitizenInboxProjector>.Instance);

        return (projector, db, lookup, hub, hubClient, emissions);
    }

    private static CredentialEntity NewCredential(string walletAddress, CredentialStatus status = CredentialStatus.PendingAcceptance) =>
        new()
        {
            Id = $"urn:credential:Test:{Guid.NewGuid():N}",
            Type = "TestCredential/v1",
            IssuerDid = "did:sorcha:org:ws1qissuer",
            SubjectDid = walletAddress,
            ClaimsJson = "{}",
            RawToken = "header.payload.sig",
            Status = status,
            WalletAddress = walletAddress,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task OnCredentialAddedAsync_CitizenRecipient_AppendsEventAndEmits()
    {
        var (projector, db, lookup, _, _, emissions) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("ws1qcitizen");
        lookup.Setup(l => l.ResolvePlatformUserIdAsync("ws1qcitizen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pid);

        await projector.OnCredentialAddedAsync(credential);

        var rows = await db.CitizenCredentialEventLog.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].PlatformUserId.Should().Be(pid);
        rows[0].Kind.Should().Be(0); // Added
        rows[0].Seq.Should().Be(1);
        rows[0].CredentialId.Should().Be(credential.Id);

        emissions.Should().ContainSingle();
        emissions[0].group.Should().Be(WalletHub.GroupNameFor(pid));
        emissions[0].credentialId.Should().Be(credential.Id);
    }

    [Fact]
    public async Task OnCredentialAddedAsync_OrgRecipient_NoOp()
    {
        var (projector, db, lookup, _, _, emissions) = CreateSut();
        var credential = NewCredential("ws1qorg");
        lookup.Setup(l => l.ResolvePlatformUserIdAsync("ws1qorg", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await projector.OnCredentialAddedAsync(credential);

        (await db.CitizenCredentialEventLog.CountAsync()).Should().Be(0);
        emissions.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCredentialAddedAsync_SecondCredential_AdvancesSeq()
    {
        var (projector, db, lookup, _, _, _) = CreateSut();
        var pid = Guid.NewGuid();
        lookup.Setup(l => l.ResolvePlatformUserIdAsync("ws1qcitizen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pid);

        await projector.OnCredentialAddedAsync(NewCredential("ws1qcitizen"));
        await projector.OnCredentialAddedAsync(NewCredential("ws1qcitizen"));

        var seqs = await db.CitizenCredentialEventLog
            .Where(e => e.PlatformUserId == pid)
            .OrderBy(e => e.Seq)
            .Select(e => e.Seq)
            .ToListAsync();
        seqs.Should().Equal(1L, 2L);
    }

    [Fact]
    public async Task OnCredentialStatusChangedAsync_RevokedTransition_EmitsRevokedEvent()
    {
        var (projector, db, lookup, _, _, emissions) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("ws1qcitizen", CredentialStatus.Revoked);
        lookup.Setup(l => l.ResolvePlatformUserIdAsync("ws1qcitizen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pid);

        await projector.OnCredentialStatusChangedAsync(credential, CredentialStatus.Active);

        var row = await db.CitizenCredentialEventLog.SingleAsync();
        row.Kind.Should().Be(1); // Revoked
        emissions.Should().ContainSingle();
    }

    [Fact]
    public async Task OnCredentialStatusChangedAsync_DeclinedTransition_EmitsRevokedEvent()
    {
        var (projector, db, lookup, _, _, emissions) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("ws1qcitizen", CredentialStatus.Declined);
        lookup.Setup(l => l.ResolvePlatformUserIdAsync("ws1qcitizen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pid);

        await projector.OnCredentialStatusChangedAsync(credential, CredentialStatus.PendingAcceptance);

        var row = await db.CitizenCredentialEventLog.SingleAsync();
        row.Kind.Should().Be(1); // Revoked maps both Revoked and Declined statuses
        emissions.Should().ContainSingle();
    }

    [Fact]
    public async Task OnCredentialStatusChangedAsync_ActiveTransition_NoOp()
    {
        // PendingAcceptance → Active is a successful holder accept. The wallet
        // already knows about the credential (we sent it as Added when it landed
        // in PendingAcceptance), so re-emitting on accept would be noise.
        var (projector, db, lookup, _, _, emissions) = CreateSut();
        var credential = NewCredential("ws1qcitizen", CredentialStatus.Active);
        lookup.Setup(l => l.ResolvePlatformUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        await projector.OnCredentialStatusChangedAsync(credential, CredentialStatus.PendingAcceptance);

        (await db.CitizenCredentialEventLog.CountAsync()).Should().Be(0);
        emissions.Should().BeEmpty();
    }
}
