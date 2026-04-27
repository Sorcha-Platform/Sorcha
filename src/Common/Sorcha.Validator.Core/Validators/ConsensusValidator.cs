// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Core.Models;

namespace Sorcha.Validator.Core.Validators;

/// <summary>
/// Pure validator for consensus votes and quorum calculations
/// This validator is stateless and can run in secure enclaves (Intel SGX, AMD SEV)
/// </summary>
public class ConsensusValidator : IConsensusValidator
{
    /// <inheritdoc/>
    public ValidationResult ValidateVoteStructure(ConsensusVoteData vote, string docketHash)
    {
        var errors = new List<ValidationError>();

        // Validate ValidatorId
        if (string.IsNullOrWhiteSpace(vote.ValidatorId))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_001",
                Message = "Validator ID is required",
                Field = nameof(vote.ValidatorId)
            });
        }

        // Validate DocketHash
        if (string.IsNullOrWhiteSpace(vote.DocketHash))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_002",
                Message = "Docket hash is required",
                Field = nameof(vote.DocketHash)
            });
        }
        else if (!string.Equals(vote.DocketHash, docketHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_003",
                Message = $"Vote docket hash mismatch. Expected: {docketHash}, Actual: {vote.DocketHash}",
                Field = nameof(vote.DocketHash)
            });
        }

        // Validate Decision is a valid enum value
        if (!Enum.IsDefined(typeof(VoteDecision), vote.Decision))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_004",
                Message = $"Invalid vote decision: {vote.Decision}",
                Field = nameof(vote.Decision)
            });
        }

        // Validate Signature
        if (string.IsNullOrWhiteSpace(vote.Signature))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_005",
                Message = "Vote signature is required",
                Field = nameof(vote.Signature)
            });
        }

        // Validate timestamp is not in the future (allow 5 minute clock skew)
        if (vote.VotedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_006",
                Message = "Vote timestamp cannot be in the future",
                Field = nameof(vote.VotedAt)
            });
        }

        // Validate rejection reason is provided for rejection votes
        if (vote.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(vote.RejectionReason))
        {
            errors.Add(new ValidationError
            {
                Code = "CV_007",
                Message = "Rejection reason is required for reject votes",
                Field = nameof(vote.RejectionReason)
            });
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public ValidationResult ValidateQuorum(int approvalCount, int totalValidators, double requiredThreshold)
    {
        var errors = new List<ValidationError>();

        // Validate inputs
        if (approvalCount < 0)
        {
            errors.Add(new ValidationError
            {
                Code = "CV_008",
                Message = "Approval count cannot be negative",
                Field = nameof(approvalCount)
            });
        }

        if (totalValidators <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "CV_009",
                Message = "Total validators must be greater than zero",
                Field = nameof(totalValidators)
            });
        }

        if (requiredThreshold < 0.0 || requiredThreshold > 1.0)
        {
            errors.Add(new ValidationError
            {
                Code = "CV_010",
                Message = "Required threshold must be between 0.0 and 1.0",
                Field = nameof(requiredThreshold)
            });
        }

        if (approvalCount > totalValidators)
        {
            errors.Add(new ValidationError
            {
                Code = "CV_011",
                Message = "Approval count cannot exceed total validators",
                Field = nameof(approvalCount)
            });
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        // Calculate actual approval percentage
        var approvalPercentage = (double)approvalCount / totalValidators;

        // Check if quorum is met (must be strictly greater than threshold)
        if (approvalPercentage <= requiredThreshold)
        {
            return ValidationResult.Failure(
                "CV_012",
                FormattableString.Invariant(
                    $"Quorum not met. Approval: {approvalPercentage * 100:F2}% ({approvalCount}/{totalValidators}), Required: >{requiredThreshold * 100:F2}%"),
                "Quorum");
        }

        return ValidationResult.Success(new Dictionary<string, object>
        {
            ["ApprovalPercentage"] = approvalPercentage,
            ["ApprovalCount"] = approvalCount,
            ["TotalValidators"] = totalValidators,
            ["RequiredThreshold"] = requiredThreshold
        });
    }

    /// <inheritdoc/>
    public ValidationResult ValidateVoteCollection(IEnumerable<ConsensusVoteData> votes, string docketHash)
    {
        var errors = new List<ValidationError>();
        var voteList = votes.ToList();

        if (voteList.Count == 0)
        {
            return ValidationResult.Failure("CV_013", "Vote collection cannot be empty", nameof(votes));
        }

        // Check for duplicate validator votes
        var validatorVotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var vote in voteList)
        {
            if (!validatorVotes.Add(vote.ValidatorId))
            {
                duplicates.Add(vote.ValidatorId);
            }
        }

        if (duplicates.Count > 0)
        {
            errors.Add(new ValidationError
            {
                Code = "CV_014",
                Message = $"Duplicate votes detected from validators: {string.Join(", ", duplicates)}",
                Field = nameof(votes)
            });
        }

        // Validate each vote structure
        for (int i = 0; i < voteList.Count; i++)
        {
            var voteResult = ValidateVoteStructure(voteList[i], docketHash);
            if (!voteResult.IsValid)
            {
                foreach (var error in voteResult.Errors)
                {
                    errors.Add(new ValidationError
                    {
                        Code = error.Code,
                        Message = $"Vote[{i}]: {error.Message}",
                        Field = $"{nameof(votes)}[{i}].{error.Field}"
                    });
                }
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public ValidationResult CheckConsensusAchievement(
        IEnumerable<ConsensusVoteData> votes,
        int totalValidators,
        double approvalThreshold)
    {
        var errors = new List<ValidationError>();
        var voteList = votes.ToList();

        // Validate inputs
        if (totalValidators <= 0)
        {
            return ValidationResult.Failure("CV_015", "Total validators must be greater than zero", nameof(totalValidators));
        }

        if (approvalThreshold < 0.0 || approvalThreshold > 1.0)
        {
            return ValidationResult.Failure("CV_016", "Approval threshold must be between 0.0 and 1.0", nameof(approvalThreshold));
        }

        // Count votes by decision
        var approvalCount = voteList.Count(v => v.Decision == VoteDecision.Approve);
        var rejectCount = voteList.Count(v => v.Decision == VoteDecision.Reject);
        var abstainCount = voteList.Count(v => v.Decision == VoteDecision.Abstain);

        // Validate quorum
        var quorumResult = ValidateQuorum(approvalCount, totalValidators, approvalThreshold);

        if (!quorumResult.IsValid)
        {
            // Consensus not achieved
            return new ValidationResult
            {
                IsValid = false,
                Errors = new[]
                {
                    new ValidationError
                    {
                        Code = "CV_017",
                        Message = $"Consensus not achieved. {quorumResult.Errors[0].Message}",
                        Field = "Consensus"
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    ["ApprovalCount"] = approvalCount,
                    ["RejectCount"] = rejectCount,
                    ["AbstainCount"] = abstainCount,
                    ["TotalValidators"] = totalValidators,
                    ["ApprovalThreshold"] = approvalThreshold,
                    ["ConsensusAchieved"] = false
                }
            };
        }

        // Consensus achieved
        return ValidationResult.Success(new Dictionary<string, object>
        {
            ["ConsensusAchieved"] = true,
            ["ApprovalCount"] = approvalCount,
            ["RejectCount"] = rejectCount,
            ["AbstainCount"] = abstainCount,
            ["TotalValidators"] = totalValidators,
            ["ApprovalPercentage"] = (double)approvalCount / totalValidators,
            ["ApprovalThreshold"] = approvalThreshold
        });
    }
}

