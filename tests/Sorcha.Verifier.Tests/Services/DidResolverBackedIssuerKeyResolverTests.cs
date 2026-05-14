// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Services;
using Sorcha.ServiceClients.Did;
using Xunit;

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
    public async Task ResolveAsync_NullIssuer_Throws()
    {
        var registry = new Mock<IDidResolverRegistry>();
        var resolver = CreateResolver(registry.Object);

        var act = async () => await resolver.ResolveAsync(string.Empty, Kid);
        await act.Should().ThrowAsync<ArgumentException>();
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
