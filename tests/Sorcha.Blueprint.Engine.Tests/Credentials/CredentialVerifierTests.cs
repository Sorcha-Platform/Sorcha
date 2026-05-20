// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 — the engine <see cref="Sorcha.Blueprint.Engine.Credentials.CredentialVerifier"/>
/// reworked onto real signed SD-JWT VCs (no mocked <c>ISdJwtService</c>, no placeholder
/// presentations). Type-matching and required-claim constraints are exercised over the claims a
/// genuinely-verified credential discloses; issuer trust flows through the unified evaluator.
/// </summary>
public class CredentialVerifierTests
{
    private static CredentialPresentation Present(string credentialId, string raw) =>
        new() { CredentialId = credentialId, RawPresentation = raw };

    [Fact]
    public async Task VerifyAsync_NoRequirements_ReturnsValid()
    {
        var verifier = Factory.BuildVerifier();

        var result = await verifier.VerifyAsync(requirements: [], presentations: []);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_ValidCredential_ReturnsValid()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["name"] = "Alice" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].CredentialId.Should().Be("cred-1");
        result.VerifiedCredentials[0].Type.Should().Be("LicenseCredential");
        result.VerifiedCredentials[0].SignatureValid.Should().BeTrue();
        result.VerifiedCredentials[0].IssuerDid.Should().Be("did:sorcha:issuer:gov");
    }

    [Fact]
    public async Task VerifyAsync_MissingCredential_ReturnsInvalid()
    {
        var verifier = Factory.BuildVerifier();
        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, presentations: []);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task VerifyAsync_IssuerMismatch_ReturnsInvalid()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:untrusted");
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.IssuerNotAccepted);
    }

    [Fact]
    public async Task VerifyAsync_ClaimMismatch_ReturnsInvalid()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["licenseType"] = "B" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.ClaimMismatch);
    }

    [Fact]
    public async Task VerifyAsync_MissingRequiredClaim_ReturnsInvalid()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov");
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.ClaimMismatch);
    }

    [Fact]
    public async Task VerifyAsync_TypeMismatch_ReturnsInvalid()
    {
        var minted = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:gov");
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task VerifyAsync_MultipleRequirements_AllMustMatch()
    {
        var license = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov");
        var identity = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:gov");
        var verifier = Factory.BuildVerifier(license, identity);

        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" },
            new CredentialRequirement { Type = "IdentityAttestation" }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", license.Raw), Present("cred-2", identity.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(2);
    }

    [Fact]
    public async Task VerifyAsync_AnyIssuerAccepted_WhenNoTrustPolicy()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:random");
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            // null policy → default register@Low source; the directory resolves any issuer.
            new CredentialRequirement { Type = "LicenseCredential", TrustPolicy = null }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ClaimPresenceCheck_NoExpectedValue()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["licenseType"] = "anything" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = null }]
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
    }
}
