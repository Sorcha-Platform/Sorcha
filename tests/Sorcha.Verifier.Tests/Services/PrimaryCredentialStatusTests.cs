// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.Verification.Abstractions;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// #1759 — the validator checked the status list of the DELEGATION only. The PRIMARY credential's own
/// <c>status.status_list</c> was never consulted, so a credential its issuer had revoked was accepted
/// as long as the wrapping delegation was still active. The primary's list is now checked, pinned to
/// the credential's own issuer, fail-closed.
/// </summary>
public sealed class PrimaryCredentialStatusTests
{
    private const string CredentialIssuer = "did:sorcha:org:test";
    private const string PrimaryList = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/L1";
    private const string DelegationList = "https://verify.test/status/00000000000000000000000000000000/citizen-devices/0.statuslist+jwt";

    [Fact]
    public async Task ValidateAsync_PrimaryCredentialRevoked_IsRefusedEvenThoughTheDelegationIsActive()
    {
        var cache = Cache(primary: StatusListVerdict.Revoked);
        var outcome = await Validate(cache.Object, WithIetfStatus());

        outcome.Accepted.Should().BeFalse("the issuer has revoked this credential");
        outcome.Errors.Should().Contain(e => e.Contains("revoked", StringComparison.OrdinalIgnoreCase),
            "consumers map the word 'revoked' to the Revoked decline reason");
        cache.Verify(c => c.CheckAsync(PrimaryList, 11, CredentialIssuer, It.IsAny<CancellationToken>()), Times.Once);
        outcome.Layers.Single(l => l.Layer == ValidationLayer.Revocation).Status.Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public async Task ValidateAsync_PrimaryStatusUnauthenticatable_FailsClosed()
    {
        var outcome = await Validate(Cache(primary: StatusListVerdict.Unverifiable).Object, WithIetfStatus());

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("could not be authenticated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_PrimaryAndDelegationBothActive_IsAccepted()
    {
        var cache = Cache(primary: StatusListVerdict.Active);
        var outcome = await Validate(cache.Object, WithIetfStatus());

        outcome.Accepted.Should().BeTrue(string.Join("; ", outcome.Errors));
        cache.Verify(c => c.CheckAsync(PrimaryList, 11, CredentialIssuer, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.CheckAsync(DelegationList, 7, TestVpFactory.StatusIssuer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_CredentialCarryingOnlyAW3cStatus_IsRefused()
    {
        // The W3C list is served unsigned (#1769), so it cannot be authenticated; accepting the
        // credential would be accepting a status nobody checked.
        var outcome = await Validate(Cache(primary: StatusListVerdict.Active).Object, new Dictionary<string, object>
        {
            ["credentialStatus"] = new Dictionary<string, object>
            {
                ["type"] = "BitstringStatusListEntry",
                ["statusPurpose"] = "revocation",
                ["statusListCredential"] = "https://n1.sorcha.dev/api/v1/credentials/status-lists/L1",
                ["statusListIndex"] = "11",
            },
        });

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("credentialStatus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_CredentialWithNoStatusAtAll_IsNotBlockedByTheNewCheck()
    {
        var outcome = await Validate(Cache(primary: StatusListVerdict.Revoked).Object, extraClaims: null);

        outcome.Accepted.Should().BeTrue(string.Join("; ", outcome.Errors));
    }

    private static Dictionary<string, object> WithIetfStatus() => new()
    {
        ["status"] = new Dictionary<string, object>
        {
            ["status_list"] = new Dictionary<string, object> { ["uri"] = PrimaryList, ["idx"] = 11 },
        },
        // Sorcha credentials carry both shapes until #1769; the IETF one is the one that is checked.
        ["credentialStatus"] = new Dictionary<string, object>
        {
            ["type"] = "BitstringStatusListEntry",
            ["statusPurpose"] = "revocation",
            ["statusListCredential"] = "https://n1.sorcha.dev/api/v1/credentials/status-lists/L1",
            ["statusListIndex"] = "11",
        },
    };

    /// <summary>The delegation's list is always active here; only the primary's verdict varies.</summary>
    private static Mock<IStatusListCache> Cache(StatusListVerdict primary)
    {
        var cache = new Mock<IStatusListCache>(MockBehavior.Strict);
        cache.Setup(c => c.CheckAsync(DelegationList, 7, TestVpFactory.StatusIssuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);
        cache.Setup(c => c.CheckAsync(PrimaryList, 11, CredentialIssuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(primary);
        return cache;
    }

    private static Task<VerificationOutcome> Validate(IStatusListCache cache, Dictionary<string, object>? extraClaims)
    {
        var bundle = TestVpFactory.Mint(
            VpValidatorTestHarness.Vct,
            VpValidatorTestHarness.Claims(("givenName", "Stuart")),
            VpValidatorTestHarness.ClientId,
            VpValidatorTestHarness.Nonce,
            statusListUri: DelegationList,
            statusListIndex: 7,
            extraPayloadClaims: extraClaims);
        return VpValidatorTestHarness.BuildValidator(statusListCache: cache)
            .ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, bundle.Delegation);
    }
}
