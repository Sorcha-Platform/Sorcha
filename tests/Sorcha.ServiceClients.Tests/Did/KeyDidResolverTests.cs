// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using SimpleBase;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class KeyDidResolverTests
{
    private readonly Mock<ILogger<KeyDidResolver>> _loggerMock = new();
    private readonly KeyDidResolver _resolver;

    public KeyDidResolverTests()
    {
        _resolver = new KeyDidResolver(_loggerMock.Object);
    }

    [Fact]
    public void CanResolve_Key_ReturnsTrue()
    {
        _resolver.CanResolve("key").Should().BeTrue();
    }

    [Fact]
    public void CanResolve_OtherMethod_ReturnsFalse()
    {
        _resolver.CanResolve("sorcha").Should().BeFalse();
        _resolver.CanResolve("web").Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_Ed25519Key_ReturnsDidDocument()
    {
        // Build a valid ED25519 did:key
        // Multicodec: 0xed01 + 32 bytes of key
        var keyBytes = new byte[32];
        Array.Fill(keyBytes, (byte)0xAB);
        var encoded = new byte[] { 0xed, 0x01 }.Concat(keyBytes).ToArray();
        var multibase = "z" + Base58.Bitcoin.Encode(encoded);
        var did = $"did:key:{multibase}";

        var doc = await _resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(did);
        doc.VerificationMethod.Should().HaveCount(1);
        doc.VerificationMethod[0].Type.Should().Be("Ed25519VerificationKey2020");
        doc.VerificationMethod[0].PublicKeyMultibase.Should().Be(multibase);
        doc.Authentication.Should().HaveCount(1);
        doc.AssertionMethod.Should().HaveCount(1);
    }

    [Fact]
    public async Task ResolveAsync_P256Key_ReturnsJsonWebKey2020WithTheKeysCoordinates()
    {
        // A REAL P-256 key (BCL), compressed per the did:key spec, behind multicodec 0x1200 as an
        // unsigned varint (0x80 0x24 — the prefix the spec's own example decodes to). The x/y the resolver publishes
        // must be the ones the key actually has, or no signature by it will ever verify.
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = ecdsa.ExportParameters(false).Q;
        var compressed = new byte[33];
        compressed[0] = (byte)(0x02 | (q.Y![31] & 1));
        q.X!.CopyTo(compressed, 1);
        var did = "did:key:z" + Base58.Bitcoin.Encode(new byte[] { 0x80, 0x24 }.Concat(compressed).ToArray());

        var doc = await _resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(did);
        var vm = doc.VerificationMethod[0];
        vm.Type.Should().Be("JsonWebKey2020");
        var jwk = vm.PublicKeyJwk!.Value;
        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("P-256");
        System.Buffers.Text.Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()).Should().Equal(q.X);
        System.Buffers.Text.Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString()).Should().Equal(q.Y);
    }

    [Fact]
    public async Task ResolveAsync_Ed25519Key_PublishesAnOkpJwkForTheSameKey()
    {
        // The issuer-key resolvers consume publicKeyJwk, not multibase. Without it an Ed25519
        // did:key issuer resolves to a document whose key no verifier can use.
        var (publicKey, _) = Ed25519KeyPair();
        var did = "did:key:z" + Base58.Bitcoin.Encode(new byte[] { 0xed, 0x01 }.Concat(publicKey).ToArray());

        var doc = await _resolver.ResolveAsync(did);

        var jwk = doc!.VerificationMethod[0].PublicKeyJwk!.Value;
        jwk.GetProperty("kty").GetString().Should().Be("OKP");
        jwk.GetProperty("crv").GetString().Should().Be("Ed25519");
        System.Buffers.Text.Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()).Should().Equal(publicKey);
    }

    [Theory]
    // Published examples in the did:key spec (w3c-ccg.github.io/did-key-spec, v0.9).
    [InlineData("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK", "OKP")]
    [InlineData("did:key:z6Mkf5rGMoatrSj1f4CyvuHBeXJELe9RPdzo2PKGNCKVtZxP", "OKP")]
    [InlineData("did:key:zDnaerx9CtbPJ1q36T5Ln5wYt3MQYeGRG5ehnPAmxcf5mDZpv", "EC")]
    public async Task ResolveAsync_SpecExamples_PublishAUsableJwk(string did, string kty)
    {
        var doc = await _resolver.ResolveAsync(did);

        var jwk = doc!.VerificationMethod[0].PublicKeyJwk!.Value;
        jwk.GetProperty("kty").GetString().Should().Be(kty);
        if (kty == "EC")
        {
            // A decompression bug yields a point that is not on the curve; the BCL refuses those.
            var act = () => System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters
            {
                Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
                Q = new System.Security.Cryptography.ECPoint
                {
                    X = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()),
                    Y = System.Buffers.Text.Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString()),
                },
            });
            act.Should().NotThrow();
        }
    }

    [Fact]
    public async Task ResolveAsync_P256BytesThatAreNotACurvePoint_ReturnsNull()
    {
        var keyBytes = new byte[33];
        keyBytes[0] = 0x02;
        Array.Fill(keyBytes, (byte)0xFF, 1, 32); // x >= p: no such point
        var did = "did:key:z" + Base58.Bitcoin.Encode(new byte[] { 0x80, 0x24 }.Concat(keyBytes).ToArray());

        var doc = await _resolver.ResolveAsync(did);

        doc.Should().BeNull("a key that cannot exist must not resolve to a verification method");
    }

    private static (byte[] PublicKey, byte[] PrivateKey) Ed25519KeyPair()
    {
        var gen = new Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.Parameters.Ed25519KeyGenerationParameters(
            new Org.BouncyCastle.Security.SecureRandom()));
        var pair = gen.GenerateKeyPair();
        return (((Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters)pair.Public).GetEncoded(),
                ((Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters)pair.Private).GetEncoded());
    }

    [Fact]
    public async Task ResolveAsync_InvalidMultibasePrefix_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("did:key:f0123456789abcdef");
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_TooShortKey_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("did:key:z1");
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EmptyDid_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("");
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WrongPrefix_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("did:web:example.com");
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedMulticodec_ReturnsNull()
    {
        // Unknown multicodec prefix 0xFF01 + some bytes
        var keyBytes = new byte[32];
        var encoded = new byte[] { 0xFF, 0x01 }.Concat(keyBytes).ToArray();
        var multibase = "z" + Base58.Bitcoin.Encode(encoded);
        var did = $"did:key:{multibase}";

        var doc = await _resolver.ResolveAsync(did);
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_Ed25519WrongKeyLength_ReturnsNull()
    {
        // ED25519 multicodec but only 16 bytes of key (should be 32)
        var shortKey = new byte[16];
        var encoded = new byte[] { 0xed, 0x01 }.Concat(shortKey).ToArray();
        var multibase = "z" + Base58.Bitcoin.Encode(encoded);
        var did = $"did:key:{multibase}";

        var doc = await _resolver.ResolveAsync(did);
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NoNetworkCall_SynchronousExecution()
    {
        // Verify that did:key resolution is purely local (no async I/O)
        var keyBytes = new byte[32];
        var encoded = new byte[] { 0xed, 0x01 }.Concat(keyBytes).ToArray();
        var multibase = "z" + Base58.Bitcoin.Encode(encoded);
        var did = $"did:key:{multibase}";

        // Should complete without any await (Task.FromResult)
        var task = _resolver.ResolveAsync(did);
        task.IsCompletedSuccessfully.Should().BeTrue("did:key resolution requires no network calls");
    }
}
