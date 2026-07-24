// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using SorchaRegisterPolicy = Sorcha.Register.Models.RegisterPolicy;

namespace Sorcha.Cli.Models;

/// <summary>
/// Register policy response from the API.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>Sorcha.Register.Service.Endpoints.RegisterPolicyResponse</c>; the pairing is asserted
/// by <c>CliWireContractTests</c>.
/// </para>
/// <para>
/// The whole policy family was previously modelled <b>flat</b> — <c>minValidators</c>,
/// <c>maxValidators</c>, <c>signatureThreshold</c>, <c>registrationMode</c> and so on as scalars on
/// the response. The server has never sent that shape: it nests the real
/// <see cref="SorchaRegisterPolicy"/> under a <c>policy</c> property, with validator and consensus
/// settings inside their own sub-objects. So <c>sorcha register policy get</c> deserialised every
/// field to its default and confidently printed a policy of all zeros.
/// </para>
/// <para>
/// The fix is to carry the real model rather than re-describe it. The CLI already references
/// <c>Sorcha.Register.Models</c>, so it can use the same <c>RegisterPolicy</c> the server
/// serialises — which also means nested policy changes cannot drift again.
/// </para>
/// </remarks>
public record RegisterPolicyResponse
{
    /// <summary>Register the policy belongs to.</summary>
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>The effective policy.</summary>
    public SorchaRegisterPolicy Policy { get; init; } = new();

    /// <summary>True when no explicit policy is on-chain and defaults were generated.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// Policy version history response.
/// </summary>
public record PolicyHistoryResponse
{
    /// <summary>Register the history belongs to.</summary>
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>The policy versions on this page.</summary>
    public List<PolicyVersionEntry> Versions { get; init; } = [];

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used for this query.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Total versions across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages available.</summary>
    public int TotalPages { get; init; }
}

/// <summary>
/// A single policy version entry in the history.
/// </summary>
public record PolicyVersionEntry
{
    /// <summary>The policy version number.</summary>
    public uint Version { get; init; }

    /// <summary>The policy as it stood at this version.</summary>
    public SorchaRegisterPolicy Policy { get; init; } = new();

    /// <summary>When this version was committed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>DID of whoever proposed this version.</summary>
    public string? UpdatedBy { get; init; }
}

/// <summary>
/// Request to propose a register policy update.
/// </summary>
/// <remarks>
/// The CLI previously sent flat scalars (<c>minValidators</c> and friends) that the server does not
/// bind, so <c>policy</c> arrived null and the proposal did nothing.
/// </remarks>
public record PolicyUpdateRequest
{
    /// <summary>The proposed policy, with its version incremented.</summary>
    public SorchaRegisterPolicy? Policy { get; init; }

    /// <summary>
    /// Transition mode when moving <c>registrationMode</c> from public to consent. Serialised as a
    /// string; the server binds it to its TransitionMode enum.
    /// </summary>
    public string? TransitionMode { get; init; }

    /// <summary>DID of the proposer.</summary>
    public string UpdatedBy { get; init; } = string.Empty;
}

/// <summary>
/// Response after proposing a policy update.
/// </summary>
/// <remarks>
/// The CLI previously expected <c>proposalId</c>, <c>status</c>, <c>requiredVotes</c> and
/// <c>currentVotes</c> — a governance-vote shape this endpoint does not return. It returns the
/// version numbers and whether a governance vote is required at all.
/// </remarks>
public record PolicyUpdateResponse
{
    /// <summary>Register the proposal applies to.</summary>
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>The version being proposed.</summary>
    public uint ProposedVersion { get; init; }

    /// <summary>The version currently in force.</summary>
    public uint CurrentVersion { get; init; }

    /// <summary>Whether the change needs a governance vote before it takes effect.</summary>
    public bool RequiresGovernanceVote { get; init; }

    /// <summary>Server-supplied outcome message.</summary>
    public string Message { get; init; } = string.Empty;
}
