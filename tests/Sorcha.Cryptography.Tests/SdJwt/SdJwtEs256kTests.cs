// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>US1 — the ES256K issuer-signature path through SdJwtService.VerifyTokenAsync.</summary>
public class SdJwtEs256kTests
{
    [Fact]
    public async Task VerifyToken_ValidEs256kIssuerSignature_Succeeds()
    {
        var (priv, sec1Pub) = NewSecp256k1();
        var token = BuildEs256kSdJwt(priv, """{"iss":"did:key:zQ3s-test","vct":"https://sorcha.dev/vc/test/v1"}""");

        var result = await new SdJwtService().VerifyTokenAsync(token, sec1Pub, "ES256K");

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyToken_TamperedEs256kSignature_Fails()
    {
        var (priv, sec1Pub) = NewSecp256k1();
        var token = BuildEs256kSdJwt(priv, """{"iss":"x"}""");

        var segments = token.Split('.');
        var signature = Base64Url.DecodeFromChars(segments[2]);
        signature[5] ^= 0xFF;
        segments[2] = Base64Url.EncodeToString(signature);
        var tampered = string.Join('.', segments);

        var result = await new SdJwtService().VerifyTokenAsync(tampered, sec1Pub, "ES256K");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task VerifyToken_Es256kSignatureVerifiedAgainstWrongKey_Fails()
    {
        var (priv, _) = NewSecp256k1();
        var (_, otherPub) = NewSecp256k1();
        var token = BuildEs256kSdJwt(priv, """{"iss":"x"}""");

        var result = await new SdJwtService().VerifyTokenAsync(token, otherPub, "ES256K");

        result.IsValid.Should().BeFalse();
    }

    private static (ECPrivateKeyParameters Priv, byte[] Sec1Pub) NewSecp256k1()
    {
        var curve = SecNamedCurves.GetByName("secp256k1");
        var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var pub = (ECPublicKeyParameters)pair.Public;
        return ((ECPrivateKeyParameters)pair.Private, pub.Q.GetEncoded(false)); // 65-byte SEC1
    }

    private static string BuildEs256kSdJwt(ECPrivateKeyParameters priv, string payloadJson)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"ES256K","typ":"dc+sd-jwt"}"""));
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{header}.{payload}";
        var signature = SignEs256k(Encoding.UTF8.GetBytes(signingInput), priv);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    private static byte[] SignEs256k(byte[] message, ECPrivateKeyParameters priv)
    {
        var hash = SHA256.HashData(message);
        var signer = new ECDsaSigner();
        signer.Init(true, priv);
        var rs = signer.GenerateSignature(hash);
        return [.. Fixed32(rs[0]), .. Fixed32(rs[1])];
    }

    private static byte[] Fixed32(BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length == 32)
        {
            return bytes;
        }

        var result = new byte[32];
        Array.Copy(bytes, 0, result, 32 - bytes.Length, bytes.Length);
        return result;
    }
}
