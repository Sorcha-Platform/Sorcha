// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Blueprints;

/// <summary>
/// Immutable state record for the publish blueprint wizard.
/// </summary>
public record PublishWizardState
{
    // Step 0: Validation
    public bool IsValidating { get; init; }
    public bool IsValid { get; init; }
    public List<ValidationIssue> ValidationResults { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    // Step 1: Register selection
    public string? SelectedRegisterId { get; init; }
    public string? SelectedRegisterName { get; init; }

    // Step 2: Rights check
    public bool IsCheckingRights { get; init; }
    public bool HasPublishRights { get; init; }
    public string? UserRole { get; init; }
    public GovernanceRosterViewModel? Roster { get; init; }
    public string? RightsError { get; init; }

    // Step 3: Publishing
    public bool IsPublishing { get; init; }
    public bool IsPublished { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the wizard can proceed from the current step.
    /// </summary>
    public bool CanProceed(int step) => step switch
    {
        0 => IsValid && !IsValidating,
        1 => !string.IsNullOrEmpty(SelectedRegisterId),
        2 => HasPublishRights && !IsCheckingRights,
        3 => true,
        _ => false
    };
}

/// <summary>
/// DTO for the governance roster API response.
/// </summary>
public record GovernanceRosterViewModel
{
    public string RegisterId { get; init; } = string.Empty;
    public List<RosterMemberViewModel> Members { get; init; } = [];
    public int MemberCount { get; init; }
    public int ControlTransactionCount { get; init; }
    public string? LastControlTxId { get; init; }
}

/// <summary>
/// A member of a register's governance roster.
/// </summary>
public record RosterMemberViewModel
{
    public string Subject { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public DateTimeOffset GrantedAt { get; init; }
}

/// <summary>
/// Response from the blueprint validation endpoint.
/// </summary>
public record BlueprintValidationResponse
{
    public string BlueprintId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public List<ValidationIssue> ValidationResults { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
