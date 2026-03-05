// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model representing the current policy of a register.
/// </summary>
public record RegisterPolicyViewModel
{
    /// <summary>
    /// The register this policy belongs to.
    /// </summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>
    /// Minimum number of validators required.
    /// </summary>
    [JsonPropertyName("minValidators")]
    public int MinValidators { get; init; }

    /// <summary>
    /// Maximum number of validators allowed.
    /// </summary>
    [JsonPropertyName("maxValidators")]
    public int MaxValidators { get; init; }

    /// <summary>
    /// Number of validator signatures required for consensus.
    /// </summary>
    [JsonPropertyName("signatureThreshold")]
    public int SignatureThreshold { get; init; }

    /// <summary>
    /// How validators register: "Open" or "Consent".
    /// </summary>
    [JsonPropertyName("registrationMode")]
    public string RegistrationMode { get; init; } = "Open";

    /// <summary>
    /// How policy changes take effect: "Immediate" or "Epoch".
    /// </summary>
    [JsonPropertyName("transitionMode")]
    public string TransitionMode { get; init; } = "Immediate";

    /// <summary>
    /// Policy version number.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// When this policy version was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Who last updated this policy.
    /// </summary>
    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; init; } = string.Empty;

    /// <summary>
    /// List of approved validators for this register.
    /// </summary>
    [JsonPropertyName("approvedValidators")]
    public List<ApprovedValidatorInfo> ApprovedValidators { get; init; } = [];
}

/// <summary>
/// Editable fields for proposing a policy update.
/// </summary>
public record RegisterPolicyFields
{
    /// <summary>
    /// Minimum number of validators required.
    /// </summary>
    [JsonPropertyName("minValidators")]
    public int MinValidators { get; set; }

    /// <summary>
    /// Maximum number of validators allowed.
    /// </summary>
    [JsonPropertyName("maxValidators")]
    public int MaxValidators { get; set; }

    /// <summary>
    /// Number of validator signatures required for consensus.
    /// </summary>
    [JsonPropertyName("signatureThreshold")]
    public int SignatureThreshold { get; set; }

    /// <summary>
    /// How validators register: "Open" or "Consent".
    /// </summary>
    [JsonPropertyName("registrationMode")]
    public string RegistrationMode { get; set; } = "Open";

    /// <summary>
    /// How policy changes take effect: "Immediate" or "Epoch".
    /// </summary>
    [JsonPropertyName("transitionMode")]
    public string TransitionMode { get; set; } = "Immediate";
}

/// <summary>
/// Information about an approved validator.
/// </summary>
public record ApprovedValidatorInfo
{
    /// <summary>
    /// Validator identifier.
    /// </summary>
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; init; } = string.Empty;

    /// <summary>
    /// Validator display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// When the validator was approved.
    /// </summary>
    [JsonPropertyName("approvedAt")]
    public DateTimeOffset ApprovedAt { get; init; }
}

/// <summary>
/// A single version entry in policy history.
/// </summary>
public record PolicyVersionViewModel
{
    /// <summary>
    /// Policy version number.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// Who made this update.
    /// </summary>
    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; init; } = string.Empty;

    /// <summary>
    /// When this version was created.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Paginated list of policy history versions.
/// </summary>
public record PolicyHistoryViewModel
{
    /// <summary>
    /// The register this history belongs to.
    /// </summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>
    /// Version entries for this page.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<PolicyVersionViewModel> Versions { get; init; } = [];

    /// <summary>
    /// Total number of versions.
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size.
    /// </summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Result of proposing a policy update.
/// </summary>
public record PolicyUpdateProposalViewModel
{
    /// <summary>
    /// Unique proposal identifier.
    /// </summary>
    [JsonPropertyName("proposalId")]
    public string ProposalId { get; init; } = string.Empty;

    /// <summary>
    /// The register this proposal targets.
    /// </summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>
    /// The version number this proposal would create.
    /// </summary>
    [JsonPropertyName("proposedVersion")]
    public int ProposedVersion { get; init; }

    /// <summary>
    /// Current proposal status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Number of votes required for approval.
    /// </summary>
    [JsonPropertyName("requiredVotes")]
    public int RequiredVotes { get; init; }

    /// <summary>
    /// Number of votes received so far.
    /// </summary>
    [JsonPropertyName("currentVotes")]
    public int CurrentVotes { get; init; }
}
