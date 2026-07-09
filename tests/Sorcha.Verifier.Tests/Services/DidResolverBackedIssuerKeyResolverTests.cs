// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Services;
using Sorcha.ServiceClients.Did;
using Xunit;using Sorcha.Verifier.Engine;


namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 120 US1 — Unit tests for <see cref="DidResolverBackedIssuerKeyResolver"/>.
/// Covers FR-003 failure-mode classification (did-unresolved | kid-unmatched | success)
/// and the kid-match strategy (exact id → thumbprint fallback → first-VM fallback).
/// </summary>
public sealed class DidResolverBackedIssuerKeyResolverTests
{
    private const string Issuer = "did:sorcha:org:abc";
    private const string Kid = "did:sorcha:org:abc#vc-issuance-1";

    // RFC 7638 §3.1 worked example — thumbprint NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs
    private const string SampleJwk = """
        {"kty":"RSA","n":"0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw","e":"AQAB","alg":"RS256","kid":"2011-04-29"}
        """;

    private static JsonElement Jwk()
    {
        return JsonDocument.Parse(SampleJwk).RootElement.Clone();
    }

    private static DidResolverBackedIssuerKeyResolver CreateResolver(IDidResolverRegistry registry)
        => new(registry, new TestMeterFactory(), NullLogger<DidResolverBackedIssuerKeyResolver>.Instance);

    [Fact]
    public async Task ResolveAsync_DidResolves_KidExactMatchesVm_ReturnsKey()
    {
        var doc = new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = Kid,
                    Type = "JsonWebKey2020",
                    Controller = Issuer,
                    PublicKeyJwk = Jwk()
                }
            ]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_DidResolves_KidMatchesByThumbprint_ReturnsKey()
    {
        const string thumbprintFragment = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";
        var doc = new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = Issuer,
                    PublicKeyJwk = Jwk()
                }
            ]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync(
            Issuer, $"did:sorcha:org:abc#{thumbprintFragment}");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_DidUnresolved_ReturnsNull()
    {
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DidDocument?)null);

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_DidHasNoVerificationMethods_ReturnsNull()
    {
        var doc = new DidDocument { Id = Issuer, VerificationMethod = [] };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_KidUnmatched_NoFallbackVm_ReturnsNull()
    {
        // Document has VMs but none with a JWK and the kid doesn't match — no fallback to apply.
        var doc = new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#other-key",
                    Type = "JsonWebKey2020",
                    Controller = Issuer,
                    PublicKeyMultibase = "zPlaceholder"
                }
            ]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NoKid_FallsBackToFirstJwkVm()
    {
        var doc = new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = Issuer,
                    PublicKeyJwk = Jwk()
                }
            ]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, kid: null);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_RegistryThrows_ReturnsNull_SoCompositeCanFallThrough()
    {
        // Issue #808 — a resolution failure (e.g. unreachable DID resolver / unresolvable
        // demo issuer) must be treated as "unresolved" (null), NOT propagated as an
        // exception. Otherwise CompositeIssuerKeyResolver crashes (500) instead of
        // falling through to the in-memory JWK registry where demo-mint keys live.
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused (localhost:443)"));

        var result = await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RegistryCancelled_PropagatesCancellation()
    {
        // A genuine cancellation must still surface — only non-cancellation failures
        // are swallowed into a null return.
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await CreateResolver(registry.Object).ResolveAsync(Issuer, Kid);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_NullIssuer_Throws()
    {
        var registry = new Mock<IDidResolverRegistry>();
        var resolver = CreateResolver(registry.Object);

        var act = async () => await resolver.ResolveAsync(string.Empty, Kid);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Feature 178 — address-form issuer (did:pkh / did:ethr) resolution ──────────

    [Fact]
    public async Task ResolveAsync_AddressFormVm_ReturnsRecoveryJwkEnvelope()
    {
        const string vmId = "did:pkh:eip155:1:0xabc#blockchainAccountId";
        var doc = new DidDocument
        {
            Id = "did:pkh:eip155:1:0xabc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = vmId,
                    Type = "EcdsaSecp256k1RecoveryMethod2020",
                    Controller = "did:pkh:eip155:1:0xabc",
                    BlockchainAccountId = "eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B"
                }
            ],
            AssertionMethod = [vmId]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync("did:pkh:eip155:1:0xabc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var result = await CreateResolver(registry.Object).ResolveAsync("did:pkh:eip155:1:0xabc", kid: null);

        result.Should().NotBeNull();
        result!.Value.GetProperty("crv").GetString().Should().Be("secp256k1");
        result.Value.GetProperty("blockchainAccountId").GetString()
            .Should().Be("eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B");
        result.Value.TryGetProperty("x", out _).Should().BeFalse("an address-form VM publishes no key");
    }

    [Fact]
    public async Task ResolveAsync_VmWithNeitherKeyNorAddress_ReturnsNull()
    {
        // A VM carrying neither publicKeyJwk nor blockchainAccountId is unusable → reject (US3).
        var doc = new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#opaque",
                    Type = "JsonWebKey2020",
                    Controller = Issuer,
                    PublicKeyMultibase = "zPlaceholder"
                }
            ]
        };
        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Issuer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        (await CreateResolver(registry.Object).ResolveAsync(Issuer, kid: null)).Should().BeNull();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];
        public Meter Create(MeterOptions options)
        {
            var m = new Meter(options);
            _meters.Add(m);
            return m;
        }
        public void Dispose()
        {
            foreach (var m in _meters) m.Dispose();
        }
    }
}
