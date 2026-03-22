// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for consensus mechanism
/// </summary>
public class ConsensusConfiguration
{
    /// <summary>
    /// Approval threshold for consensus (>50% = 0.51)
    /// </summary>
    public double ApprovalThreshold { get; set; } = 0.51;

    /// <summary>
    /// Timeout duration for vote collection
    /// </summary>
    public TimeSpan VoteTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum retry attempts for failed consensus
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to require quorum (fail if &lt;50% validators reachable)
    /// </summary>
    public bool RequireQuorum { get; set; } = true;

    /// <summary>
    /// When true, auto-approve dockets if no other validators are found in the peer network.
    /// Enables single-node operation without requiring a peer network for consensus.
    /// WARNING: Must only be used in Development environments. Production deployments
    /// require multi-validator consensus — this flag is ignored unless ASPNETCORE_ENVIRONMENT=Development.
    /// </summary>
    public bool SingleValidatorAutoApprove { get; set; } = true;
}
