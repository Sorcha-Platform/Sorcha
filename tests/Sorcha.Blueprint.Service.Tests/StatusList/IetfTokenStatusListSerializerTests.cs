// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Service.Services;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

/// <summary>
/// Tests for the IETF Token Status List serializer (FR-001 through FR-004).
/// </summary>
public class IetfTokenStatusListSerializerTests
{
    private readonly IetfTokenStatusListSerializer _serializer = new();

    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Serialize_ReturnsSignedJwt_WithStatusListPayload()
    {
        // Arrange
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var rawBitstring = new byte[131072 / 8]; // 131K bits, all zeros

        // Act
        var jwt = _serializer.Serialize(
            rawBitstring, "list-001", "did:sorcha:org:issuer1",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256");

        // Assert — must be a valid JWT (3 dot-separated segments)
        jwt.Should().NotBeNullOrWhiteSpace();
        var segments = jwt.Split('.');
        segments.Should().HaveCount(3);

        // Parse payload
        var payloadBytes = Base64Url.DecodeFromChars(segments[1]);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadBytes)!;

        payload.Should().ContainKey("iss");
        payload["iss"].GetString().Should().Be("did:sorcha:org:issuer1");
        payload.Should().ContainKey("sub");
        payload["sub"].GetString().Should().Be("list-001");
        payload.Should().ContainKey("iat");
        payload.Should().ContainKey("exp");
        payload.Should().ContainKey("status_list");

        var statusList = payload["status_list"];
        statusList.TryGetProperty("bits", out var bits).Should().BeTrue();
        bits.GetInt32().Should().Be(1);
        statusList.TryGetProperty("lst", out var lst).Should().BeTrue();
        lst.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Serialize_JwtHeader_HasStatuslistPlusJwtType()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var rawBitstring = new byte[131072 / 8];

        var jwt = _serializer.Serialize(
            rawBitstring, "list-001", "did:sorcha:org:issuer1",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256");

        var headerBytes = Base64Url.DecodeFromChars(jwt.Split('.')[0]);
        var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(headerBytes)!;

        header["typ"].GetString().Should().Be("statuslist+jwt");
        header["alg"].GetString().Should().Be("ES256");
    }

    [Fact]
    public void Serialize_LstField_IsZlibCompressedBase64Url()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var rawBitstring = new byte[131072 / 8];
        rawBitstring[0] = 0b10000000; // Set bit 0 (MSB)

        var jwt = _serializer.Serialize(
            rawBitstring, "list-001", "did:sorcha:org:issuer1",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256");

        // Extract lst from payload
        var payloadBytes = Base64Url.DecodeFromChars(jwt.Split('.')[1]);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadBytes)!;
        var lst = payload["status_list"].GetProperty("lst").GetString()!;

        // Decompress and verify
        var compressed = Base64Url.DecodeFromChars(lst);
        var decompressed = IetfTokenStatusListSerializer.ZLibDecompress(compressed);
        decompressed.Should().HaveCount(131072 / 8);
        decompressed[0].Should().Be(0b10000000, "bit 0 should be set");
    }

    [Fact]
    public void Serialize_SignatureVerifies_WithPublicKey()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var rawBitstring = new byte[131072 / 8];

        var jwt = _serializer.Serialize(
            rawBitstring, "list-001", "did:sorcha:org:issuer1",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256");

        var segments = jwt.Split('.');
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{segments[0]}.{segments[1]}");
        var signature = Base64Url.DecodeFromChars(segments[2]);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [Fact]
    public void Serialize_ExpiresAfterTtl()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var rawBitstring = new byte[131072 / 8];

        var jwt = _serializer.Serialize(
            rawBitstring, "list-001", "did:sorcha:org:issuer1",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256",
            ttlSeconds: 600);

        var payloadBytes = Base64Url.DecodeFromChars(jwt.Split('.')[1]);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadBytes)!;

        var iat = payload["iat"].GetInt64();
        var exp = payload["exp"].GetInt64();
        (exp - iat).Should().Be(600);
    }

    [Fact]
    public void ZLibCompress_Decompress_RoundTrips()
    {
        var original = new byte[16384];
        Random.Shared.NextBytes(original);

        var compressed = IetfTokenStatusListSerializer.ZLibCompress(original);
        var decompressed = IetfTokenStatusListSerializer.ZLibDecompress(compressed);

        decompressed.Should().BeEquivalentTo(original);
    }
}
