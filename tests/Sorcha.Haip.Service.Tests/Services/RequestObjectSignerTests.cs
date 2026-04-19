// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Feature 094/098 follow-up — verifies that <see cref="RequestObjectSigner"/>
/// produces a compact JWT matching the RFC 9101 contract for JWT-Secured
/// Authorization Requests.
/// </summary>
public class RequestObjectSignerTests
{
    [Fact]
    public void Sign_ES256_ProducesJwtWithRfc9101TypAndVerifiableSignature()
    {
        // Arrange: a configured P-256 signing key and a sample Request Object payload.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyBase64 = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
        var signer = BuildSigner(new()
        {
            ["Haip:IssuerSigningKey"] = keyBase64,
            ["Haip:IssuerSigningAlgorithm"] = "ES256",
        });

        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://verifier.example/haip",
            ["aud"] = "https://self-issued.me/v2",
            ["response_type"] = "vp_token",
            ["nonce"] = "n-0S6_WzA2Mj",
            ["state"] = Guid.NewGuid().ToString(),
        };

        // Act
        var jwt = signer.Sign(payload);

        // Assert: shape
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3, "compact JWS has 3 segments");

        // Header MUST carry typ=oauth-authz-req+jwt and alg=ES256 per RFC 9101 §4.
        var headerJson = Base64Url.DecodeFromChars(parts[0]);
        using var header = JsonDocument.Parse(headerJson);
        header.RootElement.GetProperty("typ").GetString().Should().Be("oauth-authz-req+jwt");
        header.RootElement.GetProperty("alg").GetString().Should().Be("ES256");

        // JWK embedded so wallets can self-resolve the verifier key pre-x5c (spec 096).
        header.RootElement.TryGetProperty("jwk", out var jwk).Should().BeTrue();
        jwk.GetProperty("kty").GetString().Should().Be("EC");
        jwk.GetProperty("crv").GetString().Should().Be("P-256");

        // Signature must verify against the configured public key.
        using var verify = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        verify.ImportECPrivateKey(Convert.FromBase64String(keyBase64), out _);
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        verify.VerifyData(
            signingInput, signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue("the JWT must verify under its own embedded public key");
    }

    [Fact]
    public void Sign_ES256_NoConfiguredKey_UsesEphemeralKeyStillVerifiesUnderEmbeddedJwk()
    {
        // When Haip:IssuerSigningKey is absent the signer falls back to an ephemeral key.
        // Dev convenience only, but the signature MUST still verify under the jwk it embeds.
        var signer = BuildSigner(new() { ["Haip:IssuerSigningAlgorithm"] = "ES256" });

        var jwt = signer.Sign(new Dictionary<string, object>
        {
            ["nonce"] = "ephemeral-key-test",
        });

        var parts = jwt.Split('.');
        var headerJson = Base64Url.DecodeFromChars(parts[0]);
        using var header = JsonDocument.Parse(headerJson);
        var jwk = header.RootElement.GetProperty("jwk");

        using var verify = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()!),
                Y = Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString()!),
            },
        });
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        verify.VerifyData(
            signingInput, signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue();
    }

    [Fact]
    public void Sign_EdDSA_ProducesJwtWithOkpJwkAndVerifiableSignature()
    {
        var keyPair = Sodium.PublicKeyAuth.GenerateKeyPair();
        var signer = BuildSigner(new()
        {
            ["Haip:IssuerSigningKey"] = Convert.ToBase64String(keyPair.PrivateKey),
            ["Haip:IssuerSigningAlgorithm"] = "EdDSA",
        });

        var jwt = signer.Sign(new Dictionary<string, object> { ["nonce"] = "ed-test" });

        var parts = jwt.Split('.');
        var headerJson = Base64Url.DecodeFromChars(parts[0]);
        using var header = JsonDocument.Parse(headerJson);
        header.RootElement.GetProperty("alg").GetString().Should().Be("EdDSA");
        header.RootElement.GetProperty("typ").GetString().Should().Be("oauth-authz-req+jwt");

        var jwk = header.RootElement.GetProperty("jwk");
        jwk.GetProperty("kty").GetString().Should().Be("OKP");
        jwk.GetProperty("crv").GetString().Should().Be("Ed25519");

        var publicKey = Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()!);
        publicKey.Should().BeEquivalentTo(keyPair.PublicKey);

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        Sodium.PublicKeyAuth.VerifyDetached(signature, signingInput, publicKey)
            .Should().BeTrue();
    }

    [Fact]
    public void Sign_PayloadRoundTripsThroughBase64UrlDecoding()
    {
        var signer = BuildSigner(new());

        var claims = new Dictionary<string, object>
        {
            ["nonce"] = "roundtrip-payload",
            ["response_type"] = "vp_token",
            ["state"] = "abc-123",
        };

        var jwt = signer.Sign(claims);
        var parts = jwt.Split('.');

        var payloadJson = Base64Url.DecodeFromChars(parts[1]);
        using var doc = JsonDocument.Parse(payloadJson);
        doc.RootElement.GetProperty("nonce").GetString().Should().Be("roundtrip-payload");
        doc.RootElement.GetProperty("response_type").GetString().Should().Be("vp_token");
        doc.RootElement.GetProperty("state").GetString().Should().Be("abc-123");
    }

    [Fact]
    public void Sign_EdDSA_RejectsWrongKeyLength()
    {
        // 16 bytes is neither a valid Ed25519 seed (32B) nor a secret key (64B).
        var signer = BuildSigner(new()
        {
            ["Haip:IssuerSigningKey"] = Convert.ToBase64String(new byte[16]),
            ["Haip:IssuerSigningAlgorithm"] = "EdDSA",
        });

        var act = () => signer.Sign(new Dictionary<string, object> { ["nonce"] = "bad" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32-byte seed or 64-byte secret key*");
    }

    private static RequestObjectSigner BuildSigner(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new RequestObjectSigner(config, NullLogger<RequestObjectSigner>.Instance);
    }
}
