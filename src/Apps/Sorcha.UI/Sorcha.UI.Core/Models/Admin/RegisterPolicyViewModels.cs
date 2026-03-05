// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public record RegisterPolicyViewModel
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("policy")] public RegisterPolicyFields Policy { get; init; } = new();
    [JsonPropertyName("isDefault")] public bool IsDefault { get; init; }
    [JsonPropertyName("version")] public uint Version { get; init; }
}

public record RegisterPolicyFields
{
    [JsonPropertyName("minimumValidators")] public uint MinimumValidators { get; set; } = 1;
    [JsonPropertyName("maximumValidators")] public uint MaximumValidators { get; set; } = 10;
    [JsonPropertyName("signatureThreshold")] public uint SignatureThreshold { get; set; } = 1;
    [JsonPropertyName("registrationMode")] public string RegistrationMode { get; set; } = "Open";
    [JsonPropertyName("approvedValidators")] public List<ApprovedValidatorInfo> ApprovedValidators { get; set; } = [];
    [JsonPropertyName("transitionMode")] public string? TransitionMode { get; set; }
}

public record ApprovedValidatorInfo
{
    [JsonPropertyName("validatorId")] public string ValidatorId { get; init; } = string.Empty;
    [JsonPropertyName("approvedAt")] public DateTimeOffset ApprovedAt { get; init; }
}

public record PolicyVersionViewModel
{
    [JsonPropertyName("version")] public uint Version { get; init; }
    [JsonPropertyName("policy")] public RegisterPolicyFields Policy { get; init; } = new();
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; init; }
}

public record PolicyHistoryViewModel
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("versions")] public List<PolicyVersionViewModel> Versions { get; init; } = [];
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
}

public record PolicyUpdateProposalViewModel
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("proposedVersion")] public uint ProposedVersion { get; init; }
    [JsonPropertyName("currentVersion")] public uint CurrentVersion { get; init; }
    [JsonPropertyName("requiresGovernanceVote")] public bool RequiresGovernanceVote { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}
