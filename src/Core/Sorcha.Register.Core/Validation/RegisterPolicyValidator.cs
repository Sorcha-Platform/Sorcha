// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Sorcha.Register.Models;

namespace Sorcha.Register.Core.Validation;

/// <summary>
/// Validates a <see cref="RegisterPolicy"/> including all nested configuration sections.
/// </summary>
public class RegisterPolicyValidator : AbstractValidator<RegisterPolicy>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterPolicyValidator"/> class.
    /// </summary>
    public RegisterPolicyValidator()
    {
        RuleFor(x => x.Version)
            .GreaterThanOrEqualTo((uint)1)
            .WithMessage("Policy version must be >= 1.");

        RuleFor(x => x.Governance)
            .NotNull()
            .WithMessage("Governance configuration is required.")
            .SetValidator(new PolicyGovernanceConfigValidator())
            .When(x => x.Governance is not null);

        RuleFor(x => x.Validators)
            .NotNull()
            .WithMessage("Validators configuration is required.")
            .SetValidator(new PolicyValidatorConfigValidator())
            .When(x => x.Validators is not null);

        RuleFor(x => x.Consensus)
            .NotNull()
            .WithMessage("Consensus configuration is required.")
            .SetValidator(new PolicyConsensusConfigValidator())
            .When(x => x.Consensus is not null);

        RuleFor(x => x.LeaderElection)
            .NotNull()
            .WithMessage("Leader election configuration is required.")
            .SetValidator(new PolicyLeaderElectionConfigValidator())
            .When(x => x.LeaderElection is not null);
    }
}

/// <summary>
/// Validates <see cref="PolicyGovernanceConfig"/> settings including quorum formula,
/// proposal TTL, and blueprint version.
/// </summary>
public class PolicyGovernanceConfigValidator : AbstractValidator<PolicyGovernanceConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyGovernanceConfigValidator"/> class.
    /// </summary>
    public PolicyGovernanceConfigValidator()
    {
        RuleFor(x => x.QuorumFormula)
            .IsInEnum()
            .WithMessage("QuorumFormula must be a defined enum value.");

        RuleFor(x => x.ProposalTtlDays)
            .InclusiveBetween(1, 90)
            .WithMessage("ProposalTtlDays must be between 1 and 90.");

        RuleFor(x => x.BlueprintVersion)
            .NotEmpty()
            .WithMessage("BlueprintVersion must not be empty.")
            .MaximumLength(100)
            .WithMessage("BlueprintVersion must be at most 100 characters.");
    }
}

/// <summary>
/// Validates <see cref="PolicyValidatorConfig"/> settings including registration mode,
/// approved validator lists, capacity limits, staking rules, and operational TTL.
/// </summary>
public class PolicyValidatorConfigValidator : AbstractValidator<PolicyValidatorConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyValidatorConfigValidator"/> class.
    /// </summary>
    public PolicyValidatorConfigValidator()
    {
        RuleFor(x => x.RegistrationMode)
            .IsInEnum()
            .WithMessage("RegistrationMode must be a defined enum value.");

        RuleFor(x => x.ApprovedValidators)
            .Must(v => v.Count <= 100)
            .WithMessage("ApprovedValidators must contain at most 100 entries.")
            .When(x => x.ApprovedValidators is not null);

        RuleForEach(x => x.ApprovedValidators)
            .SetValidator(new ApprovedValidatorValidator())
            .When(x => x.ApprovedValidators is not null);

        RuleFor(x => x.MinValidators)
            .GreaterThanOrEqualTo(1)
            .WithMessage("MinValidators must be >= 1.");

        RuleFor(x => x.MaxValidators)
            .GreaterThanOrEqualTo(x => x.MinValidators)
            .WithMessage("MaxValidators must be >= MinValidators.")
            .LessThanOrEqualTo(100)
            .WithMessage("MaxValidators must be <= 100.");

        RuleFor(x => x.StakeAmount)
            .GreaterThan(0)
            .WithMessage("StakeAmount must be > 0 when staking is required.")
            .When(x => x.RequireStake);

        RuleFor(x => x.StakeAmount)
            .Null()
            .WithMessage("StakeAmount must be null when staking is not required.")
            .When(x => !x.RequireStake);

        RuleFor(x => x.OperationalTtlSeconds)
            .InclusiveBetween(10, 600)
            .WithMessage("OperationalTtlSeconds must be between 10 and 600.");
    }
}

/// <summary>
/// Validates individual <see cref="ApprovedValidator"/> entries within the approved validators list.
/// </summary>
public class ApprovedValidatorValidator : AbstractValidator<ApprovedValidator>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApprovedValidatorValidator"/> class.
    /// </summary>
    public ApprovedValidatorValidator()
    {
        RuleFor(x => x.Did)
            .NotEmpty()
            .WithMessage("Approved validator DID must not be empty.")
            .MaximumLength(255)
            .WithMessage("Approved validator DID must be at most 255 characters.");

        RuleFor(x => x.PublicKey)
            .NotEmpty()
            .WithMessage("Approved validator PublicKey must not be empty.");
    }
}

/// <summary>
/// Validates <see cref="PolicyConsensusConfig"/> settings including signature thresholds,
/// docket capacity, build interval, and timeout.
/// </summary>
public class PolicyConsensusConfigValidator : AbstractValidator<PolicyConsensusConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyConsensusConfigValidator"/> class.
    /// </summary>
    public PolicyConsensusConfigValidator()
    {
        RuleFor(x => x.SignatureThresholdMin)
            .GreaterThanOrEqualTo(1)
            .WithMessage("SignatureThresholdMin must be >= 1.");

        RuleFor(x => x.SignatureThresholdMax)
            .GreaterThanOrEqualTo(x => x.SignatureThresholdMin)
            .WithMessage("SignatureThresholdMax must be >= SignatureThresholdMin.");

        RuleFor(x => x.MaxTransactionsPerDocket)
            .InclusiveBetween(1, 10000)
            .WithMessage("MaxTransactionsPerDocket must be between 1 and 10000.");

        RuleFor(x => x.DocketBuildIntervalMs)
            .InclusiveBetween(10, 60000)
            .WithMessage("DocketBuildIntervalMs must be between 10 and 60000.");

        RuleFor(x => x.DocketTimeoutSeconds)
            .InclusiveBetween(5, 300)
            .WithMessage("DocketTimeoutSeconds must be between 5 and 300.");
    }
}

/// <summary>
/// Validates <see cref="PolicyLeaderElectionConfig"/> settings including election mechanism,
/// heartbeat interval, leader timeout, and optional term duration.
/// </summary>
public class PolicyLeaderElectionConfigValidator : AbstractValidator<PolicyLeaderElectionConfig>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyLeaderElectionConfigValidator"/> class.
    /// </summary>
    public PolicyLeaderElectionConfigValidator()
    {
        RuleFor(x => x.Mechanism)
            .IsInEnum()
            .WithMessage("Mechanism must be a defined enum value.");

        RuleFor(x => x.HeartbeatIntervalMs)
            .InclusiveBetween(100, 30000)
            .WithMessage("HeartbeatIntervalMs must be between 100 and 30000.");

        RuleFor(x => x.LeaderTimeoutMs)
            .GreaterThan(x => x.HeartbeatIntervalMs)
            .WithMessage("LeaderTimeoutMs must be greater than HeartbeatIntervalMs.");

        RuleFor(x => x.TermDurationSeconds)
            .GreaterThanOrEqualTo(10)
            .WithMessage("TermDurationSeconds must be >= 10 when set.")
            .When(x => x.TermDurationSeconds is not null);
    }
}
