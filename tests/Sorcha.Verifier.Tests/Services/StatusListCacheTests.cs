// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.Verifier.Services;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Tests for <see cref="StatusListCache"/> (Feature 114, T072). Verifies the
/// JWT parse path, bit lookup, freshness reuse, stale-fallback on fetch
/// failure, and out-of-range index handling.
/// </summary>
public sealed class StatusListCacheTests
{
    private const string ListUri = "https://verify.test/api/v1/wallet/status/abc/citizen-devices/0.statuslist+jwt";

    [Fact]
    public void ParseJwt_ValidPayload_DecodesBitstring()
    {
        var bits = new byte[32];
        bits[5] = 0b0000_0010; // index 41 set (5*8 + 1)
        var jwt = BuildStatusListJwt(bits, expiresAt: DateTimeOffset.UtcNow.AddHours(24));

        var entry = StatusListCache.ParseJwt(jwt);

        entry.Bitstring.Length.Should().Be(32);
        entry.Bitstring[5].Should().Be(0b0000_0010);
        entry.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task IsRevokedAsync_BitSet_ReturnsTrue()
    {
        var bits = new byte[32];
        bits[1] = 0b0000_1000; // index 11 set
        var cache = BuildCache(BuildStatusListJwt(bits, DateTimeOffset.UtcNow.AddHours(24)));

        (await cache.IsRevokedAsync(ListUri, 11)).Should().BeTrue();
        (await cache.IsRevokedAsync(ListUri, 10)).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_OutOfRange_ReturnsFalseAndLogs()
    {
        var cache = BuildCache(BuildStatusListJwt(new byte[32], DateTimeOffset.UtcNow.AddHours(24)));

        (await cache.IsRevokedAsync(ListUri, 32 * 8)).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_FetchFailure_NoCachedEntry_ReturnsFalse()
    {
        var cache = BuildCache(httpStatus: HttpStatusCode.InternalServerError);

        (await cache.IsRevokedAsync(ListUri, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task IsRevokedAsync_SecondCallReusesCache_NoSecondHttpHit()
    {
        var bits = new byte[32];
        bits[0] = 0b0000_0001;
        var fetchCount = 0;
        var cache = BuildCache(
            BuildStatusListJwt(bits, DateTimeOffset.UtcNow.AddHours(24)),
            onRequest: _ => Interlocked.Increment(ref fetchCount));

        await cache.IsRevokedAsync(ListUri, 0);
        await cache.IsRevokedAsync(ListUri, 0);
        await cache.IsRevokedAsync(ListUri, 0);

        fetchCount.Should().Be(1);
    }

    [Fact]
    public async Task IsRevokedAsync_StaleEntryServedWhenFreshFetchFails()
    {
        var bits = new byte[32];
        bits[0] = 0b0000_0010; // bit 1 set
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildStatusListJwt(bits, DateTimeOffset.UtcNow.AddSeconds(-1)))
        });
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cache = BuildCacheFromResponses(responses);

        // First call seeds the cache (with already-stale entry — exp in the past).
        (await cache.IsRevokedAsync(ListUri, 1)).Should().BeTrue();
        // Second call: cache is stale → fresh fetch attempted → fails → falls back to stale.
        (await cache.IsRevokedAsync(ListUri, 1)).Should().BeTrue();
    }

    private static string BuildStatusListJwt(byte[] bits, DateTimeOffset expiresAt)
    {
        using var ms = new MemoryStream();
        using (var compressor = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(bits, 0, bits.Length);
        }
        var compressed = ms.ToArray();

        var header = new { alg = "ES256", typ = "statuslist+jwt" };
        var payload = new
        {
            iss = "did:sorcha:org:test",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds(),
            sub = ListUri,
            status_list = new { bits = 1, lst = Base64Url.EncodeToString(compressed) }
        };

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        // Signature ignored by ParseJwt — we don't verify here, only structurally parse.
        return $"{headerB64}.{payloadB64}.fakesig";
    }

    private static StatusListCache BuildCache(
        string body,
        Action<HttpRequestMessage>? onRequest = null)
    {
        return BuildCacheFromHandler(HttpStatusCode.OK, body, onRequest);
    }

    private static StatusListCache BuildCache(HttpStatusCode httpStatus)
    {
        return BuildCacheFromHandler(httpStatus, body: "", onRequest: null);
    }

    private static StatusListCache BuildCacheFromHandler(
        HttpStatusCode status,
        string body,
        Action<HttpRequestMessage>? onRequest)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                onRequest?.Invoke(req);
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/jwt")
                });
            });

        var http = new HttpClient(handler.Object);
        return new StatusListCache(http, NullLogger<StatusListCache>.Instance);
    }

    private static StatusListCache BuildCacheFromResponses(Queue<HttpResponseMessage> responses)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
                Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var http = new HttpClient(handler.Object);
        return new StatusListCache(http, NullLogger<StatusListCache>.Instance);
    }
}
