// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.Verifier.Engine;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Shared harness for the Feature 138 US1 status-list verification tests. Builds genuinely
/// ES256-signed Token Status List JWTs (matching <c>CitizenStatusListPublisher</c>'s
/// <c>ecdsa.SignData</c> IEEE-P1363 output), the matching public JWK, and a configurable
/// <see cref="IIssuerKeyResolver"/> + <see cref="StatusListCache"/> wired to a mock transport.
/// </summary>
internal static class StatusListTestHelpers
{
    public const string Issuer = "did:sorcha:org:abc";
    public const string ListUri = "https://verify.test/api/v1/wallet/status/abc/citizen-devices/0.statuslist+jwt";

    /// <summary>A signing key plus the matching public JWK extracted from it.</summary>
    public sealed record SigningKey(ECDsa Ecdsa, JsonElement PublicJwk);

    /// <summary>Creates a fresh ES256 (P-256) key and its public JWK.</summary>
    public static SigningKey NewKey()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        var jwk = JsonSerializer.SerializeToElement(new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.EncodeToString(p.Q.X!),
            y = Base64Url.EncodeToString(p.Q.Y!),
        });
        return new SigningKey(ecdsa, jwk);
    }

    /// <summary>
    /// Builds a status-list JWT. When <paramref name="signingKey"/> is null a fresh key is used.
    /// <paramref name="corruptSignature"/> flips the signature bytes (forged list); <paramref name="alg"/>
    /// overrides the header alg (e.g. to test an unsupported algorithm).
    /// </summary>
    public static string BuildSignedList(
        byte[] bits,
        DateTimeOffset exp,
        SigningKey signingKey,
        string issuer = Issuer,
        string? kid = "did:sorcha:org:abc#citizen-status-signing",
        bool corruptSignature = false,
        bool omitExp = false,
        string alg = "ES256")
    {
        using var ms = new MemoryStream();
        using (var compressor = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(bits, 0, bits.Length);
        }
        var compressed = ms.ToArray();

        object header = kid is null
            ? new { alg, typ = "statuslist+jwt" }
            : new { alg, kid, typ = "statuslist+jwt" };

        var payload = omitExp
            ? (object)new
            {
                iss = issuer,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                sub = ListUri,
                status_list = new { bits = 1, lst = Base64Url.EncodeToString(compressed) },
            }
            : new
            {
                iss = issuer,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = exp.ToUnixTimeSeconds(),
                sub = ListUri,
                status_list = new { bits = 1, lst = Base64Url.EncodeToString(compressed) },
            };

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");

        // Matches the publisher: raw ECDsa.SignData → IEEE P1363 concatenated (r‖s).
        var signature = signingKey.Ecdsa.SignData(signingInput, HashAlgorithmName.SHA256);
        if (corruptSignature)
        {
            signature[0] ^= 0xFF;
            signature[^1] ^= 0xFF;
        }

        return $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>A stub resolver that returns <paramref name="jwk"/> for <paramref name="issuer"/>, else null.</summary>
    public static IIssuerKeyResolver ResolverFor(JsonElement jwk, string issuer = Issuer)
        => new StubResolver(jwk, issuer);

    /// <summary>A resolver that always returns null (no key available).</summary>
    public static IIssuerKeyResolver NullResolver() => new StubResolver(null, Issuer);

    /// <summary>Builds a <see cref="StatusListCache"/> serving <paramref name="body"/> over a mock transport.</summary>
    public static StatusListCache BuildCache(
        string body,
        IIssuerKeyResolver resolver,
        TimeProvider clock,
        TimeSpan? skew = null,
        Action<HttpRequestMessage>? onRequest = null)
        => BuildCacheFromHandler(HttpStatusCode.OK, body, resolver, clock, skew, onRequest);

    /// <summary>Builds a <see cref="StatusListCache"/> whose transport returns <paramref name="status"/>.</summary>
    public static StatusListCache BuildCache(
        HttpStatusCode status,
        IIssuerKeyResolver resolver,
        TimeProvider clock,
        TimeSpan? skew = null)
        => BuildCacheFromHandler(status, body: string.Empty, resolver, clock, skew, onRequest: null);

    private static StatusListCache BuildCacheFromHandler(
        HttpStatusCode status,
        string body,
        IIssuerKeyResolver resolver,
        TimeProvider clock,
        TimeSpan? skew,
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
                    Content = new StringContent(body, Encoding.UTF8, "application/statuslist+jwt"),
                });
            });

        var http = new HttpClient(handler.Object);
        return new StatusListCache(http, resolver, clock, NullLogger<StatusListCache>.Instance, metrics: null, skew);
    }

    /// <summary>Builds a cache that serves a queue of responses (for stale/fail-closed sequencing).</summary>
    public static StatusListCache BuildCacheFromResponses(
        Queue<HttpResponseMessage> responses,
        IIssuerKeyResolver resolver,
        TimeProvider clock,
        TimeSpan? skew = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
                Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var http = new HttpClient(handler.Object);
        return new StatusListCache(http, resolver, clock, NullLogger<StatusListCache>.Instance, metrics: null, skew);
    }

    private sealed class StubResolver : IIssuerKeyResolver
    {
        private readonly JsonElement? _jwk;
        private readonly string _issuer;

        public StubResolver(JsonElement? jwk, string issuer)
        {
            _jwk = jwk;
            _issuer = issuer;
        }

        public Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default)
            => Task.FromResult(string.Equals(issuer, _issuer, StringComparison.Ordinal) ? _jwk : null);

        public Task<JsonElement?> ResolveAsync(string issuer, string? kid, CancellationToken ct = default)
            => ResolveAsync(issuer, ct);
    }
}

/// <summary>Minimal deterministic <see cref="TimeProvider"/> for freshness tests (no package dependency).</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
