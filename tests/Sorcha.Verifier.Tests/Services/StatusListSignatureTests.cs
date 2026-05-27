// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US1 (T005) — a status list whose signature does not verify against the issuing
/// organisation's resolved key is rejected (fail closed), even though it is structurally valid.
/// </summary>
public sealed class StatusListSignatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    [Fact]
    public async Task CheckAsync_ForgedSignature_ReturnsUnverifiable()
    {
        var key = StatusListTestHelpers.NewKey();
        var bits = new byte[32]; // index 11 NOT set — the attacker wants it to read "active"
        // Forge: the bytes say "active" but the signature is corrupt.
        var jwt = StatusListTestHelpers.BuildSignedList(
            bits, Now.AddHours(1), key, corruptSignature: true);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_SignedByWrongKey_ReturnsUnverifiable()
    {
        var realKey = StatusListTestHelpers.NewKey();
        var attackerKey = StatusListTestHelpers.NewKey();
        // List signed by the attacker's key, but the resolver only trusts the real issuer key.
        var jwt = StatusListTestHelpers.BuildSignedList(new byte[32], Now.AddHours(1), attackerKey);

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(realKey.PublicJwk), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedAlgorithm_ReturnsUnverifiable()
    {
        var key = StatusListTestHelpers.NewKey();
        // EdDSA is not verifiable in-engine → fail closed (never fail open).
        var jwt = StatusListTestHelpers.BuildSignedList(new byte[32], Now.AddHours(1), key, alg: "EdDSA");

        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(key.PublicJwk), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(StatusListTestHelpers.ListUri, 11, StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }
}
