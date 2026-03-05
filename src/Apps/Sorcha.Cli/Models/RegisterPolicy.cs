// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cli.Models;

/// <summary>
/// Register policy response from the API.
/// </summary>
public record RegisterPolicyResponse
{
    public string RegisterId { get; init; } = string.Empty;
    public int MinValidators { get; init; }
    public int MaxValidators { get; init; }
    public int SignatureThreshold { get; init; }
    public string RegistrationMode { get; init; } = "Open";
    public string TransitionMode { get; init; } = "Immediate";
    public int Version { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
}

/// <summary>
/// Policy version history response.
/// </summary>
public record PolicyHistoryResponse
{
    public string RegisterId { get; init; } = string.Empty;
    public List<PolicyVersionEntry> Versions { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// A single policy version entry in the history.
/// </summary>
public record PolicyVersionEntry
{
    public int Version { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Response after proposing a policy update.
/// </summary>
public record PolicyUpdateResponse
{
    public string ProposalId { get; init; } = string.Empty;
    public int ProposedVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RequiredVotes { get; init; }
    public int CurrentVotes { get; init; }
}
