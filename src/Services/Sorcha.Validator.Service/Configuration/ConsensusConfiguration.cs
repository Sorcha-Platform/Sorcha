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

    /// <summary>
    /// Accept-to-seal deadline, in seconds, before a validator that accepted work but did not
    /// seal it is treated as withholding and ejected via a sealed
    /// <c>control.validator.liveness-violation</c> (Feature 138 US3, FR-013). Mirrors the
    /// register policy's <c>DocketTimeoutSeconds</c>; default 30s.
    /// </summary>
    public int LivenessTimeoutSeconds { get; set; } = 30;
}
