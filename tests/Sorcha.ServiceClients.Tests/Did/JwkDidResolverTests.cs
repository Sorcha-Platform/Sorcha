// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class JwkDidResolverTests
{
    private static readonly JwkDidResolver Resolver = new(NullLogger<JwkDidResolver>.Instance);

    [Fact]
    public void CanResolve_JwkMethod_True()
    {
        Resolver.CanResolve("jwk").Should().BeTrue();
        Resolver.CanResolve("key").Should().BeFalse();
    }

    [Theory]
    [InlineData("EC", "secp256k1")]
    [InlineData("EC", "P-256")]
    [InlineData("OKP", "Ed25519")]
    public async Task Resolve_AnyCurve_SurfacesJwkVerbatim(string kty, string crv)
    {
        var jwkJson = $$"""{"kty":"{{kty}}","crv":"{{crv}}","x":"ZmFrZS14LWNvb3JkaW5hdGU"}""";
        var did = "did:jwk:" + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(jwkJson));

        var doc = await Resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        var vm = doc!.VerificationMethod.Should().ContainSingle().Subject;
        vm.Id.Should().Be($"{did}#0");
        vm.Type.Should().Be("JsonWebKey2020");
        doc.AssertionMethod.Should().Contain(vm.Id);

        vm.PublicKeyJwk.Should().NotBeNull();
        vm.PublicKeyJwk!.Value.GetProperty("kty").GetString().Should().Be(kty);
        vm.PublicKeyJwk!.Value.GetProperty("crv").GetString().Should().Be(crv);
    }

    [Theory]
    [InlineData("did:jwk:")]                       // empty payload
    [InlineData("did:jwk:!!!not-base64url!!!")]    // undecodable
    public async Task Resolve_Malformed_ReturnsNull(string did)
    {
        (await Resolver.ResolveAsync(did)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_PayloadNotJsonObject_ReturnsNull()
    {
        var did = "did:jwk:" + Base64Url.EncodeToString(Encoding.UTF8.GetBytes("123"));
        (await Resolver.ResolveAsync(did)).Should().BeNull();
    }
}
