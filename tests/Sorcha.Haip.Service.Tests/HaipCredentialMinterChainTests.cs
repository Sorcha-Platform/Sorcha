// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests;

/// <summary>
/// Feature 135 / T053 — the HAIP minter attaches the x5c chain when one is supplied (x509-tenant
/// anchor) and omits it otherwise (register anchor). Previously the chain was hardcoded to null /
/// dropped on the sign-on-behalf path.
/// </summary>
public class HaipCredentialMinterChainTests
{
    private readonly HaipCredentialMinter _minter = new(new SdJwtService(), Mock.Of<ILogger<HaipCredentialMinter>>());

    private static JsonElement HolderJwk()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(false);
        string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC", ["crv"] = "P-256", ["x"] = B64Url(p.Q.X!), ["y"] = B64Url(p.Q.Y!)
        });
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static (byte[] signingKey, byte[] certDer) NewIssuer()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = new CertificateRequest("CN=Issuer", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return (key.ExportECPrivateKey(), cert.Export(X509ContentType.Cert));
    }

    private static JsonElement Header(string sdJwt)
    {
        var jwt = sdJwt.TrimEnd('~').Split('~')[0];
        return JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(jwt.Split('.')[0]));
    }

    [Fact]
    public async Task Mint_WithX5cChain_AttachesX5cHeader()
    {
        var (signingKey, certDer) = NewIssuer();

        var token = await _minter.MintCredentialAsync(
            "did:sorcha:org:gov", HolderJwk(), "AssuredIdentity",
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosablePaths: ["name"],
            signingKey, "ES256", expiresAt: null, ct: default, kid: null, x5cChain: [certDer]);

        var header = Header(token);
        header.TryGetProperty("x5c", out var x5c).Should().BeTrue();
        x5c.EnumerateArray().First().GetString().Should().Be(Convert.ToBase64String(certDer));
    }

    [Fact]
    public async Task Mint_WithoutX5cChain_OmitsX5cHeader()
    {
        var (signingKey, _) = NewIssuer();

        var token = await _minter.MintCredentialAsync(
            "did:sorcha:org:gov", HolderJwk(), "AssuredIdentity",
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosablePaths: ["name"],
            signingKey, "ES256");

        Header(token).TryGetProperty("x5c", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Mint_ExternalSigner_WithX5cChain_AttachesX5cHeader()
    {
        var (signingKey, certDer) = NewIssuer();
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(signingKey, out _);

        var token = await _minter.MintCredentialWithExternalSignerAsync(
            "did:sorcha:org:gov", HolderJwk(), "AssuredIdentity",
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosablePaths: ["name"],
            externalSigner: (data, _) => Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256)),
            algorithm: "ES256", kid: "did:sorcha:org:gov#vc-issuance-1",
            expiresAt: null, ct: default, x5cChain: [certDer]);

        Header(token).TryGetProperty("x5c", out _).Should().BeTrue();
    }
}
