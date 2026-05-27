// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US5 PR3 — unit coverage for <see cref="EfCoreCitizenPresentationStore"/>
/// (and the in-memory fallback) exercised against the EF Core InMemory provider via
/// the <see cref="TestCitizenWalletDbContext"/> pattern.
/// </summary>
public class CitizenPresentationStoreTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static TestCitizenWalletDbContext NewDb(
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"presentation-store-{testName}-{Guid.NewGuid():N}")
            .Options;
        return new TestCitizenWalletDbContext(options);
    }

    private static PresentationLogEntry Entry(
        Guid? id = null,
        DateTimeOffset? presentedAt = null,
        PresentationLogOutcome outcome = PresentationLogOutcome.Presented,
        params string[] claims) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CredentialId = Guid.NewGuid(),
        VerifierLabel = "Strathcarron Council",
        VerifierDid = null,
        DisclosedClaims = claims.Length == 0 ? ["givenName", "familyName"] : claims,
        PresentedAt = presentedAt ?? DateTimeOffset.UtcNow,
        Outcome = outcome
    };

    [Fact]
    public async Task UpsertAsync_SameEntryTwice_IsIdempotentOnCompositeKey()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var entry = Entry();

        await store.UpsertAsync(UserA, entry);
        await store.UpsertAsync(UserA, entry);

        var listed = await store.ListAsync(UserA);
        listed.Should().HaveCount(1);
        listed[0].Id.Should().Be(entry.Id);
    }

    [Fact]
    public async Task UpsertAsync_PreservesReportedAtOnReReport()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var entry = Entry();

        await store.UpsertAsync(UserA, entry);
        var firstReportedAt = (await db.CitizenPresentationRecords.SingleAsync()).ReportedAt;

        await Task.Delay(5);
        await store.UpsertAsync(UserA, entry);
        var secondReportedAt = (await db.CitizenPresentationRecords.SingleAsync()).ReportedAt;

        secondReportedAt.Should().Be(firstReportedAt);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var now = DateTimeOffset.UtcNow;

        var older = Entry(presentedAt: now.AddMinutes(-10));
        var newer = Entry(presentedAt: now);
        await store.UpsertAsync(UserA, older);
        await store.UpsertAsync(UserA, newer);

        var listed = await store.ListAsync(UserA);
        listed.Select(e => e.Id).Should().ContainInOrder(newer.Id, older.Id);
    }

    [Fact]
    public async Task ListAsync_ScopesToOwningCitizen()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        await store.UpsertAsync(UserA, Entry());
        await store.UpsertAsync(UserB, Entry());

        (await store.ListAsync(UserA)).Should().HaveCount(1);
        (await store.ListAsync(UserB)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_EmptyHistory_ReturnsEmptyList()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);

        (await store.ListAsync(UserA)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_OwnRow_RemovesAndReturnsTrue()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var entry = Entry();
        await store.UpsertAsync(UserA, entry);

        var removed = await store.DeleteAsync(UserA, entry.Id);

        removed.Should().BeTrue();
        (await store.ListAsync(UserA)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_CrossUser_IsNoOpAndLeavesRowIntact()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var entry = Entry();
        await store.UpsertAsync(UserA, entry);

        // UserB attempts to delete UserA's entry id.
        var removed = await store.DeleteAsync(UserB, entry.Id);

        removed.Should().BeFalse();
        (await store.ListAsync(UserA)).Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);

        (await store.DeleteAsync(UserA, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_PreservesWireFieldsAndDropsRegisterCorrelation()
    {
        using var db = NewDb();
        var store = new EfCoreCitizenPresentationStore(db);
        var entry = Entry(outcome: PresentationLogOutcome.Acknowledged, claims: ["dateOfBirth"]);

        await store.UpsertAsync(UserA, entry);
        var round = (await store.ListAsync(UserA)).Single();

        round.Id.Should().Be(entry.Id);
        round.CredentialId.Should().Be(entry.CredentialId);
        round.VerifierLabel.Should().Be(entry.VerifierLabel);
        round.DisclosedClaims.Should().BeEquivalentTo(entry.DisclosedClaims);
        round.Outcome.Should().Be(PresentationLogOutcome.Acknowledged);
        round.RegisterId.Should().BeNull();
        round.ActionTxId.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryStore_HasSameSemantics()
    {
        var store = new InMemoryCitizenPresentationStore();
        var now = DateTimeOffset.UtcNow;
        var older = Entry(presentedAt: now.AddMinutes(-5));
        var newer = Entry(presentedAt: now);

        await store.UpsertAsync(UserA, older);
        await store.UpsertAsync(UserA, newer);
        await store.UpsertAsync(UserA, newer); // idempotent re-report
        await store.UpsertAsync(UserB, Entry());

        var listed = await store.ListAsync(UserA);
        listed.Select(e => e.Id).Should().ContainInOrder(newer.Id, older.Id);

        (await store.DeleteAsync(UserB, older.Id)).Should().BeFalse(); // cross-user
        (await store.DeleteAsync(UserA, older.Id)).Should().BeTrue();
        (await store.ListAsync(UserA)).Should().ContainSingle(e => e.Id == newer.Id);
    }
}
