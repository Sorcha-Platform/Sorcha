// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Sorcha.Verifier.Engine;
using Xunit;

namespace Sorcha.Verifier.Tests;

/// <summary>US1 — the ES256K path through the verifier engine's JWS signature verification.</summary>
public class VerifiablePresentationValidatorEs256kTests
{
    [Fact]
    public void VerifyJwsSignature_ValidEs256k_ReturnsTrue()
    {
        var (priv, jwk) = NewSecp256k1Jwk();
        var jws = BuildEs256kJws(priv, """{"hello":"sorcha"}""");

        VerifiablePresentationValidator.VerifyJwsSignature(jws, jwk, out var payload).Should().BeTrue();
        payload.GetProperty("hello").GetString().Should().Be("sorcha");
    }

    [Fact]
    public void VerifyJwsSignature_TamperedEs256k_ReturnsFalse()
    {
        var (priv, jwk) = NewSecp256k1Jwk();
        var jws = BuildEs256kJws(priv, """{"a":"b"}""");

        var segments = jws.Split('.');
        var signature = Base64Url.DecodeFromChars(segments[2]);
        signature[3] ^= 0xFF;
        segments[2] = Base64Url.EncodeToString(signature);

        VerifiablePresentationValidator.VerifyJwsSignature(string.Join('.', segments), jwk, out _).Should().BeFalse();
    }

    [Fact]
    public void VerifyJwsSignature_Es256kAgainstWrongKey_ReturnsFalse()
    {
        var (priv, _) = NewSecp256k1Jwk();
        var (_, otherJwk) = NewSecp256k1Jwk();
        var jws = BuildEs256kJws(priv, """{"a":"b"}""");

        VerifiablePresentationValidator.VerifyJwsSignature(jws, otherJwk, out _).Should().BeFalse();
    }

    private static (ECPrivateKeyParameters Priv, JsonElement Jwk) NewSecp256k1Jwk()
    {
        var curve = SecNamedCurves.GetByName("secp256k1");
        var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var pub = (ECPublicKeyParameters)pair.Public;
        var q = pub.Q.Normalize();

        var x = Base64Url.EncodeToString(q.AffineXCoord.GetEncoded());
        var y = Base64Url.EncodeToString(q.AffineYCoord.GetEncoded());
        var jwk = JsonDocument.Parse($$"""{"kty":"EC","crv":"secp256k1","x":"{{x}}","y":"{{y}}"}""").RootElement.Clone();
        return ((ECPrivateKeyParameters)pair.Private, jwk);
    }

    private static string BuildEs256kJws(ECPrivateKeyParameters priv, string payloadJson)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"ES256K"}"""));
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{header}.{payload}";
        var signature = SignEs256k(Encoding.ASCII.GetBytes(signingInput), priv);
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
