// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;
using FluentAssertions;
using Sorcha.CitizenWallet.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="CitizenSyncService"/> (Feature 114, T103/T104). Covers
/// sync-token round-trip, first-sync, incremental sync delta projection, replay
/// to snapshot, and the 410-stale-cursor recovery path.
/// </summary>
public sealed class CitizenSyncServiceTests
{
    private const string HolderKeyId = "abcdef0123456789abcdef0123456789abcdef0123";
    private static readonly Guid PlatformUserId = Guid.Parse("d8b04e5a-0000-0000-0000-000000000001");

    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
    private readonly StubEventStream _events = new();
    private readonly CitizenSyncService _sut;

    public CitizenSyncServiceTests()
    {
        var jwt = new JwtSettings { SigningKey = "unit-test-signing-key-not-for-production-use" };
        _sut = new CitizenSyncService(_events, jwt, _clock);
    }

    [Fact]
    public async Task ComposeDeltaAsync_FirstSync_ReturnsEmptyDeltaAndCursor()
    {
        var response = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, sinceToken: null);

        response.Should().NotBeNull();
        response!.SyncToken.Should().NotBeNullOrEmpty();
        response.Credentials.Added.Should().BeEmpty();
        response.Credentials.Revoked.Should().BeEmpty();
        response.Credentials.Replaced.Should().BeEmpty();
    }

    [Fact]
    public async Task ComposeDeltaAsync_IncrementalSync_ProjectsAddedRevokedReplaced()
    {
        var addedId = $"urn:credential:test:{Guid.NewGuid():N}";
        var revokedId = $"urn:credential:test:{Guid.NewGuid():N}";
        var replacedOldId = $"urn:credential:test:{Guid.NewGuid():N}";
        var replacedNewId = $"urn:credential:test:{Guid.NewGuid():N}";

        _events.Append(1, CitizenCredentialEventKind.Added, new CachedCredentialPayload
        {
            Id = addedId,
            Vct = "https://sorcha.dev/vc/test/v1",
            Jwt = "eyJ...",
            IssuerDid = "did:sorcha:org:test",
            IssuedAt = _clock.GetUtcNow(),
            StatusListUri = "https://verify.test/status/A.statuslist+jwt",
        });
        _events.Append(2, CitizenCredentialEventKind.Revoked, new RevokedCredentialEntry
        {
            Id = revokedId,
            Reason = CredentialRevocationReason.Compromised,
            RevokedAt = _clock.GetUtcNow(),
        });
        _events.Append(3, CitizenCredentialEventKind.Replaced, new ReplacedCredentialEntry
        {
            OldId = replacedOldId,
            NewId = replacedNewId,
            Jwt = "eyJ...new",
            IssuedAt = _clock.GetUtcNow(),
        });

        var response = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, sinceToken: null);

        response!.Credentials.Added.Should().ContainSingle(p => p.Id == addedId);
        response.Credentials.Revoked.Should().ContainSingle(p => p.Id == revokedId);
        response.Credentials.Replaced.Should().ContainSingle(p => p.OldId == replacedOldId && p.NewId == replacedNewId);
        response.StatusListsToRefresh.Should().ContainSingle(u => u.Contains("A.statuslist"));
    }

    [Fact]
    public async Task ComposeDeltaAsync_RoundTripCursor_AdvancesPastConsumedEvents()
    {
        _events.Append(1, CitizenCredentialEventKind.Added, NewPayload());
        var first = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, null);
        first!.Credentials.Added.Should().HaveCount(1);

        // Second sync with the returned cursor should yield no new events.
        _events.Append(2, CitizenCredentialEventKind.Added, NewPayload());

        var second = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, first.SyncToken);
        second!.Credentials.Added.Should().HaveCount(1, "only events with seq > 1 should appear");
    }

    [Fact]
    public async Task ComposeDeltaAsync_StaleCursor_Returns410Sentinel()
    {
        var freshCursor = _sut.MintCursor(HolderKeyId, lastEventSeq: 0);
        _clock.Advance(CitizenSyncService.MaxCursorAge.Add(TimeSpan.FromMinutes(1)));

        var response = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, freshCursor);

        response.Should().BeNull("stale cursor must surface as null so the endpoint can return 410");
    }

    [Fact]
    public async Task ComposeDeltaAsync_CursorBoundToDifferentHolder_Rejected()
    {
        var other = "deadbeef0123456789abcdef0123456789abcdef012";
        var foreignCursor = _sut.MintCursor(other, lastEventSeq: 0);

        var response = await _sut.ComposeDeltaAsync(PlatformUserId, HolderKeyId, foreignCursor);

        response.Should().BeNull("cursor sub claim must match the calling holder");
    }

    [Fact]
    public async Task ListAllCredentialsAsync_ReplaysToNetSnapshot()
    {
        var keepId = $"urn:credential:test:{Guid.NewGuid():N}";
        var dropId = $"urn:credential:test:{Guid.NewGuid():N}";
        _events.Append(1, CitizenCredentialEventKind.Added, NewPayload(keepId));
        _events.Append(2, CitizenCredentialEventKind.Added, NewPayload(dropId));
        _events.Append(3, CitizenCredentialEventKind.Revoked, new RevokedCredentialEntry
        {
            Id = dropId,
            Reason = CredentialRevocationReason.Withdrawn,
            RevokedAt = _clock.GetUtcNow(),
        });

        var snapshot = await _sut.ListAllCredentialsAsync(PlatformUserId, HolderKeyId);

        snapshot.Credentials.Should().ContainSingle(p => p.Id == keepId);
        snapshot.Credentials.Should().NotContain(p => p.Id == dropId);
    }

    private CachedCredentialPayload NewPayload(string? id = null) => new()
    {
        Id = id ?? $"urn:credential:test:{Guid.NewGuid():N}",
        Vct = "https://sorcha.dev/vc/test/v1",
        Jwt = "eyJ...",
        IssuerDid = "did:sorcha:org:test",
        IssuedAt = _clock.GetUtcNow(),
    };

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class StubEventStream : ICitizenCredentialEventStream
    {
        private readonly List<CitizenCredentialEvent> _events = [];

        public void Append(long seq, CitizenCredentialEventKind kind, object payload)
            => _events.Add(new CitizenCredentialEvent(seq, kind, payload));

        public Task<IReadOnlyList<CitizenCredentialEvent>> ReadAsync(Guid platformUserId, long afterSeq, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CitizenCredentialEvent>>(
                new ReadOnlyCollection<CitizenCredentialEvent>(_events.Where(e => e.Seq > afterSeq).ToList()));

        public Task<long> GetHighestSeqAsync(Guid platformUserId, CancellationToken ct = default)
            => Task.FromResult(_events.Count == 0 ? 0L : _events[^1].Seq);
    }
}
