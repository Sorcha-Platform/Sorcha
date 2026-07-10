// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Cryptography.SdJwt;

using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// Feature 096 US3 — verifies <see cref="SdJwtService.CreateTokenAsync"/>
/// embeds the supplied X.509 chain in the JWS header's <c>x5c</c> array per
/// RFC 7515 §4.1.6. Downstream verifiers (HaipPresentationVerifier) walk this
/// chain against a trust anchor to resolve the issuer key without DID lookup.
/// </summary>
public class SdJwtX5cHeaderTests
{
    private readonly SdJwtService _service = new();

    [Fact]
    public async Task CreateTokenAsync_WithX5cChain_EmbedsChainInJwsHeader()
    {
        var (chain, signingKey) = BuildOrgChain();

        var token = await _service.CreateTokenAsync(
            claims: new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:alice",
            signingKey: signingKey,
            algorithm: "ES256",
            expiresAt: DateTimeOffset.UtcNow.AddDays(30),
            cancellationToken: default,
            x5cChain: chain);

        // Decode the header segment and assert x5c is an array of base64-encoded DER.
        var headerB64 = token.RawToken.Split('.')[0];
        var headerJson = Base64Url.DecodeFromChars(headerB64);
        using var doc = JsonDocument.Parse(headerJson);

        doc.RootElement.TryGetProperty("x5c", out var x5c).Should().BeTrue(
            "x5c header MUST be present when chain supplied");
        x5c.ValueKind.Should().Be(JsonValueKind.Array);
        x5c.GetArrayLength().Should().Be(chain.Count);

        // First entry must be the leaf cert (DER matches).
        var leafFromHeader = Convert.FromBase64String(x5c[0].GetString()!);
        leafFromHeader.Should().BeEquivalentTo(chain[0],
            "leaf cert in x5c must be byte-identical to the one passed in");

        // Header still carries the standard JWT claims.
        doc.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        doc.RootElement.GetProperty("typ").GetString().Should().Be("dc+sd-jwt");
    }

    [Fact]
    public async Task CreateTokenAsync_NullX5cChain_OmitsHeader()
    {
        // Default behaviour (pre-096) — no x5c header — MUST continue to hold so
        // issuer flows that don't have a cert chain yet keep working.
        var (_, signingKey) = BuildOrgChain();

        var token = await _service.CreateTokenAsync(
            claims: new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:alice",
            signingKey: signingKey,
            algorithm: "ES256");

        var headerJson = Base64Url.DecodeFromChars(token.RawToken.Split('.')[0]);
        using var doc = JsonDocument.Parse(headerJson);
        doc.RootElement.TryGetProperty("x5c", out _).Should().BeFalse(
            "x5c header MUST be absent when no chain supplied (spec 096 backward-compat)");
    }

    [Fact]
    public async Task CreateTokenAsync_EmptyX5cChain_OmitsHeader()
    {
        var (_, signingKey) = BuildOrgChain();

        var token = await _service.CreateTokenAsync(
            claims: new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:alice",
            signingKey: signingKey,
            algorithm: "ES256",
            expiresAt: null,
            cancellationToken: default,
            x5cChain: Array.Empty<byte[]>());

        var headerJson = Base64Url.DecodeFromChars(token.RawToken.Split('.')[0]);
        using var doc = JsonDocument.Parse(headerJson);
        doc.RootElement.TryGetProperty("x5c", out _).Should().BeFalse(
            "empty chain treated as 'no chain' — don't emit an empty array");
    }

    // --- helpers ---

    /// <summary>
    /// Builds a minimal two-cert chain: self-signed root CA + org leaf signed
    /// by it. Returns the chain (leaf first, root last) plus the org's signing
    /// key bytes so the test can create a token signed by the leaf.
    /// </summary>
    private static (IReadOnlyList<byte[]> Chain, byte[] SigningKey) BuildOrgChain()
    {
        using var rootEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest(
            new X500DistinguishedName("CN=Test Root CA"),
            rootEcdsa,
            HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, true, 1, true));
        rootRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var now = DateTimeOffset.UtcNow;
        using var rootCert = rootRequest.CreateSelfSigned(now, now.AddYears(5));

        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgRequest = new CertificateRequest(
            new X500DistinguishedName("CN=Test Org Leaf"),
            orgEcdsa,
            HashAlgorithmName.SHA256);
        orgRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        orgRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var orgCert = orgRequest.Create(
            rootCert.SubjectName,
            X509SignatureGenerator.CreateForECDsa(rootEcdsa),
            now, now.AddYears(2), serial);

        var chain = new List<byte[]> { orgCert.RawData, rootCert.RawData };
        return (chain, orgEcdsa.ExportECPrivateKey());
    }
}
