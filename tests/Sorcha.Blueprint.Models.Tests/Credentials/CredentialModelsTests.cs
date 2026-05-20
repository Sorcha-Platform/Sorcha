// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using FluentValidation.TestHelper;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Models.Tests.Credentials;

public class CredentialRequirementValidatorTests
{
    private readonly CredentialRequirementValidator _validator = new();

    [Fact]
    public void Validate_ValidRequirement_HasNoErrors()
    {
        var requirement = new CredentialRequirement
        {
            Type = "LicenseCredential",
            TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(["did:sorcha:issuer:abc123"]),
            RequiredClaims = [new ClaimConstraint { ClaimName = "licenseType", ExpectedValue = "A" }],
            RevocationCheckPolicy = RevocationCheckPolicy.FailClosed,
            Description = "Valid driving license"
        };

        var result = _validator.TestValidate(requirement);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyType_HasError()
    {
        var requirement = new CredentialRequirement { Type = "" };

        var result = _validator.TestValidate(requirement);

        result.ShouldHaveValidationErrorFor(x => x.Type);
    }

    [Fact]
    public void Validate_TypeExceedsMaxLength_HasError()
    {
        var requirement = new CredentialRequirement { Type = new string('A', 201) };

        var result = _validator.TestValidate(requirement);

        result.ShouldHaveValidationErrorFor(x => x.Type);
    }

    [Fact]
    public void Validate_DidAllowlistSourceWithNoIssuers_HasError()
    {
        var requirement = new CredentialRequirement
        {
            Type = "LicenseCredential",
            TrustPolicy = new TrustPolicy
            {
                Sources = [new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = [] }]
            }
        };

        var result = _validator.TestValidate(requirement);

        result.ShouldHaveValidationErrorFor("TrustPolicy.Sources[0].AllowedIssuers");
    }

    [Fact]
    public void Validate_NullTrustPolicy_NoError()
    {
        var requirement = new CredentialRequirement
        {
            Type = "LicenseCredential",
            TrustPolicy = null
        };

        var result = _validator.TestValidate(requirement);

        result.ShouldNotHaveValidationErrorFor(x => x.TrustPolicy);
    }

    [Fact]
    public void Validate_RequiredClaimWithEmptyName_HasError()
    {
        var requirement = new CredentialRequirement
        {
            Type = "LicenseCredential",
            RequiredClaims = [new ClaimConstraint { ClaimName = "" }]
        };

        var result = _validator.TestValidate(requirement);

        result.ShouldHaveValidationErrorFor("RequiredClaims[0].ClaimName");
    }

    [Fact]
    public void Validate_DescriptionExceedsMaxLength_HasError()
    {
        var requirement = new CredentialRequirement
        {
            Type = "LicenseCredential",
            Description = new string('X', 501)
        };

        var result = _validator.TestValidate(requirement);

        result.ShouldHaveValidationErrorFor(x => x.Description);
    }
}

public class CredentialIssuanceConfigValidatorTests
{
    private readonly CredentialIssuanceConfigValidator _validator = new();

    [Fact]
    public void Validate_ValidConfig_HasNoErrors()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [new ClaimMapping { ClaimName = "licenseType", SourceField = "/licenseType" }],
            RecipientParticipantId = "participant-1",
            ExpiryDuration = "P365D"
        };

        var result = _validator.TestValidate(config);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyCredentialType_HasError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "",
            ClaimMappings = [new ClaimMapping { ClaimName = "x", SourceField = "/x" }],
            RecipientParticipantId = "p1"
        };

        var result = _validator.TestValidate(config);

        result.ShouldHaveValidationErrorFor(x => x.CredentialType);
    }

    [Fact]
    public void Validate_EmptyClaimMappings_HasError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [],
            RecipientParticipantId = "p1"
        };

        var result = _validator.TestValidate(config);

        result.ShouldHaveValidationErrorFor(x => x.ClaimMappings);
    }

    [Fact]
    public void Validate_ClaimMappingInvalidJsonPointer_HasError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [new ClaimMapping { ClaimName = "x", SourceField = "noSlash" }],
            RecipientParticipantId = "p1"
        };

        var result = _validator.TestValidate(config);

        result.ShouldHaveValidationErrorFor("ClaimMappings[0].SourceField");
    }

    [Fact]
    public void Validate_EmptyRecipientParticipantId_HasError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [new ClaimMapping { ClaimName = "x", SourceField = "/x" }],
            RecipientParticipantId = ""
        };

        var result = _validator.TestValidate(config);

        result.ShouldHaveValidationErrorFor(x => x.RecipientParticipantId);
    }

    [Fact]
    public void Validate_InvalidExpiryDuration_HasError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [new ClaimMapping { ClaimName = "x", SourceField = "/x" }],
            RecipientParticipantId = "p1",
            ExpiryDuration = "30days"
        };

        var result = _validator.TestValidate(config);

        result.ShouldHaveValidationErrorFor(x => x.ExpiryDuration);
    }

    [Fact]
    public void Validate_NullExpiryDuration_NoError()
    {
        var config = new CredentialIssuanceConfig
        {
            CredentialType = "LicenseCredential",
            ClaimMappings = [new ClaimMapping { ClaimName = "x", SourceField = "/x" }],
            RecipientParticipantId = "p1",
            ExpiryDuration = null
        };

        var result = _validator.TestValidate(config);

        result.ShouldNotHaveValidationErrorFor(x => x.ExpiryDuration);
    }
}

public class CredentialPresentationValidatorTests
{
    private readonly CredentialPresentationValidator _validator = new();

    [Fact]
    public void Validate_ValidPresentation_HasNoErrors()
    {
        var presentation = new CredentialPresentation
        {
            CredentialId = "did:sorcha:credential:abc123",
            DisclosedClaims = new Dictionary<string, object> { ["name"] = "Alice" },
            RawPresentation = "eyJhbGciOiJFZERTQSJ9.eyJpc3MiOiJ0ZXN0In0.sig~disc1~"
        };

        var result = _validator.TestValidate(presentation);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyCredentialId_HasError()
    {
        var presentation = new CredentialPresentation
        {
            CredentialId = "",
            RawPresentation = "token~"
        };

        var result = _validator.TestValidate(presentation);

        result.ShouldHaveValidationErrorFor(x => x.CredentialId);
    }

    [Fact]
    public void Validate_EmptyRawPresentation_HasError()
    {
        var presentation = new CredentialPresentation
        {
            CredentialId = "did:sorcha:credential:abc123",
            RawPresentation = ""
        };

        var result = _validator.TestValidate(presentation);

        result.ShouldHaveValidationErrorFor(x => x.RawPresentation);
    }
}
