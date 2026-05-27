// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Tests for <see cref="StatusListCache"/> (Feature 114; hardened in Feature 138 US1). Covers the
/// JWT parse path, verified bit lookup, fail-closed out-of-range handling, and verified-entry caching.
/// The forged/issuer-mismatch/fail-closed/freshness rejection paths live in the dedicated
/// StatusList*Tests classes alongside.
/// </summary>
public sealed class StatusListCacheTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    [Fact]
    public void ParseJwt_ValidPayload_DecodesBitstringAndClaims()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[5] = 0b0000_0010; // index 41 set (5*8 + 1)
        var jwt = StatusListTestHelpers.BuildSignedList(bits, Now.AddHours(24), key);

        var parsed = StatusListCache.ParseJwt(jwt);

        parsed.Bitstring.Length.Should().Be(32);
        parsed.Bitstring[5].Should().Be(0b0000_0010);
        parsed.Issuer.Should().Be(StatusListTestHelpers.Issuer);
        parsed.Alg.Should().Be("ES256");
        parsed.Kid.Should().Be("did:sorcha:org:abc#citizen-status-signing");
        parsed.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_BitSet_ReturnsRevoked_BitClear_ReturnsActive()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[1] = 0b0000_1000; // index 11 set
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(bits, Now.AddHours(24), key),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Revoked);
        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 10, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Active);
    }

    [Fact]
    public async Task CheckAsync_OutOfRange_FailsClosed()
    {
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(new byte[32], Now.AddHours(24), key),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 32 * 8, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_SecondCallReusesVerifiedCache_NoSecondHttpHit()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[0] = 0b0000_0001;
        var fetchCount = 0;
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(bits, Now.AddHours(24), key),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now),
            onRequest: _ => Interlocked.Increment(ref fetchCount));

        await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);
        await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);
        await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);

        fetchCount.Should().Be(1);
    }
}
