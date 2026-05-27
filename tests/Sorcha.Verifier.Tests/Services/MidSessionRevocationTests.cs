// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 138 US5 (T058) — revocation is re-checked at verify time, so a credential revoked after
/// the session opened fails verification even when a still-fresh KB-JWT is presented (FR-019).
/// Combined with US1's fail-closed status check, a presentation for a since-revoked credential cannot
/// pass mid-session.
/// </summary>
public sealed class MidSessionRevocationTests
{
    [Fact]
    public async Task ValidateAsync_DeviceCredentialRevokedMidSession_FreshProof_Rejected()
    {
        // Status list now reports the delegation credential as revoked (revoked after session open).
        var validator = VpValidatorTestHarness.BuildValidator(statusVerdict: StatusListVerdict.Revoked);

        // A perfectly fresh KB-JWT (default exp now+120s) — only revocation should fail it.
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce);

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("revoked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_StatusListUnverifiableMidSession_FreshProof_Rejected()
    {
        // US1 fail-closed: if the status list cannot be authenticated, verification fails closed.
        var validator = VpValidatorTestHarness.BuildValidator(statusVerdict: StatusListVerdict.Unverifiable);

        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce);

        var outcome = await validator.ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("could not be authenticated", StringComparison.Ordinal));
    }
}
