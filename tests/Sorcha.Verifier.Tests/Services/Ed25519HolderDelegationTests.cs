// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Regression tests for the citizen-wallet holder→device delegation chain when the
/// holder key is <b>Ed25519</b> (the default Sorcha wallet algorithm).
///
/// <para>Before this fix the verifier's JWS check was ES256/EC-only, so an Ed25519
/// holder key (OKP <c>cnf.jwk</c>, EdDSA-signed delegation) failed with
/// "Delegation credential signature verification failed against holder key." — even
/// though the chain was cryptographically sound. These tests pin both algorithms.</para>
/// </summary>
public sealed class Ed25519HolderDelegationTests
{
    private const string Vct = "https://sorcha.dev/vc/test/v1";
    private const string Nonce = "verifier-nonce-abc";
    private const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    private readonly Mock<IStatusListCache> _statusList = new();
    private readonly VerifiablePresentationValidator _validator;

    public Ed25519HolderDelegationTests()
    {
        _statusList
            .Setup(s => s.CheckAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);
        _validator = new VerifiablePresentationValidator(
            _statusList.Object, TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance);
    }

    private static VerifierSession Session() => new()
    {
        SessionId = "sess-ed25519",
        ClientId = ClientId,
        Nonce = Nonce,
        RequiredVct = Vct,
        RequiredClaims = ["givenName"],
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
    public async Task ValidateAsync_Ed25519HolderKey_AcceptsDelegationChain()
    {
        var bundle = TestVpFactory.MintEd25519Holder(
            Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKey("givenName");
    }

    [Fact]
    public async Task ValidateAsync_Ed25519HolderKey_LivePresentationLayerReportsOkpAndEdDsa()
    {
        // The Feature 155 verdict trail (rendered by the Open Verifier PWA) must surface the holder
        // key's curve and the delegation algorithm, so an operator can see the Ed25519 (OKP) holder key
        // that explains the curve-specific behaviour — without browser dev tools.
        var bundle = TestVpFactory.MintEd25519Holder(
            Vct, Claims(("givenName", "Stuart")), ClientId, Nonce);

        var outcome = await _validator.ValidateAsync(Session(), bundle.VpToken, bundle.Delegation);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        var live = outcome.Layers.Should().ContainSingle(l => l.Layer == ValidationLayer.LivePresentation).Subject;
        live.Status.Should().Be(LayerStatus.Pass);
        live.Detail.Should().ContainKey("holder-key").WhoseValue.Should().Be("OKP / Ed25519");
        live.Detail.Should().ContainKey("delegation").WhoseValue.Should().Contain("EdDSA");
        live.Detail["delegation"].Should().Contain("EC / P-256"); // device key stays P-256
    }
}
