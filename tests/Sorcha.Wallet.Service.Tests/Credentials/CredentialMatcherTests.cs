// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;

namespace Sorcha.Wallet.Service.Tests.Credentials;

public class CredentialMatcherTests
{
    private readonly CredentialMatcher _matcher = new();

    private static CredentialEntity CreateCredential(
        string id, string type, string issuer,
        Dictionary<string, object>? claims = null,
        DateTimeOffset? expiresAt = null,
        CredentialStatus status = CredentialStatus.Active)
    {
        return new CredentialEntity
        {
            Id = id,
            Type = type,
            IssuerDid = issuer,
            SubjectDid = "did:sorcha:subject:alice",
            ClaimsJson = JsonSerializer.Serialize(claims ?? new Dictionary<string, object>()),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ExpiresAt = expiresAt,
            RawToken = "dummy-token",
            Status = status,
            WalletAddress = "wallet-1",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void Match_TypeMatch_ReturnsMatch()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "did:sorcha:issuer:gov")
        };

        var result = _matcher.Match(requirements, credentials);

        result.Should().ContainKey("LicenseCredential");
        result["LicenseCredential"].Should().NotBeNull();
        result["LicenseCredential"]!.Id.Should().Be("cred-1");
    }

    [Fact]
    public void Match_TypeMismatch_ReturnsNull()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "IdentityAttestation", "did:sorcha:issuer:gov")
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_IssuerFilter_RejectsUntrustedIssuer()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "did:sorcha:issuer:untrusted")
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_IssuerFilter_AcceptsTrustedIssuer()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "did:sorcha:issuer:gov")
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().NotBeNull();
    }

    [Fact]
    public void Match_ClaimConstraint_MatchesValue()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }]
            }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer",
                claims: new Dictionary<string, object> { ["licenseType"] = "A" })
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().NotBeNull();
    }

    [Fact]
    public void Match_ClaimConstraint_RejectsWrongValue()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }]
            }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer",
                claims: new Dictionary<string, object> { ["licenseType"] = "B" })
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_ExpiredCredential_Rejected()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer",
                expiresAt: DateTimeOffset.UtcNow.AddDays(-1)) // expired
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_RevokedCredential_Rejected()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer", status: CredentialStatus.Revoked)
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_MultipleRequirements_MatchesSeparately()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" },
            new CredentialRequirement { Type = "IdentityAttestation" }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer"),
            CreateCredential("cred-2", "IdentityAttestation", "issuer")
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().NotBeNull();
        result["IdentityAttestation"].Should().NotBeNull();
    }

    [Fact]
    public void Match_EmptyCredentialList_AllNull()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var result = _matcher.Match(requirements, new List<CredentialEntity>());

        result["LicenseCredential"].Should().BeNull();
    }

    // --- C2: RawToken (not stale ClaimsJson) is the source of truth for claim gating ---

    // --- SD-JWT construction helpers (RFC 9901 §4.2.1) — mirrors SdJwtClaimProjectionTests ---

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    private static string Digest(string disclosure) =>
        B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    private static string Token(object body, params string[] disclosures)
    {
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln";
        return disclosures.Length == 0 ? jwt : jwt + "~" + string.Join("~", disclosures);
    }

    /// <summary>
    /// A valid SD-JWT carrying a NESTED disclosure for "address" (town + line1
    /// individually disclosable children) — the real, correctly-nested schema.
    /// </summary>
    private static string NestedAddressRawToken()
    {
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/assured-identity/v1",
            ["iss"] = "did:sorcha:org:ws11q",
            ["email"] = "stuart@stuartfraser.net",
            ["address"] = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        };
        return Token(body, town, line1);
    }

    /// <summary>
    /// Reproduces the real n1 defect: a credential ingested BEFORE the nested
    /// SD-JWT decoder fix has a stored ClaimsJson where "town" (really nested
    /// inside "address") leaked out as a flat TOP-LEVEL key, while its RawToken
    /// (correctly nested) is the true schema.
    /// </summary>
    private static CredentialEntity StaleFlattenedCredential() => new()
    {
        Id = "cred-1",
        Type = "AssuredIdentityCredential",
        IssuerDid = "did:sorcha:org:ws11q",
        SubjectDid = "did:sorcha:subject:alice",
        ClaimsJson =
            """{"email":"stuart@stuartfraser.net","town":"Edinburgh","line1":"6/2 Warrender Park Terrace","address":{"_sd":["stale-digest"]}}""",
        RawToken = NestedAddressRawToken(),
        IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
        Status = CredentialStatus.Active,
        WalletAddress = "wallet-1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void Match_StaleFlattenedClaimsJsonWithNestedRawToken_DoesNotFalselyMatchLeakedTopLevelClaim()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "AssuredIdentityCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "town" }]
            }
        };

        var credentials = new List<CredentialEntity> { StaleFlattenedCredential() };

        var result = _matcher.Match(requirements, credentials);

        // "town" is nested inside "address" in the real (raw-token-derived) schema —
        // it must NOT be reported matched just because the stale stored ClaimsJson
        // blob happens to carry that key at the top level.
        result["AssuredIdentityCredential"].Should().BeNull();
    }

    [Fact]
    public async Task MatchAsync_StaleFlattenedClaimsJsonWithNestedRawToken_DoesNotFalselyMatchLeakedTopLevelClaim()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "AssuredIdentityCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "town" }]
            }
        };

        var credentials = new List<CredentialEntity> { StaleFlattenedCredential() };

        var result = await _matcher.MatchAsync(requirements, credentials);

        result["AssuredIdentityCredential"].Should().BeNull();
    }

    [Fact]
    public void Match_ClaimConstraint_NonSdJwtRawToken_FallsBackToStoredClaimsJson()
    {
        // Discipline check: when RawToken genuinely isn't a decodable SD-JWT (mdoc/CBOR,
        // W3C VC, truncated token — CreateCredential's "dummy-token" default stands in
        // for all three), the stored ClaimsJson must still be usable for gating, not
        // wiped to "no match" because the projection failed.
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }]
            }
        };

        var credentials = new List<CredentialEntity>
        {
            CreateCredential("cred-1", "LicenseCredential", "issuer",
                claims: new Dictionary<string, object> { ["licenseType"] = "A" })
        };

        var result = _matcher.Match(requirements, credentials);

        result["LicenseCredential"].Should().NotBeNull();
    }
}
