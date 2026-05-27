// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for the citizen presentation activity log (Feature 114 US5).
/// Exercises the in-memory store's append / newest-first ordering / delete
/// semantics — the same contract the IndexedDB-backed store fulfils.
/// </summary>
public sealed class PresentationLogTests
{
    private static PresentationLogEntry Entry(string vct, DateTimeOffset at, params string[] claims) =>
        new(
            Id: Guid.NewGuid(),
            PresentedAt: at,
            CredentialType: vct,
            CredentialLabel: null,
            VerifierLabel: "Strathcarron Council",
            DisclosedClaims: claims,
            Outcome: PresentationLogOutcome.Sent);

    [Fact]
    public async Task Append_Then_List_ReturnsNewestFirst()
    {
        var log = new InMemoryPresentationLog();
        var older = Entry("AssuredIdentityCredential/v1", DateTimeOffset.UtcNow.AddMinutes(-10), "givenName");
        var newer = Entry("BlueBadgeCredential/v1", DateTimeOffset.UtcNow, "familyName", "dateOfBirth");

        await log.AppendAsync(older);
        await log.AppendAsync(newer);

        var list = await log.ListAsync();

        list.Should().HaveCount(2);
        list[0].Id.Should().Be(newer.Id, "entries list newest-first");
        list[1].Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheTargetEntry()
    {
        var log = new InMemoryPresentationLog();
        var keep = Entry("A/v1", DateTimeOffset.UtcNow.AddMinutes(-5), "x");
        var drop = Entry("B/v1", DateTimeOffset.UtcNow, "y");
        await log.AppendAsync(keep);
        await log.AppendAsync(drop);

        await log.DeleteAsync(drop.Id);

        var list = await log.ListAsync();
        list.Should().ContainSingle().Which.Id.Should().Be(keep.Id);
    }

    [Fact]
    public async Task Delete_UnknownId_IsNoOp()
    {
        var log = new InMemoryPresentationLog();
        await log.AppendAsync(Entry("A/v1", DateTimeOffset.UtcNow, "x"));

        await log.DeleteAsync(Guid.NewGuid());

        (await log.ListAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Clear_EmptiesTheLog()
    {
        var log = new InMemoryPresentationLog();
        await log.AppendAsync(Entry("A/v1", DateTimeOffset.UtcNow, "x"));
        await log.AppendAsync(Entry("B/v1", DateTimeOffset.UtcNow, "y"));

        await log.ClearAsync();

        (await log.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Entry_DefaultsToNotSyncedToServer()
    {
        var log = new InMemoryPresentationLog();
        var entry = Entry("A/v1", DateTimeOffset.UtcNow, "x");
        await log.AppendAsync(entry);

        var stored = (await log.ListAsync())[0];

        stored.SyncedToServer.Should().BeFalse("server forwarding is a follow-up PR");
    }
}
