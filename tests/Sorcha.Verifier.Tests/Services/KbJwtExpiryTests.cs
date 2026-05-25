// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US5 (T056) — a captured KB-JWT replayed after its own <c>exp</c> is rejected even
/// while the verifier session is still open, closing the multi-minute session-TTL replay window.
/// </summary>
public sealed class KbJwtExpiryTests
{
    [Fact]
    public async Task ValidateAsync_KbJwtExpiredBeyondSkew_Rejected()
    {
        var validator = VpValidatorTestHarness.BuildValidator();
        // KB-JWT expired 120s ago — well beyond the 60s default skew. Session is still open.
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce,
            kbJwtIssuedAt: DateTimeOffset.UtcNow.AddSeconds(-180),
            kbJwtExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-120));

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("KB-JWT has expired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_KbJwtFreshWithinSkew_Accepted()
    {
        var validator = VpValidatorTestHarness.BuildValidator();
        // Expired 30s ago but within the 60s skew → still fresh.
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce,
            kbJwtIssuedAt: DateTimeOffset.UtcNow.AddSeconds(-40),
            kbJwtExpiresAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
    }
}
