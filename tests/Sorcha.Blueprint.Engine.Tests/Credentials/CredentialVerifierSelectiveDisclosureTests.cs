// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 — selective disclosure against the reworked verifier, using real SD-JWT VC
/// presentations built by <c>SdJwtService.CreatePresentationAsync</c>. Only the disclosed claims
/// surface; undisclosed claims are genuinely absent from the verified token, not stripped by a mock.
/// </summary>
public class CredentialVerifierSelectiveDisclosureTests
{
    private static CredentialPresentation Present(string credentialId, string raw) =>
        new() { CredentialId = credentialId, RawPresentation = raw };

    [Fact]
    public async Task VerifyAsync_RequiredClaimsDisclosed_Accepted()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA", ["name"] = "Alice" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }]
            }
        };

        // Disclose only license_type — name stays hidden.
        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", Factory.Present(minted, "license_type"))]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].VerifiedClaims.Should().ContainKey("license_type");
        result.VerifiedCredentials[0].VerifiedClaims.Should().HaveCount(2); // vct + license_type
        result.VerifiedCredentials[0].VerifiedClaims.Should().NotContainKey("name");
    }

    [Fact]
    public async Task VerifyAsync_RequiredClaimNotDisclosed_Rejected()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["name"] = "Alice" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", Factory.Present(minted, "name"))]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.ClaimMismatch);
        result.Errors[0].Message.Should().Contain("license_type");
        result.Errors[0].Message.Should().Contain("not disclosed");
    }

    [Fact]
    public async Task VerifyAsync_RequiredClaimValueMismatch_Rejected()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassB" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type", ExpectedValue = "ClassA" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", Factory.Present(minted, "license_type"))]);

        result.IsValid.Should().BeFalse();
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.ClaimMismatch);
    }

    [Fact]
    public async Task VerifyAsync_PartialDisclosure_ExtraClaimsIgnored()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA", ["name"] = "Alice", ["address"] = "1 High St" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", Factory.Present(minted, "license_type"))]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].VerifiedClaims.Should().HaveCount(2); // vct + license_type only
        result.VerifiedCredentials[0].VerifiedClaims.Should().NotContainKey("name");
        result.VerifiedCredentials[0].VerifiedClaims.Should().NotContainKey("address");
    }

    [Fact]
    public async Task VerifyAsync_NoRequiredClaims_TypeMatchSufficient()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["name"] = "Alice" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        // Disclose nothing — only the non-disclosable vct remains.
        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", Factory.Present(minted))]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_MultipleRequirementsWithPartialDisclosure_AllMustSatisfy()
    {
        var identity = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["name"] = "Alice", ["birthdate"] = "1990-01-01" });
        var license = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA", ["holder_name"] = "Alice" });
        var verifier = Factory.BuildVerifier(identity, license);

        var requirements = new[]
        {
            new CredentialRequirement { Type = "IdentityAttestation", RequiredClaims = [new ClaimConstraint { ClaimName = "name" }] },
            new CredentialRequirement { Type = "LicenseCredential", RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }] }
        };

        var result = await verifier.VerifyAsync(requirements,
        [
            Present("identity-cred", Factory.Present(identity, "name")),
            Present("license-cred", Factory.Present(license, "license_type"))
        ]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(2);
    }

    [Fact]
    public async Task VerifyAsync_VctClaimUsedForType_Accepted()
    {
        // The SD-JWT VC profile keys the type on vct — the factory always mints vct, so this
        // confirms type-matching reads vct.
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("cred-1", Factory.Present(minted, "license_type"))]);

        result.IsValid.Should().BeTrue();
    }
}
