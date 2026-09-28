// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// #1199 — a disclosure is an ISSUER's claim only if the issuer-signed payload commits its digest.
/// These attack the verifier the way a presenter would: by editing the unsigned disclosure tail of
/// a genuinely issued token.
/// </summary>
/// <remarks>
/// <c>SdJwtService.VerifyTokenAsync</c> — behind the HAIP OpenID4VP verifier, the blueprint
/// credential gate and the wallet's presentation checks — merged every disclosure into the verified
/// claims with no digest check, so a presenter could add a claim the issuer never made, or replace
/// a signed one. The KB-JWT <c>sd_hash</c> is presenter-signed and does not prevent this.
/// </remarks>
public class SdJwtDisclosureAnchoringTests
{
    private readonly SdJwtService _service = new();

    private static (byte[] Private, byte[] Public) P256()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    private async Task<(SdJwtToken Token, byte[] PublicKey)> IssueAsync()
    {
        var (priv, pub) = P256();
        var token = await _service.CreateTokenAsync(
            new Dictionary<string, object>
            {
                ["given_name"] = "Alice",
                ["nationality"] = "IE",
                ["licenseType"] = "B",       // a plain, signed, NON-disclosable claim
            },
            disclosableClaims: ["given_name", "nationality"],
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: priv,
            algorithm: "ES256");
        return (token, pub);
    }

    private static string Disclosure(string name, object value) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new object[] { "forged-salt-0001", name, value })));

    /// <summary>The issuer-signed JWT with a caller-chosen disclosure tail.</summary>
    private static string WithDisclosures(SdJwtToken token, params string[] disclosures)
    {
        var jwt = token.RawToken.Split('~')[0];
        return jwt + "~" + string.Concat(disclosures.Select(d => d + "~"));
    }

    [Fact]
    public async Task AnHonestPresentation_Verifies_WithItsDisclosedClaims()
    {
        var (token, pub) = await IssueAsync();

        var result = await _service.VerifyTokenAsync(token.RawToken, pub, "ES256");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.Claims["given_name"].Should().Be("Alice");
        result.Claims["licenseType"].Should().Be("B");
    }

    [Fact]
    public async Task PresentingOnlySomeDisclosures_StillVerifies()
    {
        // Selective disclosure is the point: withholding a disclosure is legitimate.
        var (token, pub) = await IssueAsync();

        var result = await _service.VerifyTokenAsync(WithDisclosures(token, token.Disclosures[0]), pub, "ES256");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task AForgedDisclosure_AddingAClaimTheIssuerNeverMade_IsRejected()
    {
        var (token, pub) = await IssueAsync();

        var forged = WithDisclosures(token, [.. token.Disclosures, Disclosure("age_over_18", true)]);
        var result = await _service.VerifyTokenAsync(forged, pub, "ES256");

        result.IsValid.Should().BeFalse("the issuer never committed age_over_18");
        result.Claims.Should().NotContainKey("age_over_18");
        result.Errors.Should().Contain(e => e.Contains("age_over_18") && e.Contains("not committed"));
    }

    [Fact]
    public async Task AForgedDisclosure_ReplacingASignedPlainClaim_IsRejected()
    {
        var (token, pub) = await IssueAsync();

        var forged = WithDisclosures(token, [.. token.Disclosures, Disclosure("licenseType", "C")]);
        var result = await _service.VerifyTokenAsync(forged, pub, "ES256");

        result.IsValid.Should().BeFalse();
        (result.Claims.TryGetValue("licenseType", out var v) ? v : null).Should().NotBe("C",
            "a presenter must not be able to overwrite what the issuer signed");
    }

    [Fact]
    public async Task AForgedDisclosure_ReplacingADisclosableClaimsValue_IsRejected()
    {
        // Same name as a genuine disclosure, different value (and so a different digest).
        var (token, pub) = await IssueAsync();

        var forged = WithDisclosures(token, Disclosure("given_name", "Mallory"));
        var result = await _service.VerifyTokenAsync(forged, pub, "ES256");

        result.IsValid.Should().BeFalse();
        (result.Claims.TryGetValue("given_name", out var v) ? v : null).Should().NotBe("Mallory");
    }

    [Fact]
    public async Task TheRuleIsShared_WithTheVerifierEngine()
    {
        // The two verifiers disagreeing about which disclosures are issuer-committed was the defect;
        // both now call the one implementation, and must reach the same verdict on the same token.
        var (token, _) = await IssueAsync();
        var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(token.RawToken.Split('~')[0].Split('.')[1])).RootElement;

        var genuine = Sorcha.Verification.Abstractions.SdJwtDisclosureAnchoring.FindUnanchoredDisclosures(payload, token.Disclosures);
        var forged = Sorcha.Verification.Abstractions.SdJwtDisclosureAnchoring.FindUnanchoredDisclosures(
            payload, [.. token.Disclosures, Disclosure("age_over_18", true)]);

        genuine.Should().BeEmpty();
        forged.Should().ContainSingle().Which.Should().Be("age_over_18");
    }
}
