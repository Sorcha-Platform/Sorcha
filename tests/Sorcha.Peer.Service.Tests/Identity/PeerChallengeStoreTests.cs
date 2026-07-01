// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Identity;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="PeerChallengeStore"/> (Feature 175) — one-time, TTL-bound registration
/// nonces underpinning replay resistance of the node-identity proof.
/// </summary>
public sealed class PeerChallengeStoreTests
{
    private static PeerChallengeStore CreateStore(int ttlSeconds, Func<DateTimeOffset> now) =>
        new(Options.Create(new PeerServiceConfiguration { ChallengeTtlSeconds = ttlSeconds }), now);

    [Fact]
    public void Issued_Nonce_Consumes_Exactly_Once()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore(30, () => now);

        var (nonce, _) = store.Issue("peer-1");

        store.TryConsume("peer-1", nonce).Should().BeTrue("first consume of a valid nonce succeeds");
        store.TryConsume("peer-1", nonce).Should().BeFalse("a nonce is single-use (replay refused)");
    }

    [Fact]
    public void Consume_Fails_For_Unknown_Nonce_Or_Wrong_Peer()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore(30, () => now);
        var (nonce, _) = store.Issue("peer-1");

        store.TryConsume("peer-1", "not-the-nonce").Should().BeFalse();
        store.TryConsume("peer-2", nonce).Should().BeFalse("the nonce is bound to the issuing peer id");
    }

    [Fact]
    public void Consume_Fails_After_Expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = now;
        var store = CreateStore(30, () => clock);
        var (nonce, expiresAt) = store.Issue("peer-1");

        clock = expiresAt.AddSeconds(1);

        store.TryConsume("peer-1", nonce).Should().BeFalse("an expired nonce is refused");
    }

    [Fact]
    public void Issue_Returns_Distinct_Nonces()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore(30, () => now);

        var (a, _) = store.Issue("peer-1");
        var (b, _) = store.Issue("peer-1");

        a.Should().NotBe(b);
    }
}
