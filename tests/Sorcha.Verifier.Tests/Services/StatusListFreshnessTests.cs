// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US1 (T008) — freshness is enforced against the list's own <c>exp</c> within the
/// configured clock skew (no +24h default for a list missing <c>exp</c>), and the honest path still
/// reports revoked/active correctly for a genuine, signed, fresh list.
/// </summary>
public sealed class StatusListFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    [Fact]
    public async Task CheckAsync_ExpiredList_ReturnsUnverifiable()
    {
        var key = StatusListTestHelpers.NewKey();
        var jwt = StatusListTestHelpers.BuildSignedList(new byte[32], Now.AddHours(-2), key);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now),
            skew: TimeSpan.FromSeconds(60));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_NoExpClaim_ReturnsUnverifiable_NoTwentyFourHourDefault()
    {
        var key = StatusListTestHelpers.NewKey();
        var jwt = StatusListTestHelpers.BuildSignedList(new byte[32], Now, key, omitExp: true);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_GenuineFreshList_ReportsRevokedAndActiveCorrectly()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[1] = 0b0000_1000; // index 11 = revoked
        var jwt = StatusListTestHelpers.BuildSignedList(bits, Now.AddHours(1), key);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Revoked);
        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 10, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Active);
    }

    [Fact]
    public async Task CheckAsync_ExpiredButWithinSkew_StillTrusted()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[1] = 0b0000_1000;
        // Expired 30s ago, but skew is 60s → still fresh.
        var jwt = StatusListTestHelpers.BuildSignedList(bits, Now.AddSeconds(-30), key);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now),
            skew: TimeSpan.FromSeconds(60));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Revoked);
    }
}
