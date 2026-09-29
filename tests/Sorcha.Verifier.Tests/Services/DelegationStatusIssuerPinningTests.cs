// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// #1759 (d) — a device delegation's <c>iss</c> is the HOLDER (<c>did:sorcha:holder:…</c>), but its
/// status list is signed by the org's status signer. The validator used to pin the list to the
/// delegation's iss, which no real list can ever carry, so every real delegated presentation with a
/// status reference failed closed. The delegation now names its list's signer (<c>status_issuer</c>,
/// inside the holder-signed payload) and the list is pinned to exactly that.
/// </summary>
public sealed class DelegationStatusIssuerPinningTests
{
    private const string ListUri = "https://verify.test/status/00000000000000000000000000000000/citizen-devices/0.statuslist+jwt";

    [Fact]
    public async Task ValidateAsync_DelegationStatusList_IsCheckedAgainstTheSignerTheDelegationNames()
    {
        var cache = new Mock<IStatusListCache>(MockBehavior.Strict);
        cache.Setup(c => c.CheckAsync(ListUri, 7, TestVpFactory.StatusIssuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);
        var validator = VpValidatorTestHarness.BuildValidator(statusListCache: cache.Object);
        var bundle = Mint();

        var outcome = await validator.ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join("; ", outcome.Errors));
        cache.Verify(c => c.CheckAsync(ListUri, 7, TestVpFactory.StatusIssuer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_DelegationNamingNoStatusSigner_FailsClosedWithoutFetchingTheList()
    {
        var cache = new Mock<IStatusListCache>(MockBehavior.Strict);
        var validator = VpValidatorTestHarness.BuildValidator(statusListCache: cache.Object);
        var bundle = Mint(statusIssuer: null);

        var outcome = await validator.ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("with no named signer, the list's authenticity cannot be established");
        outcome.Errors.Should().Contain(e => e.Contains("status_issuer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_DelegationNamingASignerThatIsNotADid_FailsClosed()
    {
        // A cache that would vouch for ANY issuer — so only the DID-shape guard can refuse this.
        var cache = new Mock<IStatusListCache>();
        cache.Setup(c => c.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);
        var validator = VpValidatorTestHarness.BuildValidator(statusListCache: cache.Object);
        var bundle = Mint(statusIssuer: "https://evil.example/list-signer");

        var outcome = await validator.ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
    }

    private static TestVpFactory.Bundle Mint(string? statusIssuer = TestVpFactory.StatusIssuer) => TestVpFactory.Mint(
        VpValidatorTestHarness.Vct,
        VpValidatorTestHarness.Claims(("givenName", "Stuart")),
        VpValidatorTestHarness.ClientId,
        VpValidatorTestHarness.Nonce,
        statusListUri: ListUri,
        statusListIndex: 7,
        statusIssuer: statusIssuer);
}
