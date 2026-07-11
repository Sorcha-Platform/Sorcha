// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Sorcha.Blueprint.Engine.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Tests for BitstringStatusListChecker — W3C Bitstring Status List fetching, decoding, and bit checking.
/// </summary>
public class BitstringStatusListCheckerTests
{
    [Fact]
    public void TryParseStatusReference_ValidReference_ReturnsTrueAndParsesComponents()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference(
            "https://example.com/status/1#42", out var url, out var index);

        result.Should().BeTrue();
        url.Should().Be("https://example.com/status/1");
        index.Should().Be(42);
    }

    [Fact]
    public void TryParseStatusReference_NoHashSign_ReturnsFalse()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference(
            "https://example.com/status/1", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStatusReference_EmptyString_ReturnsFalse()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference("", out _, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStatusReference_HashAtEnd_ReturnsFalse()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference(
            "https://example.com/status/1#", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStatusReference_NonNumericIndex_ReturnsFalse()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference(
            "https://example.com/status/1#abc", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseStatusReference_NegativeIndex_ReturnsFalse()
    {
        var result = BitstringStatusListChecker.TryParseStatusReference(
            "https://example.com/status/1#-1", out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckBitAsync_BitSet_ReturnsRevoked()
    {
        // Create a bitstring with bit 0 set (MSB-first: byte[0] bit 7)
        var bytes = new byte[] { 0b10000000 }; // bit 0 set
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Revoked");
    }

    [Fact]
    public async Task CheckBitAsync_BitNotSet_ReturnsActive()
    {
        var bytes = new byte[] { 0b00000000 }; // no bits set
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Active");
    }

    [Fact]
    public async Task CheckBitAsync_SuspensionPurpose_BitSet_ReturnsSuspended()
    {
        var bytes = new byte[] { 0b10000000 }; // bit 0 set
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "suspension"));

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Suspended");
    }

    [Fact]
    public async Task CheckBitAsync_ChecksCorrectBitIndex()
    {
        // bit 5 = byte[0], bit position 2 (7-5=2), so byte value = 0b00000100
        var bytes = new byte[] { 0b00000100 };
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var resultBit5 = await checker.CheckBitAsync("https://example.com/status/1", 5);
        resultBit5.Should().Be("Revoked");

        // bit 0 is not set
        checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));
        var resultBit0 = await checker.CheckBitAsync("https://example.com/status/1", 0);
        resultBit0.Should().Be("Active");
    }

    [Fact]
    public async Task CheckBitAsync_MultiByte_ChecksCorrectByteAndBit()
    {
        // bit 8 = byte[1], bit position 7 (7-0=7), so byte[1] = 0b10000000
        var bytes = new byte[] { 0x00, 0b10000000 };
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var result = await checker.CheckBitAsync("https://example.com/status/1", 8);

        result.Should().Be("Revoked");
    }

    [Fact]
    public async Task CheckBitAsync_IndexOutOfRange_ReturnsNull()
    {
        var bytes = new byte[] { 0xFF };
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var result = await checker.CheckBitAsync("https://example.com/status/1", 100);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckBitAsync_InvalidJson_ReturnsNull()
    {
        var checker = CreateCheckerWithResponse("not valid json");

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckBitAsync_MissingCredentialSubject_ReturnsNull()
    {
        var json = JsonSerializer.Serialize(new { type = "StatusListCredential" });
        var checker = CreateCheckerWithResponse(json);

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckRevocationStatusAsync_ValidReference_ChecksBit()
    {
        var bytes = new byte[] { 0b10000000 }; // bit 0 set
        var checker = CreateCheckerWithResponse(
            CreateStatusListJson(bytes, "revocation"));

        var result = await checker.CheckRevocationStatusAsync(
            "https://example.com/status/1#0", "issuer-wallet");

        result.Should().Be("Revoked");
    }

    [Fact]
    public async Task CheckRevocationStatusAsync_InvalidReference_ReturnsNull()
    {
        var checker = CreateCheckerWithResponse("");

        var result = await checker.CheckRevocationStatusAsync(
            "no-hash-no-index", "issuer-wallet");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckBitAsync_NoPurposeField_DefaultsToRevocation()
    {
        var bytes = new byte[] { 0b10000000 };
        // Create status list without statusPurpose field
        var encodedList = GZipAndBase64Encode(bytes);
        var json = JsonSerializer.Serialize(new
        {
            credentialSubject = new
            {
                encodedList
            }
        });
        var checker = CreateCheckerWithResponse(json);

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Revoked");
    }

    private static BitstringStatusListChecker CreateCheckerWithResponse(string responseContent)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object);
        return new BitstringStatusListChecker(httpClient);
    }

    // Feature 181 US3 (T032 / R7) — the W3C Bitstring Status List v1 canonical encodedList form is
    // multibase base64url (leading 'u', unpadded). Sorcha issuance still emits plain base64; both must
    // decode so external EUDI status lists verify.

    [Fact]
    public async Task CheckBitAsync_MultibaseBase64UrlEncodedList_BitSet_ReturnsRevoked()
    {
        var bytes = new byte[] { 0b10000000 }; // bit 0 set (MSB-first)
        var json = CreateStatusListJson(bytes, "revocation", multibaseBase64Url: true);
        var checker = CreateCheckerWithResponse(json);

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Revoked");
    }

    [Fact]
    public async Task CheckBitAsync_MultibaseBase64UrlEncodedList_BitNotSet_ReturnsActive()
    {
        var bytes = new byte[] { 0b00000000 };
        var json = CreateStatusListJson(bytes, "revocation", multibaseBase64Url: true);
        var checker = CreateCheckerWithResponse(json);

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Active");
    }

    [Fact]
    public async Task CheckBitAsync_PlainBase64EncodedList_StillDecodes()
    {
        // Regression guard — the pre-v1 plain-base64 form must keep working alongside multibase.
        var bytes = new byte[] { 0b10000000 };
        var json = CreateStatusListJson(bytes, "revocation", multibaseBase64Url: false);
        var checker = CreateCheckerWithResponse(json);

        var result = await checker.CheckBitAsync("https://example.com/status/1", 0);

        result.Should().Be("Revoked");
    }

    private static string CreateStatusListJson(byte[] bitstringBytes, string purpose, bool multibaseBase64Url = false)
    {
        var encodedList = multibaseBase64Url
            ? "u" + GZipAndBase64UrlEncode(bitstringBytes)
            : GZipAndBase64Encode(bitstringBytes);

        return JsonSerializer.Serialize(new
        {
            credentialSubject = new
            {
                statusPurpose = purpose,
                encodedList
            }
        });
    }

    private static string GZipAndBase64UrlEncode(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return System.Buffers.Text.Base64Url.EncodeToString(output.ToArray());
    }

    private static string GZipAndBase64Encode(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }
}
