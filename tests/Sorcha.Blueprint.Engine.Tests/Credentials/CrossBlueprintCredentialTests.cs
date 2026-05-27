// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;

using Factory = Sorcha.Blueprint.Engine.Tests.Credentials.EngineSdJwtTestFactory;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 — cross-blueprint credential composability on real signed SD-JWT VCs: a credential
/// "issued" by one flow is presented as an entry gate to another. Replaces the prior mock-issuer
/// pipeline; the issuer signature is genuinely verified and the issuer trust runs through the
/// unified evaluator.
/// </summary>
public class CrossBlueprintCredentialTests
{
    private static CredentialPresentation Present(string credentialId, string raw) =>
        new() { CredentialId = credentialId, RawPresentation = raw };

    [Fact]
    public async Task IssueThenVerify_CredentialFromBlueprintA_AcceptedByBlueprintB()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:gov-authority",
            new Dictionary<string, object>
            {
                ["holder_name"] = "Alice",
                ["license_type"] = "ClassA",
                ["license_number"] = "LIC-2026-001"
            });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("license-cred", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].CredentialId.Should().Be("license-cred");
        result.VerifiedCredentials[0].Type.Should().Be("LicenseCredential");
        result.VerifiedCredentials[0].VerifiedClaims.Should().ContainKey("license_type").WhoseValue.Should().Be("ClassA");
    }

    [Fact]
    public async Task IssueThenVerify_WithIssuerConstraint_AcceptedWhenIssuerMatches()
    {
        const string trustedIssuer = "did:sorcha:issuer:gov-authority";
        var minted = Factory.MintEs256("LicenseCredential", trustedIssuer,
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers([trustedIssuer])
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(1);
        result.VerifiedCredentials[0].IssuerDid.Should().Be(trustedIssuer);
    }

    [Fact]
    public async Task IssueThenVerify_WithClaimValueConstraint_AcceptedWhenValueMatches()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:1",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                RequiredClaims = [new ClaimConstraint { ClaimName = "license_type", ExpectedValue = "ClassA" }]
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task IssueThenVerify_MismatchedIssuer_RejectedByBlueprintB()
    {
        var minted = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:unknown-body",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[]
        {
            new CredentialRequirement
            {
                Type = "LicenseCredential",
                TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:gov-authority"])
            }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.IssuerNotAccepted);
        result.Errors[0].RequirementType.Should().Be("LicenseCredential");
    }

    [Fact]
    public async Task IssueThenVerify_WrongCredentialType_RejectedByBlueprintB()
    {
        var minted = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:1",
            new Dictionary<string, object> { ["name"] = "Charlie" });
        var verifier = Factory.BuildVerifier(minted);

        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };

        var result = await verifier.VerifyAsync(requirements, [Present("cred-1", minted.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].FailureReason.Should().Be(CredentialFailureReason.Missing);
    }

    [Fact]
    public async Task IssueThenVerify_MultipleCredentials_ComposedFromDifferentBlueprints()
    {
        var identity = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:identity",
            new Dictionary<string, object> { ["name"] = "Alice Smith" });
        var license = Factory.MintEs256("LicenseCredential", "did:sorcha:issuer:licensing",
            new Dictionary<string, object> { ["license_type"] = "ClassA" });
        var verifier = Factory.BuildVerifier(identity, license);

        var requirements = new[]
        {
            new CredentialRequirement { Type = "IdentityAttestation" },
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var result = await verifier.VerifyAsync(requirements,
            [Present("identity-cred", identity.Raw), Present("license-cred", license.Raw)]);

        result.IsValid.Should().BeTrue();
        result.VerifiedCredentials.Should().HaveCount(2);
        result.VerifiedCredentials.Should().Contain(v => v.Type == "IdentityAttestation");
        result.VerifiedCredentials.Should().Contain(v => v.Type == "LicenseCredential");
    }

    [Fact]
    public async Task IssueThenVerify_PartialMultiRequirement_RejectedWhenOneMissing()
    {
        var identity = Factory.MintEs256("IdentityAttestation", "did:sorcha:issuer:1",
            new Dictionary<string, object> { ["name"] = "Bob" });
        var verifier = Factory.BuildVerifier(identity);

        var requirements = new[]
        {
            new CredentialRequirement { Type = "IdentityAttestation" },
            new CredentialRequirement { Type = "LicenseCredential" }
        };

        var result = await verifier.VerifyAsync(requirements, [Present("identity-cred", identity.Raw)]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.RequirementType == "LicenseCredential" && e.FailureReason == CredentialFailureReason.Missing);
    }
}
