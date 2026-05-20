// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// FluentValidation validator for <see cref="CredentialRequirement"/>.
/// </summary>
public class CredentialRequirementValidator : AbstractValidator<CredentialRequirement>
{
    /// <summary>
    /// Initializes validation rules for credential requirements.
    /// </summary>
    public CredentialRequirementValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Credential type is required")
            .MaximumLength(200).WithMessage("Credential type must not exceed 200 characters");

        // Feature 135: trust is expressed as a TrustPolicy (replaces the flat accepted-issuer list).
        RuleFor(x => x.TrustPolicy!)
            .SetValidator(new TrustPolicyValidator())
            .When(x => x.TrustPolicy != null);

        RuleForEach(x => x.RequiredClaims)
            .SetValidator(new ClaimConstraintValidator())
            .When(x => x.RequiredClaims != null);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(x => x.Description != null);
    }
}

/// <summary>
/// FluentValidation validator for <see cref="TrustPolicy"/> (feature 135).
/// </summary>
public class TrustPolicyValidator : AbstractValidator<TrustPolicy>
{
    /// <summary>Initializes validation rules for a trust policy.</summary>
    public TrustPolicyValidator()
    {
        RuleFor(x => x.Sources)
            .NotEmpty().WithMessage("A trust policy must declare at least one trust source");

        RuleForEach(x => x.Sources)
            .ChildRules(source =>
            {
                source.RuleFor(s => s.AllowedIssuers)
                    .NotEmpty().WithMessage("A did-allowlist source must list at least one issuer")
                    .When(s => s.Kind == TrustSourceKind.DidAllowlist);

                source.RuleFor(s => s.TrustListId)
                    .NotEmpty().WithMessage("A trustlist source must name a trust list id")
                    .When(s => s.Kind == TrustSourceKind.TrustList);
            });
    }
}

/// <summary>
/// FluentValidation validator for <see cref="ClaimConstraint"/>.
/// </summary>
public class ClaimConstraintValidator : AbstractValidator<ClaimConstraint>
{
    /// <summary>
    /// Initializes validation rules for claim constraints.
    /// </summary>
    public ClaimConstraintValidator()
    {
        RuleFor(x => x.ClaimName)
            .NotEmpty().WithMessage("Claim name is required")
            .MaximumLength(200).WithMessage("Claim name must not exceed 200 characters");
    }
}
