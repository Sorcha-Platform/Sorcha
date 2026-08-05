// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Xunit;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 155 (T008 / T018) — the enriched per-layer verdict trail surfaced on
/// <see cref="VerificationOutcome.Layers"/>. Asserts the LivePresentation, IssuerSignature, and
/// Revocation layers without changing the accept/reject contract verified by
/// <see cref="VerifiablePresentationValidatorTests"/>.
/// </summary>
public sealed class VerifiablePresentationValidatorLayersTests
{
    private const string Vct = "https://sorcha.dev/vc/test/v1";
    private const string Nonce = "verifier-nonce-155";
    private const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    private static VerifierSession Session(IReadOnlyList<string>? required = null) => new()
    {
        SessionId = "sess-155",
        ClientId = ClientId,
        Nonce = Nonce,
        RequiredVct = Vct,
        RequiredClaims = required ?? ["givenName"],
        OptionalClaims = [],
        Purpose = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    private static Dictionary<string, JsonElement> Claims(params (string Name, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (var (n, v) in pairs)
            d[n] = JsonSerializer.SerializeToElement(v);
        return d;
    }

    private static Mock<IStatusListCache> StatusList(StatusListVerdict verdict)
    {
        var mock = new Mock<IStatusListCache>();
        mock.Setup(s => s.CheckAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verdict);
        return mock;
    }

    private static VerifiablePresentationValidator Validator(
        IStatusListCache statusList,
        IIssuerKeyResolver? resolver = null,
        bool requireIssuerSignature = false)
        => new(
            statusList,
            resolver ?? new OptOutIssuerKeyResolver(),
            TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature);

    private static ValidationLayerResult Layer(VerificationOutcome o, ValidationLayer layer)
        => o.Layers.Single(l => l.Layer == layer);

    [Fact]
    public async Task ValidateAsync_HappyPath_LivePresentationPass_RevocationPass()
    {
        var validator = Validator(StatusList(StatusListVerdict.Active).Object);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        Layer(outcome, ValidationLayer.LivePresentation).Status.Should().Be(VerificationStatus.Verified);
        Layer(outcome, ValidationLayer.Revocation).Status.Should().Be(VerificationStatus.Verified);
        // The engine never appends RegisterAnchor — that is the verifier app's job.
        outcome.Layers.Should().NotContain(l => l.Layer == ValidationLayer.RegisterAnchor);
    }

    [Fact]
    public async Task ValidateAsync_HappyPath_LivePresentationDetail_CarriesProtocolNonceAud()
    {
        var validator = Validator(StatusList(StatusListVerdict.Active).Object);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        var live = Layer(outcome, ValidationLayer.LivePresentation);
        live.Detail.Should().ContainKey("protocol")
            .WhoseValue.Should().Contain("OpenID4VP");
        live.Detail.Should().ContainKey("nonce").WhoseValue.Should().Be("matches request");
        live.Detail.Should().ContainKey("aud").WhoseValue.Should().Be(ClientId);
        live.Detail.Should().ContainKey("kb-jwt").WhoseValue.Should().Contain("ES256");
    }

    [Fact]
    public async Task ValidateAsync_ResolvedIssuerKeyVerifies_IssuerSignatureLayerPass()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        var resolver = new JwkRegistryIssuerKeyResolver();
        resolver.Register(
            "did:sorcha:org:test",
            JsonSerializer.Deserialize<JsonElement>(TestVpFactory.ToJwk(bundle.IssuerKey)));
        var validator = Validator(StatusList(StatusListVerdict.Active).Object, resolver);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        var issuer = Layer(outcome, ValidationLayer.IssuerSignature);
        issuer.Status.Should().Be(VerificationStatus.Verified);
        issuer.Detail.Should().ContainKey("iss").WhoseValue.Should().Be("did:sorcha:org:test");
        issuer.Detail.Should().ContainKey("alg");
    }

    [Fact]
    public async Task ValidateAsync_UnresolvedIssuerKey_NotRequired_IssuerSignatureLayerUnverified()
    {
        // Default opt-out resolver + requireIssuerSignature:false (the PWA offline path).
        var validator = Validator(StatusList(StatusListVerdict.Active).Object);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        Layer(outcome, ValidationLayer.IssuerSignature).Status.Should().Be(VerificationStatus.Unverified);
    }

    [Fact]
    public async Task ValidateAsync_ResolvedIssuerKeyMismatch_IssuerSignatureLayerFail()
    {
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);
        // Register a DIFFERENT key under the issuer DID — the JWS verification must fail.
        using var foreignIssuer = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var resolver = new JwkRegistryIssuerKeyResolver();
        resolver.Register(
            "did:sorcha:org:test",
            JsonSerializer.Deserialize<JsonElement>(TestVpFactory.ToJwk(foreignIssuer)));
        var validator = Validator(StatusList(StatusListVerdict.Active).Object, resolver);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        Layer(outcome, ValidationLayer.IssuerSignature).Status.Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public async Task ValidateAsync_RevokedStatusList_RevocationLayerFail()
    {
        var validator = Validator(StatusList(StatusListVerdict.Revoked).Object);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeFalse();
        var revocation = Layer(outcome, ValidationLayer.Revocation);
        revocation.Status.Should().Be(VerificationStatus.Failed);
        revocation.Detail.Should().ContainKey("result").WhoseValue.Should().Be("Revoked");
        revocation.Detail.Should().ContainKey("statusList");
        revocation.Detail.Should().ContainKey("idx");
    }

    [Fact]
    public async Task ValidateAsync_UnverifiableStatusList_RevocationLayerUnverified()
    {
        var validator = Validator(StatusList(StatusListVerdict.Unverifiable).Object);
        var bundle = TestVpFactory.Mint(Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        // Fail-closed: an unverifiable status list rejects the presentation overall (F138),
        // but the Revocation LAYER status is Unverified (could-not-determine), not Fail.
        outcome.Accepted.Should().BeFalse();
        Layer(outcome, ValidationLayer.Revocation).Status.Should().Be(VerificationStatus.Unverified);
    }
}
