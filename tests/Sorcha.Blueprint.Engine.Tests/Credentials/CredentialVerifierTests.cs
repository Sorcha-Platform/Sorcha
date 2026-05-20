// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

public class CredentialVerifierTests
{
    private readonly Mock<ISdJwtService> _sdJwtMock = new();
    private readonly CredentialVerifier _verifier;

    public CredentialVerifierTests()
    {
        _verifier = new CredentialVerifier(_sdJwtMock.Object);
    }

    [Fact]
    public async Task VerifyAsync_NoRequirements_ReturnsValid()
    {
        var result = await _verifier.VerifyAsync(
            requirements: [],
            presentations: []);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyAsync_ValidCredential_ReturnsValid()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential",
                    ["iss"] = "did:sorcha:issuer:gov",
                    ["name"] = "Alice"
                },
                RawPresentation = "jwt~disc1~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].CredentialId.Should().Be("cred-1");
        result.VerifiedCredentials[0].Type.Should().Be("LicenseCredential");
    }

    [Fact]
    public async Task VerifyAsync_MissingCredential_ReturnsInvalid()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations: []);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task VerifyAsync_IssuerMismatch_ReturnsInvalid()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov"])
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential",
                    ["iss"] = "did:sorcha:issuer:untrusted"
                },
                RawPresentation = "jwt~disc1~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.IssuerNotAccepted);
    }

    [Fact]
    public async Task VerifyAsync_ClaimMismatch_ReturnsInvalid()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }]
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential",
                    ["licenseType"] = "B"
                },
                RawPresentation = "jwt~disc1~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.ClaimMismatch);
    }

    [Fact]
    public async Task VerifyAsync_MissingRequiredClaim_ReturnsInvalid()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType" }]
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential"
                    // licenseType not disclosed
                },
                RawPresentation = "jwt~disc1~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.FailureReason == CredentialFailureReason.ClaimMismatch);
    }

    [Fact]
    public async Task VerifyAsync_TypeMismatch_ReturnsInvalid()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "IdentityAttestation"
                },
                RawPresentation = "jwt~disc1~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task VerifyAsync_MultipleRequirements_AllMustMatch()
    {
        var requirements = new[]
        {
            new CredentialRequirement { Type = "LicenseCredential" },
            new CredentialRequirement { Type = "IdentityAttestation" }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object> { ["type"] = "LicenseCredential" },
                RawPresentation = "jwt~"
            },
            new CredentialPresentation
            {
                CredentialId = "cred-2",
                DisclosedClaims = new Dictionary<string, object> { ["type"] = "IdentityAttestation" },
                RawPresentation = "jwt~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(2);
    }

    [Fact]
    public async Task VerifyAsync_AnyIssuerAccepted_WhenNoAcceptedIssuers()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = null // any issuer accepted (default register source applies)
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential",
                    ["iss"] = "did:sorcha:issuer:random"
                },
                RawPresentation = "jwt~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ClaimPresenceCheck_NoExpectedValue()
    {
        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = null }]
            }
        };

        var presentations = new[]
        {
            new CredentialPresentation
            {
                CredentialId = "cred-1",
                DisclosedClaims = new Dictionary<string, object>
                {
                    ["type"] = "LicenseCredential",
                    ["licenseType"] = "anything"
                },
                RawPresentation = "jwt~"
            }
        };

        var result = await _verifier.VerifyAsync(requirements, presentations);

        result.IsValid.Should().BeTrue();
    }
}
