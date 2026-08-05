// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
using Sorcha.Validator.Core.Models;

namespace Sorcha.Validator.Core.Validators;

/// <summary>
/// Pure validator for consensus votes and quorum calculations
/// This validator is stateless and can run in secure enclaves (Intel SGX, AMD SEV)
/// </summary>
public interface IConsensusValidator
{
    /// <summary>
    /// Validates a consensus vote structure and integrity
    /// </summary>
    /// <param name="vote">Vote to validate</param>
    /// <param name="docketHash">Expected docket hash being voted on</param>
    /// <returns>Validation result with errors if any</returns>
    ValidationResult ValidateVoteStructure(ConsensusVoteData vote, string docketHash);

    /// <summary>
    /// Validates that quorum requirements are met
    /// </summary>
    /// <param name="approvalCount">Number of approval votes</param>
    /// <param name="totalValidators">Total number of validators</param>
    /// <param name="requiredThreshold">Required approval threshold (0.0-1.0)</param>
    /// <returns>Validation result with errors if any</returns>
    ValidationResult ValidateQuorum(int approvalCount, int totalValidators, double requiredThreshold);

    /// <summary>
    /// Validates a collection of votes for consistency
    /// </summary>
    /// <param name="votes">Votes to validate</param>
    /// <param name="docketHash">Expected docket hash</param>
    /// <returns>Validation result with errors if any</returns>
    ValidationResult ValidateVoteCollection(IEnumerable<ConsensusVoteData> votes, string docketHash);

    /// <summary>
    /// Checks if consensus is achieved based on votes and threshold
    /// </summary>
    /// <param name="votes">All votes cast</param>
    /// <param name="totalValidators">Total number of validators</param>
    /// <param name="approvalThreshold">Required approval threshold (0.0-1.0)</param>
    /// <returns>Validation result indicating consensus achievement</returns>
    ValidationResult CheckConsensusAchievement(
        IEnumerable<ConsensusVoteData> votes,
        int totalValidators,
        double approvalThreshold);
}

/// <summary>
/// Pure data structure for consensus vote validation (no service dependencies)
/// </summary>
public record ConsensusVoteData
{
    /// <summary>
    /// Validator ID who cast the vote
    /// </summary>
    public required string ValidatorId { get; init; }

    /// <summary>
    /// Docket hash being voted on
    /// </summary>
    public required string DocketHash { get; init; }

    /// <summary>
    /// Vote decision (Approve/Reject/Abstain)
    /// </summary>
    public required VoteDecision Decision { get; init; }

    /// <summary>
    /// Timestamp when vote was cast
    /// </summary>
    public required DateTimeOffset VotedAt { get; init; }

    /// <summary>
    /// Validator's signature on the vote
    /// </summary>
    public required string Signature { get; init; }

    /// <summary>
    /// Optional reason for rejection
    /// </summary>
    public string? RejectionReason { get; init; }
}

