// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US5 (T057) — a KB-JWT MUST carry an <c>exp</c> (FR-017) and MUST NOT exceed the
/// configured maximum lifetime (so an over-long-lived proof cannot widen the replay window).
/// </summary>
public sealed class KbJwtMissingExpTests
{
    [Fact]
    public async Task ValidateAsync_KbJwtMissingExp_Rejected()
    {
        var validator = VpValidatorTestHarness.BuildValidator();
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce,
            omitKbJwtExp: true);

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("missing the mandatory exp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_KbJwtOverLongLifetime_Rejected()
    {
        // Default max lifetime is 120s. Fresh (exp in the future) but minted with a 600s lifetime.
        var validator = VpValidatorTestHarness.BuildValidator();
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce,
            kbJwtIssuedAt: DateTimeOffset.UtcNow.AddSeconds(-300),
            kbJwtExpiresAt: DateTimeOffset.UtcNow.AddSeconds(300));

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("exceeds the maximum", StringComparison.Ordinal));
    }
}
