// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Engine.Models;
using Xunit;using Sorcha.Verifier.Engine;


namespace Sorcha.Verifier.Tests.Services;

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
            .Setup(s => s.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);
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

    // ── Review H3: issuer-signature status surfaced on the outcome ───────────────────────────

    [Fact]
    public async Task ValidateAsync_IssuerKeyUnresolved_NotRequired_AcceptsButReportsIssuerNotVerified()
    {
        // Default validator: OptOutIssuerKeyResolver + requireIssuerSignature:false (the PWA offline path).
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.IssuerSignature.Should().Be(IssuerSignatureStatus.NotVerified,
            "the issuer key could not be resolved and the verifier does not require it");
    }

    [Fact]
    public async Task ValidateAsync_IssuerKeyResolvesAndSignatureValid_ReportsIssuerVerified()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        var resolver = new JwkRegistryIssuerKeyResolver();
        resolver.Register(
            "did:sorcha:org:test",
            System.Text.Json.JsonDocument.Parse(TestVpFactory.ToJwk(bundle.IssuerKey)).RootElement);
        var validator = new VerifiablePresentationValidator(
            _statusList.Object, resolver, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance, requireIssuerSignature: false);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.IssuerSignature.Should().Be(IssuerSignatureStatus.Verified,
            "the issuer key resolved and the credential's issuer JWS verified against it");
    }

    [Fact]
    public async Task ValidateAsync_LegacyVcSdJwtTyp_StillAccepted()
    {
        // Feature 181 FR-004 lock-in: already-issued credentials carry typ "vc+sd-jwt" and live
        // in citizen wallets we cannot recall — the verify side must accept both media types.
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce,
            credentialTyp: "vc+sd-jwt");
        var resolver = new JwkRegistryIssuerKeyResolver();
        resolver.Register(
            "did:sorcha:org:test",
            System.Text.Json.JsonDocument.Parse(TestVpFactory.ToJwk(bundle.IssuerKey)).RootElement);
        var validator = new VerifiablePresentationValidator(
            _statusList.Object, resolver, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance, requireIssuerSignature: false);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
    }

    [Fact]
    public async Task ValidateAsync_IssuerKeyUnresolved_Required_Rejects()
    {
        // Authoritative server-gate posture (F120): require the issuer signature → reject when unresolvable.
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        var validator = new VerifiablePresentationValidator(
            _statusList.Object, new OptOutIssuerKeyResolver(), TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance, requireIssuerSignature: true);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse("require-issuer-signature rejects an unresolvable issuer key");
    }

    [Fact]
    public async Task ValidateAsync_NoDelegation_DeviceSignedKbJwt_RejectedAsKeyBindingMismatch()
    {
        // #1195 Phase 2 (C-1): a delegation-free present is now VALID under server-custody — but only
        // when the KB-JWT is signed by the credential's OWN cnf holder key. The Mint bundle's KB-JWT
        // is DEVICE-signed (it is built for the delegation model), so presenting it with no delegation
        // is the wrong-key case: it must reject with the NAMED key-binding-mismatch error, distinct
        // from the delegation-model errors. (The legitimate holder-signed server-custody path is
        // covered by ServerCustodyPresentationTests.)
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("Key binding mismatch", StringComparison.Ordinal));
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
            .Setup(s => s.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Revoked);
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

    [Fact]
    public async Task ValidateAsync_RegisteredIssuerKeyMatches_AcceptedAndIssuerSignatureVerified()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        var registry = new JwkRegistryIssuerKeyResolver();
        registry.Register("did:sorcha:org:test",
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                TestVpFactory.ToJwk(bundle.IssuerKey)));
        var hardened = new VerifiablePresentationValidator(
            _statusList.Object, registry, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature: true);

        var outcome = await hardened.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
    }

    [Fact]
    public async Task ValidateAsync_RegisteredIssuerKeyDoesNotMatch_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        // Register a DIFFERENT key under the same issuer DID — signature must reject.
        using var foreignIssuer = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var registry = new JwkRegistryIssuerKeyResolver();
        registry.Register("did:sorcha:org:test",
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                TestVpFactory.ToJwk(foreignIssuer)));
        var hardened = new VerifiablePresentationValidator(
            _statusList.Object, registry, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance);

        var outcome = await hardened.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("issuer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_RequireIssuerWithoutKey_Rejected()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        var hardened = new VerifiablePresentationValidator(
            _statusList.Object, new OptOutIssuerKeyResolver(), TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature: true);

        var outcome = await hardened.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("RequireIssuerSignature", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_OptOutResolver_AcceptsWithWarning_DefaultV1Behaviour()
    {
        // Default _validator uses the back-compat constructor (opt-out, !require).
        // This is the v1 contract: trust holder→device chain when issuer key is unknown.
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
    }
}

// Note: PresentationRequestBuilder smoke tests removed in Feature 164 B3 (US4 retirement).
// The PresentationRequestBuilder and InMemoryVerifierSessionStore have been deleted — the
// desk verifier now uses HaipVerificationTransport via the shared IVerificationTransport seam.
