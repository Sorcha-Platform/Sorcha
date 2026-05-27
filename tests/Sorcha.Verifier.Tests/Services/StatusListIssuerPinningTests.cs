// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US1 (T006) — a status list whose <c>iss</c> claim does not match the expected
/// organisation DID is rejected, even when its own signature is internally valid (an attacker's
/// genuinely-self-signed list must not satisfy a different org's revocation check).
/// </summary>
public sealed class StatusListIssuerPinningTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    [Fact]
    public async Task CheckAsync_IssuerMismatch_ReturnsUnverifiable_EvenWithValidSelfSignature()
    {
        var attackerKey = StatusListTestHelpers.NewKey();
        // A perfectly valid, self-signed list — but issued by the attacker's own org DID.
        const string attackerIssuer = "did:sorcha:org:evil";
        var jwt = StatusListTestHelpers.BuildSignedList(
            new byte[32], Now.AddHours(1), attackerKey, issuer: attackerIssuer);

        // The resolver would even hand back the attacker's key for the attacker's DID — but the
        // verifier pins to the EXPECTED issuer (the credential's org), so the mismatch is caught first.
        var cache = StatusListTestHelpers.BuildCache(
            jwt, StatusListTestHelpers.ResolverFor(attackerKey.PublicJwk, attackerIssuer), new FixedTimeProvider(Now));

        var verdict = await cache.CheckAsync(
            StatusListTestHelpers.ListUri, 11, expectedIssuer: StatusListTestHelpers.Issuer);

        verdict.Should().Be(StatusListVerdict.Unverifiable);
    }
}
