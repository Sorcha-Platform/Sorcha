// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US4 — unit coverage for <see cref="EfCoreCitizenCredentialEventStream"/>.
/// </summary>
public class EfCoreCitizenCredentialEventStreamTests
{
    private static (EfCoreCitizenCredentialEventStream stream, TestCitizenWalletDbContext db) CreateSut(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"event-stream-{testName}-{Guid.NewGuid():N}")
            .Options;
        var db = new TestCitizenWalletDbContext(options);
        var stream = new EfCoreCitizenCredentialEventStream(
            db, NullLogger<EfCoreCitizenCredentialEventStream>.Instance);
        return (stream, db);
    }

    private static async Task SeedAsync(
        TestCitizenWalletDbContext db,
        Guid platformUserId,
        params (long Seq, int Kind, string CredentialId, CredentialEntity? Credential)[] events)
    {
        foreach (var e in events)
        {
            db.CitizenCredentialEventLog.Add(new CitizenCredentialEventLog
            {
                PlatformUserId = platformUserId,
                Seq = e.Seq,
                Kind = e.Kind,
                CredentialId = e.CredentialId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            if (e.Credential is not null)
            {
                db.Credentials.Add(e.Credential);
            }
        }
        await db.SaveChangesAsync();
    }

    private static CredentialEntity NewCredential(
        string credentialId,
        string walletAddress,
        CredentialStatus status = CredentialStatus.PendingAcceptance) =>
        new()
        {
            Id = credentialId,
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
    public async Task ReadAsync_AfterSeq_ReturnsOnlyNewerEventsOrdered()
    {
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var c1 = NewCredential("urn:credential:1", "ws1qcitizen");
        var c2 = NewCredential("urn:credential:2", "ws1qcitizen");
        var c3 = NewCredential("urn:credential:3", "ws1qcitizen");
        await SeedAsync(db, pid,
            (1L, 0, c1.Id, c1),
            (2L, 0, c2.Id, c2),
            (3L, 0, c3.Id, c3));

        var events = await stream.ReadAsync(pid, afterSeq: 1L);

        events.Should().HaveCount(2);
        events.Select(e => e.Seq).Should().Equal(2L, 3L);
    }

    [Fact]
    public async Task ReadAsync_DifferentPlatformUser_DoesNotLeakEvents()
    {
        var (stream, db) = CreateSut();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var aliceCredential = NewCredential("urn:credential:alice", "ws1qalice");
        var bobCredential = NewCredential("urn:credential:bob", "ws1qbob");
        await SeedAsync(db, alice, (1L, 0, aliceCredential.Id, aliceCredential));
        await SeedAsync(db, bob, (1L, 0, bobCredential.Id, bobCredential));

        var aliceEvents = await stream.ReadAsync(alice, afterSeq: 0L);
        var bobEvents = await stream.ReadAsync(bob, afterSeq: 0L);

        aliceEvents.Should().ContainSingle().Which.Payload.Should().BeOfType<CachedCredentialPayload>()
            .Which.Id.Should().Be("urn:credential:alice");
        bobEvents.Should().ContainSingle().Which.Payload.Should().BeOfType<CachedCredentialPayload>()
            .Which.Id.Should().Be("urn:credential:bob");
    }

    [Fact]
    public async Task ReadAsync_AddedKind_ProducesCachedCredentialPayload()
    {
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("urn:credential:1", "ws1qcitizen");
        credential.IssuerDid = "did:sorcha:org:ws1qriverside";
        credential.IssuerOrgName = "Riverside Council";
        await SeedAsync(db, pid, (1L, 0, credential.Id, credential));

        var events = await stream.ReadAsync(pid, afterSeq: 0L);

        var evt = events.Should().ContainSingle().Subject;
        evt.Kind.Should().Be(CitizenCredentialEventKind.Added);
        var payload = evt.Payload.Should().BeOfType<CachedCredentialPayload>().Subject;
        payload.Id.Should().Be(credential.Id);
        payload.Vct.Should().Be(credential.Type);
        payload.IssuerDid.Should().Be(credential.IssuerDid);
        payload.Jwt.Should().Be(credential.RawToken);
    }

    [Fact]
    public async Task ReadAsync_AddedKind_WithDisplayConfigJson_PopulatesDisplayMetaCredentialName()
    {
        // Credential VCT decoupling (Task 4): the authored displayName (Task 3.5) must
        // survive onto the sync-out payload so the PWA card shows it instead of a
        // humanized vct.
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("urn:credential:1", "ws1qcitizen");
        credential.DisplayConfigJson = """{"credentialName":"Assured Identity"}""";
        await SeedAsync(db, pid, (1L, 0, credential.Id, credential));

        var events = await stream.ReadAsync(pid, afterSeq: 0L);

        var evt = events.Should().ContainSingle().Subject;
        var payload = evt.Payload.Should().BeOfType<CachedCredentialPayload>().Subject;
        payload.DisplayMeta.Should().NotBeNull();
        payload.DisplayMeta!["credentialName"]!.GetValue<string>().Should().Be("Assured Identity");
    }

    [Fact]
    public async Task ReadAsync_AddedKind_WithoutDisplayConfigJson_LeavesDisplayMetaNull()
    {
        // Legacy credentials (issued before Task 3.5, or with no authored display name)
        // must keep the PWA's Humanize(vct) fallback rather than surfacing a synthetic
        // DisplayMeta.
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("urn:credential:1", "ws1qcitizen");
        credential.DisplayConfigJson = null;
        await SeedAsync(db, pid, (1L, 0, credential.Id, credential));

        var events = await stream.ReadAsync(pid, afterSeq: 0L);

        var evt = events.Should().ContainSingle().Subject;
        var payload = evt.Payload.Should().BeOfType<CachedCredentialPayload>().Subject;
        payload.DisplayMeta.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_RevokedKind_ProducesRevokedCredentialEntry()
    {
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var credential = NewCredential("urn:credential:1", "ws1qcitizen", CredentialStatus.Revoked);
        await SeedAsync(db, pid, (1L, 1, credential.Id, credential));

        var events = await stream.ReadAsync(pid, afterSeq: 0L);

        var evt = events.Should().ContainSingle().Subject;
        evt.Kind.Should().Be(CitizenCredentialEventKind.Revoked);
        var payload = evt.Payload.Should().BeOfType<RevokedCredentialEntry>().Subject;
        payload.Id.Should().Be(credential.Id);
        payload.Reason.Should().Be(CredentialRevocationReason.Withdrawn);
    }

    [Fact]
    public async Task ReadAsync_MissingCredential_SkipsEventGracefully()
    {
        // Defensive: an event log row that references a deleted credential should
        // not crash the sync surface.
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        await SeedAsync(db, pid, (1L, 0, "urn:credential:vanished", null));

        var events = await stream.ReadAsync(pid, afterSeq: 0L);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighestSeqAsync_NoEvents_ReturnsZero()
    {
        var (stream, _) = CreateSut();

        var max = await stream.GetHighestSeqAsync(Guid.NewGuid());

        max.Should().Be(0L);
    }

    [Fact]
    public async Task GetHighestSeqAsync_SeededEvents_ReturnsMaxSeq()
    {
        var (stream, db) = CreateSut();
        var pid = Guid.NewGuid();
        var c1 = NewCredential("urn:credential:1", "ws1qcitizen");
        var c2 = NewCredential("urn:credential:2", "ws1qcitizen");
        await SeedAsync(db, pid, (3L, 0, c1.Id, c1), (7L, 0, c2.Id, c2));

        var max = await stream.GetHighestSeqAsync(pid);

        max.Should().Be(7L);
    }
}
