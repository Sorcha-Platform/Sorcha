// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Manages distributed consensus for proposed dockets
/// </summary>
/// <remarks>
/// The consensus engine coordinates the voting process across validator instances.
/// It distributes proposed dockets to peers, collects votes, determines when
/// consensus threshold (>50%) is achieved, and handles timeout scenarios.
///
/// <para><b>Key Responsibilities:</b></para>
/// <list type="bullet">
///   <item>Query Peer Service for list of active validators</item>
///   <item>Distribute proposed dockets to peer validators</item>
///   <item>Collect and validate votes from peers</item>
///   <item>Determine consensus achievement (threshold-based)</item>
///   <item>Handle consensus timeout and failures</item>
///   <item>Report malicious validator behavior to Peer Service</item>
/// </list>
///
/// <para><b>User Story:</b> US3 - Distributed Consensus Achievement (P1)</para>
/// </remarks>
public interface IConsensusEngine
{
    /// <summary>
    /// Achieves consensus on a proposed docket by collecting votes from peer validators
    /// </summary>
    /// <param name="docket">The proposed docket to achieve consensus on</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consensus result indicating success/failure and collected votes</returns>
    /// <remarks>
    /// This method orchestrates the entire consensus process:
    /// <list type="number">
    ///   <item>Queries Peer Service for active validators with reputation scores</item>
    ///   <item>Distributes docket to peers via Peer Service pub/sub</item>
    ///   <item>Collects votes using direct gRPC calls with Task.WhenAll pattern</item>
    ///   <item>Validates vote signatures and detects double-votes</item>
    ///   <item>Determines if >50% threshold is met</item>
    ///   <item>Reports invalid proposers to Peer Service</item>
    /// </list>
    ///
    /// <para><b>Acceptance Criteria:</b></para>
    /// <list type="bullet">
    ///   <item>AS1: Query Peer Service for validators</item>
    ///   <item>AS2: Distribute docket to peers</item>
    ///   <item>AS3: Receive and validate signed votes</item>
    ///   <item>AS4: Achieve consensus at >50%</item>
    ///   <item>AS5: Fail consensus on timeout</item>
    ///   <item>AS6: Include consensus signatures in confirmed docket</item>
    ///   <item>AS7: Return transactions to memory pool on failure</item>
    ///   <item>AS8: Report invalid proposers to Peer Service</item>
    /// </list>
    /// </remarks>
    Task<ConsensusResult> AchieveConsensusAsync(
        Docket docket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a proposed docket and generates a vote
    /// </summary>
    /// <param name="docket">The proposed docket to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consensus vote (approve or reject with reason)</returns>
    /// <remarks>
    /// This method is called when a validator receives a vote request from a peer.
    /// It performs independent validation and returns a signed vote.
    ///
    /// <para><b>Validation Checks:</b></para>
    /// <list type="bullet">
    ///   <item>Docket structure and hash integrity</item>
    ///   <item>Previous hash linkage to known chain</item>
    ///   <item>Transaction signatures and blueprint compliance</item>
    ///   <item>Proposer signature validity</item>
    ///   <item>Docket number sequencing</item>
    ///   <item>Timestamp monotonicity</item>
    /// </list>
    /// </remarks>
    Task<ConsensusVote> ValidateAndVoteAsync(
        Docket docket,
        CancellationToken cancellationToken = default);
}
