// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;

using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Feature 095 US4 — verifies <see cref="IetfTokenStatusListChecker"/> can round-
/// trip a signed envelope produced by the issuer-side
/// <see cref="Sorcha.Blueprint.Service.Services.IetfTokenStatusListSerializer"/>.
/// The signature verification closes the real security boundary — malicious
/// endpoints cannot fake a revocation state.
/// </summary>
public class IetfTokenStatusListCheckerTests
{
    [Fact]
    public void ParseAndReadBit_NotSet_WhenBitAtIdxIsZero()
    {
        // Single-bit list of 128 bits, all zero — every index should read NotSet.
        var jwt = BuildSignedEnvelope(new byte[16], bitsPerEntry: 1);

        var bit = IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 42);

        bit.Should().Be(CredentialStatusValue.Valid);
    }

    [Fact]
    public void ParseAndReadBit_Set_WhenBitAtIdxIsOne()
    {
        // Set the bit at index 42 to 1. MSB-first within a byte: bit 42 → byte 5, bit 2.
        var raw = new byte[16];
        SetBit(raw, 42);
        var jwt = BuildSignedEnvelope(raw, bitsPerEntry: 1);

        var bit = IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 42);

        bit.Should().Be(CredentialStatusValue.Invalid);
        IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 41).Should().Be(CredentialStatusValue.Valid);
        IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 43).Should().Be(CredentialStatusValue.Valid);
    }

    [Fact]
    public void ParseAndReadBit_Unknown_WhenSignatureInvalid()
    {
        var jwt = BuildSignedEnvelope(new byte[16], bitsPerEntry: 1);
        // Flip the last byte of the signature segment to invalidate it.
        var parts = jwt.Split('.');
        var sigBytes = Base64Url.DecodeFromChars(parts[2]);
        sigBytes[^1] ^= 0xFF;
        var tamperedJwt = $"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(sigBytes)}";

        var bit = IetfTokenStatusListChecker.ParseAndReadBit(tamperedJwt, idx: 0);

        bit.Should().Be(CredentialStatusValue.Unresolved,
            "a tampered signature MUST cause the status read to be treated as Unknown, never Active");
    }

    [Fact]
    public void ParseAndReadBit_Unknown_WhenTypHeaderWrong()
    {
        // Rebuild an envelope with typ="jwt" instead of statuslist+jwt — verifier
        // must refuse because it wasn't meant as a status list.
        var jwt = BuildSignedEnvelope(new byte[16], bitsPerEntry: 1, typ: "jwt");

        var bit = IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 0);

        bit.Should().Be(CredentialStatusValue.Unresolved);
    }

    [Fact]
    public void ParseAndReadBit_Unknown_WhenIdxOutOfRange()
    {
        var jwt = BuildSignedEnvelope(new byte[16], bitsPerEntry: 1);

        IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 128).Should().Be(CredentialStatusValue.Unresolved);
        IetfTokenStatusListChecker.ParseAndReadBit(jwt, idx: 10_000).Should().Be(CredentialStatusValue.Unresolved);
    }

    [Fact]
    public void ReadBit_TwoBitList_ReadsAcrossBoundary()
    {
        // 2-bit list: entry 0 occupies bits 0-1, entry 1 occupies 2-3, etc.
        // Entry 3 (bits 6-7) = 0b01 — IETF 0x01 INVALID, i.e. revoked.
        var raw = new byte[2];
        raw[0] = 0b0000_0001;

        IetfTokenStatusListChecker.ReadBit(raw, idx: 3, bitsPerEntry: 2)
            .Should().Be(CredentialStatusValue.Invalid);
        IetfTokenStatusListChecker.ReadBit(raw, idx: 0, bitsPerEntry: 2)
            .Should().Be(CredentialStatusValue.Valid);
    }

    // --- Test helpers ---

    private static void SetBit(byte[] raw, int idx)
    {
        var byteIdx = idx / 8;
        var bitIdx = idx % 8;
        raw[byteIdx] |= (byte)(1 << (7 - bitIdx));
    }

    private static string BuildSignedEnvelope(byte[] rawBitstring, int bitsPerEntry, string typ = "statuslist+jwt")
    {
        // Mirror what IetfTokenStatusListSerializer produces: zlib-compress the raw
        // bits, base64url, build header+payload, sign with ES256, embed the JWK so
        // the checker can self-resolve the key (dev phase; pre-x5c).
        var compressed = ZLibCompress(rawBitstring);
        var lst = Base64Url.EncodeToString(compressed);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = typ,
            ["jwk"] = new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64Url.EncodeToString(parameters.Q.X!),
                ["y"] = Base64Url.EncodeToString(parameters.Q.Y!),
            },
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object>
        {
            ["iss"] = "did:sorcha:test:issuer",
            ["sub"] = "https://test/list/1",
            ["iat"] = now,
            ["exp"] = now + 3600,
            ["status_list"] = new Dictionary<string, object>
            {
                ["bits"] = bitsPerEntry,
                ["lst"] = lst,
            },
        };

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        var signature = ecdsa.SignData(signingInput, HashAlgorithmName.SHA256);
        return $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(signature)}";
    }

    private static byte[] ZLibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
