// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public record PendingValidatorViewModel
{
    [JsonPropertyName("validatorId")] public string ValidatorId { get; init; } = string.Empty;
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("registerName")] public string RegisterName { get; init; } = string.Empty;
    [JsonPropertyName("requestedAt")] public DateTimeOffset RequestedAt { get; init; }
    public bool IsSelected { get; set; }
}

public record ConsentQueueViewModel
{
    [JsonPropertyName("registerGroups")] public List<RegisterConsentGroup> RegisterGroups { get; init; } = [];
    [JsonPropertyName("totalPending")] public int TotalPending { get; init; }
}

public record RegisterConsentGroup
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("registerName")] public string RegisterName { get; init; } = string.Empty;
    [JsonPropertyName("registrationMode")] public string RegistrationMode { get; init; } = string.Empty;
    [JsonPropertyName("pendingValidators")] public List<PendingValidatorViewModel> PendingValidators { get; init; } = [];
    [JsonPropertyName("approvedValidators")] public List<ApprovedValidatorInfo> ApprovedValidators { get; init; } = [];
}
