// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// A validator's decision on a proposed docket. This is the <b>single</b> declaration of the vote
/// decision across the platform, and its numeric values are a persisted ledger contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives here (Feature 187 / issue #1371).</b> This enum was previously declared TWICE —
/// <c>Sorcha.Validator.Core.Validators.VoteDecision</c> and
/// <c>Sorcha.Validator.Service.Models.VoteDecision</c> — with <b>incompatible numeric values</b>:
/// Core had <c>Approve=1, Reject=2, Abstain=3</c>, Service had <c>Reject=0, Approve=1</c> and no
/// Abstain. Validator.Service references Validator.Core, so both were in scope in the same assembly
/// and were being told apart only by namespace qualification (see the <c>Models.VoteDecision.Approve</c>
/// qualification that used to be required in <c>ValidatorGrpcService</c>). A value crossing between
/// them numerically turned a <b>Reject into something else</b>, silently. Consensus votes are now
/// persisted to the ledger, so the values are a wire contract and the duplication had to end.
/// </para>
/// <para>
/// <b>Never renumber these.</b> They are written to the register. Adding a member is additive and
/// safe; changing an existing value silently reinterprets already-sealed dockets. Pinned by
/// <c>ConsensusVoteContractTests</c> and guarded against re-declaration by
/// <c>scripts/check-consensus-vote-contract.ps1</c>.
/// </para>
/// <para>
/// <b><see cref="Unspecified"/> is deliberately 0.</b> Neither historical enum reserved zero, so
/// <c>default(VoteDecision)</c> silently meant "Reject" under the Service numbering — a
/// fail-dangerous default for a value that decides whether a docket seals. Zero is now a
/// non-decision that every consumer must reject explicitly (protobuf convention).
/// </para>
/// </remarks>
public enum VoteDecision
{
    /// <summary>
    /// No decision recorded. Never a valid vote — this is the zero value, so an uninitialised or
    /// malformed vote lands here rather than being read as a real Approve or Reject.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Vote to approve the docket (validation passed).
    /// </summary>
    Approve = 1,

    /// <summary>
    /// Vote to reject the docket (validation failed). Carries a rejection reason.
    /// </summary>
    Reject = 2,

    /// <summary>
    /// Abstain from voting — the validator neither approves nor rejects, and the vote carries no
    /// quorum weight in either direction.
    /// </summary>
    Abstain = 3
}
