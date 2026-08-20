// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for HaipCredentialMinter — SD-JWT VC minting with cnf binding.
/// </summary>
public class HaipCredentialMinterTests
{
    private readonly HaipCredentialMinter _minter;
    private readonly SdJwtService _sdJwtService = new();

    public HaipCredentialMinterTests()
    {
        _minter = new HaipCredentialMinter(
            _sdJwtService,
            Mock.Of<ILogger<HaipCredentialMinter>>());
    }

    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    private static JsonElement CreateHolderJwk(byte[] publicKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Convert.ToBase64String(parameters.Q.X!).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["y"] = Convert.ToBase64String(parameters.Q.Y!).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        };
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    [Fact]
    public async Task MintCredential_ReturnsSdJwtVcWithCnf()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object>
        {
            ["licenseType"] = "ClassA",
            ["holder"] = "Alice"
        };

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            holderJwk,
            "LicenseCredential",
            claims,
            disclosablePaths: ["licenseType", "holder"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        rawToken.Should().NotBeNullOrWhiteSpace();
        rawToken.Should().Contain("~"); // SD-JWT format

        // Verify the token has cnf
        var (_, issuerPublic) = GenerateP256KeyPair(); // Can't verify with this key, but parse
        // Parse the JWT payload to check cnf
        var parts = rawToken.TrimEnd('~').Split('~');
        var jwtParts = parts[0].Split('.');
        var payloadBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtParts[1]);
        var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);

        payload.TryGetProperty("cnf", out var cnf).Should().BeTrue("credential must have cnf");
        cnf.TryGetProperty("jwk", out _).Should().BeTrue("cnf must contain jwk");
    }

    [Fact]
    public async Task MintCredential_WithDisclosables_ProducesDisclosures()
    {
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["license"] = "A",
            ["publicField"] = "visible"
        };

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            holderJwk,
            "LicenseCredential",
            claims,
            disclosablePaths: ["name", "license"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        // Verify: should have disclosures
        var result = await _sdJwtService.VerifyTokenAsync(rawToken, issuerPublic, "ES256");
        result.IsValid.Should().BeTrue();
        result.Claims.Should().ContainKey("name");
        result.Claims.Should().ContainKey("license");
        result.Claims.Should().ContainKey("publicField");
        result.CnfJwk.Should().NotBeNull();
    }

    [Fact]
    public async Task MintCredential_FullRoundTrip_IssueAndVerify()
    {
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:gov",
            holderJwk,
            "ProfessionalLicense",
            new Dictionary<string, object>
            {
                ["name"] = "Alice O'Brien",
                ["licenseNumber"] = "LIC-12345",
                ["councilArea"] = "Dublin City"
            },
            disclosablePaths: ["name", "licenseNumber", "councilArea"],
            signingKey: issuerPrivate,
            algorithm: "ES256",
            expiresAt: DateTimeOffset.UtcNow.AddYears(1));

        // Verify
        var verifyResult = await _sdJwtService.VerifyTokenAsync(rawToken, issuerPublic, "ES256");
        verifyResult.IsValid.Should().BeTrue();
        verifyResult.Issuer.Should().Be("did:sorcha:org:gov");
        verifyResult.CnfJwk.Should().NotBeNull();
        verifyResult.ExpiresAt.Should().NotBeNull();

        // Create a presentation with KB-JWT
        int bytesRead;
        Func<byte[], CancellationToken, Task<byte[]>> signer = (data, _) =>
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ecdsa.ImportECPrivateKey(holderPrivate, out bytesRead);
            return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
        };

        var presentation = await _sdJwtService.CreatePresentationAsync(
            rawToken,
            claimsToDisclose: ["licenseNumber", "councilArea"],
            kbJwtSigner: signer,
            holderAlgorithm: "ES256",
            audience: "https://booking-platform.example.com",
            nonce: "verification-nonce-1");

        // Verify the presentation
        var presResult = await _sdJwtService.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://booking-platform.example.com",
            expectedNonce: "verification-nonce-1");

        presResult.IsValid.Should().BeTrue();
        presResult.HolderKeyVerified.Should().BeTrue();
        presResult.Claims.Should().ContainKey("licenseNumber");
        presResult.Claims.Should().ContainKey("councilArea");
        presResult.Claims.Should().NotContainKey("name"); // Not disclosed
    }
    // -------------------------------------------------------------------------
    // #1540 — the vct type claim. SD-JWT VC §3.2.2.1 makes vct the credential's
    // SOLE type claim and REQUIRES it; HAIP spent credentialType on the `sub`
    // string and a log line and wrote no type identifier at all, so no conformant
    // verifier could match the credential to a requested type.
    // -------------------------------------------------------------------------

    /// <summary>Decodes the issuer-signed JWT payload of an SD-JWT VC.</summary>
    private static JsonElement DecodePayload(string rawToken)
    {
        var jwt = rawToken.TrimEnd('~').Split('~')[0];
        var payloadBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwt.Split('.')[1]);
        return JsonSerializer.Deserialize<JsonElement>(payloadBytes);
    }

    /// <summary>The claim names carried by the token's disclosures.</summary>
    private static List<string> DisclosedNames(string rawToken)
    {
        var names = new List<string>();
        foreach (var d in rawToken.TrimEnd('~').Split('~').Skip(1))
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            var arr = JsonSerializer.Deserialize<JsonElement>(System.Buffers.Text.Base64Url.DecodeFromChars(d));
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 3)
            {
                names.Add(arr[1].GetString()!);
            }
        }
        return names;
    }

    [Fact]
    public async Task MintCredentialAsync_Always_WritesVctAsTopLevelClaim()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "CyberEssentialsUacPosture",
            new Dictionary<string, object> { ["compliant"] = true },
            disclosablePaths: ["compliant"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        var payload = DecodePayload(rawToken);

        payload.TryGetProperty("vct", out var vct).Should().BeTrue(
            "vct is REQUIRED by SD-JWT VC and is the credential's only type identifier");
        vct.GetString().Should().Be("CyberEssentialsUacPosture");
    }

    [Fact]
    public async Task MintCredentialAsync_VctListedAsDisclosable_KeepsVctInTheClear()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();

        // A holder able to withhold the type identifier would present a credential
        // indistinguishable from the untyped ones this fix exists to stop.
        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "LicenseCredential",
            new Dictionary<string, object> { ["holder"] = "Alice" },
            disclosablePaths: ["vct", "holder"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        DecodePayload(rawToken).TryGetProperty("vct", out _).Should().BeTrue();
        DisclosedNames(rawToken).Should().NotContain("vct").And.Contain("holder");
    }

    [Fact]
    public async Task MintCredentialAsync_ClaimSetDeclaresConflictingVct_OfferedTypeWins()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();

        // The token request was validated against the offer's type, so a credential whose declared
        // type contradicts it would contradict the authorisation it was minted under.
        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "OfferedType",
            new Dictionary<string, object> { ["vct"] = "SomethingElse", ["holder"] = "Alice" },
            disclosablePaths: null,
            signingKey: issuerPrivate,
            algorithm: "ES256");

        DecodePayload(rawToken).GetProperty("vct").GetString().Should().Be("OfferedType");
    }

    [Fact]
    public async Task MintCredentialAsync_Always_LeavesTheCallersClaimDictionaryUnmutated()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();

        // offer.Claims belongs to a stored offer that may be read again.
        var callerClaims = new Dictionary<string, object> { ["holder"] = "Alice" };

        await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "LicenseCredential",
            callerClaims,
            disclosablePaths: null,
            signingKey: issuerPrivate,
            algorithm: "ES256");

        callerClaims.Should().NotContainKey("vct").And.HaveCount(1);
    }

    [Fact]
    public async Task MintCredentialAsync_NullDisclosableSet_StillDisclosesEveryClaimExceptVct()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();

        // SdJwtService treats a null set as "every claim is disclosable"
        // (disclosableClaims?.ToList() ?? claims.Keys.ToList()). Excluding vct must not be done by
        // forwarding null — that would put vct back in a disclosure — nor by collapsing the set to
        // empty, which would silently stop disclosing anything for every caller that omits it.
        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "LicenseCredential",
            new Dictionary<string, object> { ["holder"] = "Alice", ["region"] = "Highland" },
            disclosablePaths: null,
            signingKey: issuerPrivate,
            algorithm: "ES256");

        DecodePayload(rawToken).GetProperty("vct").GetString().Should().Be("LicenseCredential");
        DisclosedNames(rawToken).Should().BeEquivalentTo(["holder", "region"],
            "a null set still means every claim is disclosable — except the type identifier");
    }

    [Fact]
    public async Task MintCredentialWithExternalSignerAsync_Always_WritesVctAsTopLevelClaim()
    {
        // The sign-on-behalf overload is the path a real org issuance takes (Feature 120 kid-swap).
        // It had the identical defect, so it needs its own assertion — one overload passing proves
        // nothing about the other.
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (_, holderPublic) = GenerateP256KeyPair();

        var rawToken = await _minter.MintCredentialWithExternalSignerAsync(
            "did:sorcha:org:issuer1",
            CreateHolderJwk(holderPublic),
            "CyberEssentialsUacPosture",
            new Dictionary<string, object> { ["compliant"] = true },
            disclosablePaths: ["vct", "compliant"],
            externalSigner: (data, _) => Task.FromResult(
                issuerKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)),
            algorithm: "ES256",
            kid: "did:sorcha:org:issuer1#vc-issuance-1");

        DecodePayload(rawToken).GetProperty("vct").GetString().Should().Be("CyberEssentialsUacPosture");
        DisclosedNames(rawToken).Should().NotContain("vct");
    }
}
