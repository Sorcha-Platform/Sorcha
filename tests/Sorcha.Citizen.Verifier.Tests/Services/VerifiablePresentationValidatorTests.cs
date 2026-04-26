// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Citizen.Verifier.Services;
using Sorcha.Citizen.Verifier.Services.Models;
using Xunit;

namespace Sorcha.Citizen.Verifier.Tests.Services;

/// <summary>
/// Tests for <see cref="VerifiablePresentationValidator"/> (Feature 114, T090).
/// Exercises the offline holder→device delegation chain end-to-end:
/// happy path, malformed input, revoked delegation, expired delegation, tampered
/// KB-JWT, mismatched nonce/aud, missing required claim.
/// </summary>
public sealed class VerifiablePresentationValidatorTests
{
    private const string Vct = "https://sorcha.dev/vc/test/v1";
    private const string Nonce = "verifier-nonce-abc";
    private const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    private readonly Mock<IStatusListCache> _statusList = new();
    private readonly VerifiablePresentationValidator _validator;

    public VerifiablePresentationValidatorTests()
    {
        _statusList
            .Setup(s => s.IsRevokedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _validator = new VerifiablePresentationValidator(
            _statusList.Object, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance);
    }

    private static VerifierSession Session(IReadOnlyList<string>? required = null) => new()
    {
        SessionId = "sess-1",
        ClientId = ClientId,
        Nonce = Nonce,
        RequiredVct = Vct,
        RequiredClaims = required ?? ["givenName"],
        OptionalClaims = [],
        Purpose = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static Dictionary<string, System.Text.Json.JsonElement> Claims(params (string Name, string Value)[] pairs)
    {
        var d = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (n, v) in pairs)
            d[n] = System.Text.Json.JsonSerializer.SerializeToElement(v);
        return d;
    }

    [Fact]
    public async Task ValidateAsync_HappyPath_ReturnsAccepted()
    {
        var bundle = TestVpFactory.Mint(Vct,
            Claims(("givenName", "Stuart"), ("familyName", "Fraser")),
            ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKey("givenName");
        outcome.DisclosedClaims.Should().ContainKey("familyName");
    }

    [Fact]
    public async Task ValidateAsync_MissingDelegation_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("Delegation credential is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_TamperedKbJwtSignature_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        // Flip the last char of the KB-JWT signature
        var idx = bundle.VpToken.LastIndexOf('~');
        var tampered = bundle.VpToken[..^1] + (bundle.VpToken[^1] == 'A' ? 'B' : 'A');

        var outcome = await _validator.ValidateAsync(Session(), tampered, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("KB-JWT signature", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_NonceMismatch_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, "wrong-nonce");

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("nonce", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_AudienceMismatch_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")),
            "did:sorcha:verifier:wrong", Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("aud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_ExpiredDelegation_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce,
            delegationExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_RevokedDelegation_Rejected()
    {
        _statusList
            .Setup(s => s.IsRevokedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("revoked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredClaim_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("familyName", "Fraser")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(
            Session(required: ["givenName"]), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("givenName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_VctMismatch_Rejected()
    {
        var bundle = TestVpFactory.Mint("https://sorcha.dev/vc/wrong/v1",
            Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("vct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_MalformedVpToken_Rejected()
    {
        var outcome = await _validator.ValidateAsync(Session(), "not-a-jwt", "also-not-a-jwt");
        outcome.Accepted.Should().BeFalse();
    }
}

/// <summary>Smoke tests for <see cref="PresentationRequestBuilder"/> (T088).</summary>
public sealed class PresentationRequestBuilderTests
{
    [Fact]
    public async Task CreateAsync_ProducesDeepLinkAndStoresSession()
    {
        var store = new InMemoryVerifierSessionStore();
        var builder = new PresentationRequestBuilder(
            store, TimeProvider.System,
            NullLogger<PresentationRequestBuilder>.Instance);

        var result = await builder.CreateAsync(
            Guid.NewGuid(), "Confirm identity",
            "https://sorcha.dev/vc/test/v1",
            ["givenName"], ["familyName"],
            "https://verify.local");

        result.DeepLink.Should().StartWith("openid4vp://?");
        result.DeepLink.Should().Contain("response_mode=direct_post");
        result.DeepLink.Should().Contain("nonce=");
        result.DeepLink.Should().Contain("client_id=");
        result.Session.RequiredClaims.Should().ContainSingle().Which.Should().Be("givenName");
        store.Get(result.Session.SessionId).Should().NotBeNull();
    }

    [Fact]
    public void BuildPresentationDefinitionJson_IncludesRequiredAndOptionalFields()
    {
        var json = PresentationRequestBuilder.BuildPresentationDefinitionJson(
            "sess-1", "https://sorcha.dev/vc/test/v1",
            ["givenName"], ["familyName"], "purpose");

        json.Should().Contain("\"id\":\"sess-1\"");
        json.Should().Contain("givenName");
        json.Should().Contain("familyName");
        json.Should().Contain("\"limit_disclosure\":\"required\"");
    }
}
