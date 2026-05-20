// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Integration tests for cross-blueprint credential composability.
/// Verifies that credentials issued by one blueprint flow (Blueprint A)
/// are accepted as entry gates in a different blueprint flow (Blueprint B).
/// </summary>
public class CrossBlueprintCredentialTests
{
    private readonly Mock<ISdJwtService> _sdJwtMock;
    private readonly CredentialIssuer _issuer;
    private readonly CredentialVerifier _verifier;

    public CrossBlueprintCredentialTests()
    {
        _sdJwtMock = new Mock<ISdJwtService>();
        _issuer = new CredentialIssuer(_sdJwtMock.Object);
        _verifier = new CredentialVerifier(_sdJwtMock.Object);

        SetupSdJwtService();
    }

    [Fact]
    public async Task IssueThenVerify_CredentialFromBlueprintA_AcceptedByBlueprintB()
    {
        // Arrange — Blueprint A issues a LicenseCredential
        var issuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "holder_name", SourceField = "/applicantName" },
                new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" },
                new ClaimMapping { ClaimName = "license_number", SourceField = "/licenseNumber" }
            ],
            RecipientParticipantId = "applicant",
            ExpiryDuration = "P365D",
            Disclosable = ["holder_name", "license_type", "license_number"]
        };

        var actionData = new Dictionary<string, object>
        {
            ["applicantName"] = "Alice",
            ["licenseType"] = "ClassA",
            ["licenseNumber"] = "LIC-2026-001"
        };

        // Act — Issue credential via Blueprint A
        var issuedCredential = await _issuer.IssueAsync(
            issuanceConfig,
            actionData,
            issuerDid: "did:sorcha:issuer:gov-authority",
            recipientDid: "did:sorcha:holder:alice",
            signingKey: [1, 2, 3],
            algorithm: "EdDSA");

        // Transform issued credential into a presentation for Blueprint B
        var presentation = new CredentialPresentation
        {
            CredentialId = issuedCredential.CredentialId,
            DisclosedClaims = new Dictionary<string, object>(issuedCredential.Claims),
            RawPresentation = issuedCredential.RawToken
        };

        // Arrange — Blueprint B requires a LicenseCredential
        var blueprintBRequirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims =
                [
                    new ClaimConstraint { ClaimName = "license_type" }
                ]
            }
        };

        // Act — Verify presentation against Blueprint B requirements
        var verificationResult = await _verifier.VerifyAsync(
            blueprintBRequirements,
            [presentation]);

        // Assert — Credential from Blueprint A is accepted by Blueprint B
        verificationResult.IsValid.Should().BeTrue();
        verificationResult.VerifiedCredentials.Should().HaveCount(1);
        verificationResult.VerifiedCredentials[0].CredentialId
            .Should().Be(issuedCredential.CredentialId);
        verificationResult.VerifiedCredentials[0].Type
            .Should().Be("LicenseCredential");
        verificationResult.VerifiedCredentials[0].VerifiedClaims
            .Should().ContainKey("license_type").WhoseValue.Should().Be("ClassA");
    }

    [Fact]
    public async Task IssueThenVerify_WithIssuerConstraint_AcceptedWhenIssuerMatches()
    {
        // Arrange — Blueprint A issues credential from trusted authority
        var issuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" }
            ],
            RecipientParticipantId = "applicant"
        };

        var actionData = new Dictionary<string, object>
        {
            ["licenseType"] = "ClassA"
        };

        var trustedIssuer = "did:sorcha:issuer:gov-authority";

        // Act — Issue
        var issuedCredential = await _issuer.IssueAsync(
            issuanceConfig, actionData, trustedIssuer,
            "did:sorcha:holder:alice", [1, 2, 3], "EdDSA");

        // Present with issuer claim
        var presentation = new CredentialPresentation
        {
            CredentialId = issuedCredential.CredentialId,
            DisclosedClaims = new Dictionary<string, object>(issuedCredential.Claims)
            {
                ["iss"] = trustedIssuer
            },
            RawPresentation = issuedCredential.RawToken
        };

        // Blueprint B requires credential from the trusted issuer
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers([trustedIssuer])
            }
        };

        // Act — Verify
        var result = await _verifier.VerifyAsync(requirements, [presentation]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].IssuerDid.Should().Be(trustedIssuer);
    }

    [Fact]
    public async Task IssueThenVerify_WithClaimValueConstraint_AcceptedWhenValueMatches()
    {
        // Arrange — Issue a ClassA license
        var issuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" }
            ],
            RecipientParticipantId = "applicant"
        };

        var issuedCredential = await _issuer.IssueAsync(
            issuanceConfig,
            new Dictionary<string, object> { ["licenseType"] = "ClassA" },
            "did:issuer:1", "did:holder:1", [1, 2, 3], "EdDSA");

        var presentation = new CredentialPresentation
        {
            CredentialId = issuedCredential.CredentialId,
            DisclosedClaims = new Dictionary<string, object>(issuedCredential.Claims),
            RawPresentation = issuedCredential.RawToken
        };

        // Blueprint B specifically requires ClassA
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims =
                [
                    new ClaimConstraint { ClaimName = "license_type", ExpectedValue = "ClassA" }
                ]
            }
        };

        // Act
        var result = await _verifier.VerifyAsync(requirements, [presentation]);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task IssueThenVerify_MismatchedIssuer_RejectedByBlueprintB()
    {
        // Arrange — Blueprint A issues credential from an untrusted authority
        var issuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" }
            ],
            RecipientParticipantId = "applicant"
        };

        var untrustedIssuer = "did:sorcha:issuer:unknown-body";

        var issuedCredential = await _issuer.IssueAsync(
            issuanceConfig,
            new Dictionary<string, object> { ["licenseType"] = "ClassA" },
            untrustedIssuer, "did:sorcha:holder:bob", [1, 2, 3], "EdDSA");

        // Present credential with issuer claim
        var presentation = new CredentialPresentation
        {
            CredentialId = issuedCredential.CredentialId,
            DisclosedClaims = new Dictionary<string, object>(issuedCredential.Claims)
            {
                ["iss"] = untrustedIssuer
            },
            RawPresentation = issuedCredential.RawToken
        };

        // Blueprint B only trusts the government authority
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov-authority"])
            }
        };

        // Act
        var result = await _verifier.VerifyAsync(requirements, [presentation]);

        // Assert — Rejected because issuer is not trusted
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.IssuerNotAccepted);
        result.Errors[0].RequirementType.Should().Be("LicenseCredential");
    }

    [Fact]
    public async Task IssueThenVerify_WrongCredentialType_RejectedByBlueprintB()
    {
        // Arrange — Blueprint A issues an IdentityAttestation
        var issuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = "IdentityAttestation",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "name", SourceField = "/holderName" }
            ],
            RecipientParticipantId = "applicant"
        };

        var issuedCredential = await _issuer.IssueAsync(
            issuanceConfig,
            new Dictionary<string, object> { ["holderName"] = "Charlie" },
            "did:issuer:1", "did:holder:1", [1, 2, 3], "EdDSA");

        var presentation = new CredentialPresentation
        {
            CredentialId = issuedCredential.CredentialId,
            DisclosedClaims = new Dictionary<string, object>(issuedCredential.Claims),
            RawPresentation = issuedCredential.RawToken
        };

        // Blueprint B requires a LicenseCredential, NOT an IdentityAttestation
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        // Act
        var result = await _verifier.VerifyAsync(requirements, [presentation]);

        // Assert — Rejected because type doesn't match
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task IssueThenVerify_MultipleCredentials_ComposedFromDifferentBlueprints()
    {
        // Arrange — Blueprint A issues an IdentityAttestation
        var identityConfig = new CredentialIssuanceConfig
        {
            CredentialType = "IdentityAttestation",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "name", SourceField = "/fullName" }
            ],
            RecipientParticipantId = "applicant"
        };

        var identityCredential = await _issuer.IssueAsync(
            identityConfig,
            new Dictionary<string, object> { ["fullName"] = "Alice Smith" },
            "did:issuer:identity", "did:holder:alice", [1, 2, 3], "EdDSA");

        // Blueprint B issues a LicenseCredential
        var licenseConfig = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "license_type", SourceField = "/licenseType" }
            ],
            RecipientParticipantId = "applicant"
        };

        var licenseCredential = await _issuer.IssueAsync(
            licenseConfig,
            new Dictionary<string, object> { ["licenseType"] = "ClassA" },
            "did:issuer:licensing", "did:holder:alice", [1, 2, 3], "EdDSA");

        // Blueprint C requires BOTH credentials
        var requirements = new[]
        {
            new CredentialRequirement { Type = "IdentityAttestation" },
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = identityCredential.CredentialId,
                DisclosedClaims = new Dictionary<string, object>(identityCredential.Claims),
                RawPresentation = identityCredential.RawToken
            },
            new CredentialPresentation
            {
                CredentialId = licenseCredential.CredentialId,
                DisclosedClaims = new Dictionary<string, object>(licenseCredential.Claims),
                RawPresentation = licenseCredential.RawToken
            }
        };

        // Act — Verify both credentials against Blueprint C
        var result = await _verifier.VerifyAsync(requirements, presentations);

        // Assert — Both credentials accepted
        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(2);
        result.VerifiedCredentials.Should().Contain(v => v.Type == "IdentityAttestation");
        result.VerifiedCredentials.Should().Contain(v => v.Type == "LicenseCredential");
    }

    [Fact]
    public async Task IssueThenVerify_PartialMultiRequirement_RejectedWhenOneMissing()
    {
        // Arrange — Only have IdentityAttestation, missing LicenseCredential
        var identityConfig = new CredentialIssuanceConfig
        {
            CredentialType = "IdentityAttestation",
            ClaimMappings =
            [
                new ClaimMapping { ClaimName = "name", SourceField = "/fullName" }
            ],
            RecipientParticipantId = "applicant"
        };

        var identityCredential = await _issuer.IssueAsync(
            identityConfig,
            new Dictionary<string, object> { ["fullName"] = "Bob" },
            "did:issuer:1", "did:holder:bob", [1, 2, 3], "EdDSA");

        // Blueprint requires both IdentityAttestation AND LicenseCredential
        var requirements = new[]
        {
            new CredentialRequirement { Type = "IdentityAttestation" },
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = identityCredential.CredentialId,
                DisclosedClaims = new Dictionary<string, object>(identityCredential.Claims),
                RawPresentation = identityCredential.RawToken
            }
        };

        // Act
        var result = await _verifier.VerifyAsync(requirements, presentations);

        // Assert — Rejected because LicenseCredential is missing
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.RequirementType == "LicenseCredential" &&
            e.FailureReason == CredentialFailureReason.Missing);
    }

    private void SetupSdJwtService()
    {
        _sdJwtMock
            .Setup(s => s.CreateTokenAsync(
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<List<string>?>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdJwtToken { RawToken = "eyJ.eyJ.sig~disc1~disc2~" });
    }
}
