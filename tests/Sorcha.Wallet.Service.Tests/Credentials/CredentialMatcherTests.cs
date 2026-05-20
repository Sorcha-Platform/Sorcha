// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}
