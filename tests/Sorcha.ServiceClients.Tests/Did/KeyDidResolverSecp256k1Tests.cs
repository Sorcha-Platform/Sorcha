// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using SimpleBase;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class KeyDidResolverSecp256k1Tests
{
    [Fact]
    public async Task Resolve_Secp256k1DidKey_EmitsSecp256k1PublicKeyJwk()
    {
        var (expected, compressed) = GenerateSecp256k1();
        // did:key = 'z' (base58btc multibase) + base58(0xe701 multicodec || compressed key)
        var multicodec = (byte[])[0xe7, 0x01, .. compressed];
        var did = "did:key:z" + Base58.Bitcoin.Encode(multicodec);

        var resolver = new KeyDidResolver(NullLogger<KeyDidResolver>.Instance);
        var doc = await resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        var vm = doc!.VerificationMethod.Should().ContainSingle().Subject;
        vm.Type.Should().Be("JsonWebKey2020");
        doc.AssertionMethod.Should().Contain(vm.Id);

        vm.PublicKeyJwk.Should().NotBeNull();
        var jwk = vm.PublicKeyJwk!.Value;
        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("secp256k1");

        Secp256k1Jwk.TryParse(jwk, out var parsed).Should().BeTrue();
        parsed!.X.Should().Equal(expected.X);
        parsed.Y.Should().Equal(expected.Y);
    }

    [Fact]
    public async Task Resolve_UnknownMulticodec_ReturnsNull()
    {
        // 0xffff is not a supported multicodec prefix.
        var bytes = (byte[])[0xff, 0xff, 1, 2, 3];
        var did = "did:key:z" + Base58.Bitcoin.Encode(bytes);

        var resolver = new KeyDidResolver(NullLogger<KeyDidResolver>.Instance);
        (await resolver.ResolveAsync(did)).Should().BeNull();
    }

    private static (Secp256k1PublicKey Key, byte[] Compressed) GenerateSecp256k1()
    {
        var curve = SecNamedCurves.GetByName("secp256k1");
        var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        var pub = (ECPublicKeyParameters)gen.GenerateKeyPair().Public;
        var compressed = pub.Q.GetEncoded(true); // 33-byte compressed SEC1
        return (Secp256k1PublicKey.FromSec1(compressed), compressed);
    }
}
