// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// Result of a consensus attempt on a proposed docket
/// </summary>
public class ConsensusResult
{
    /// <summary>
    /// Whether consensus was achieved (>50% approval)
    /// </summary>
    public required bool Achieved { get; init; }

    /// <summary>
    /// The docket that consensus was attempted for
    /// </summary>
    public required Docket Docket { get; init; }

    /// <summary>
    /// All votes collected from validators
    /// </summary>
    public required IReadOnlyList<ConsensusVote> Votes { get; init; }

    /// <summary>
    /// Number of validators that approved the docket
    /// </summary>
    public int ApprovalCount => Votes.Count(v => v.Decision == VoteDecision.Approve);

    /// <summary>
    /// Number of validators that rejected the docket
    /// </summary>
    public int RejectionCount => Votes.Count(v => v.Decision == VoteDecision.Reject);

    /// <summary>
    /// Total number of validators that participated
    /// </summary>
    public int TotalValidators { get; init; }

    /// <summary>
    /// Approval percentage (ApprovalCount / TotalValidators)
    /// </summary>
    public double ApprovalPercentage => TotalValidators > 0
        ? (double)ApprovalCount / TotalValidators
        : 0;

    /// <summary>
    /// Reason for consensus failure (if Achieved == false)
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Duration of consensus process
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// When consensus completed (or failed)
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }
}
