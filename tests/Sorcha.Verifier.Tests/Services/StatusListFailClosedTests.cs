// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US1 (T007) — when the status list cannot be fetched (or its key cannot be resolved)
/// the verifier fails closed (<see cref="StatusListVerdict.Unverifiable"/>) and never serves a stale
/// cached copy. This is the removal of the old fail-open path.
/// </summary>
public sealed class StatusListFailClosedTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    [Fact]
    public async Task CheckAsync_FetchFails_NoCache_ReturnsUnverifiable()
    {
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            HttpStatusCode.InternalServerError,
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_KeyUnresolved_ReturnsUnverifiable()
    {
        var key = StatusListTestHelpers.NewKey();
        var jwt = StatusListTestHelpers.BuildSignedList(new byte[32], Now.AddHours(1), key);

        // Genuine list, but the resolver has no key for the issuer (e.g. DID not published).
        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.NullResolver(), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_FreshFetchFails_DoesNotServeStaleCache()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32];
        bits[1] = 0b0000_1000; // index 11 = revoked
        var clock = new FixedTimeProvider(Now);

        var responses = new Queue<HttpResponseMessage>();
        // First response seeds a VERIFIED entry that is about to expire.
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                StatusListTestHelpers.BuildSignedList(bits, Now.AddSeconds(1), key),
                Encoding.UTF8, "application/statuslist+jwt"),
        });
        // Second fetch fails.
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cache = StatusListTestHelpers.BuildCacheFromResponses(
            responses, StatusListTestHelpers.ResolverFor(key.PublicJwk), clock, skew: TimeSpan.Zero);

        // First call: seeds the cache, reports revoked.
        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Revoked);

        // Advance past the entry's exp so a fresh fetch is forced; that fetch fails.
        clock.Advance(TimeSpan.FromSeconds(5));

        // MUST fail closed — NOT serve the stale (now-expired) cached copy.
        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Unverifiable);
    }
}
