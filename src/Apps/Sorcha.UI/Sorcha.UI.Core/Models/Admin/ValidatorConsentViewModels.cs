// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for a pending validator awaiting consent.
/// </summary>
public record PendingValidatorViewModel
{
    [JsonPropertyName("validatorId")] public string ValidatorId { get; init; } = string.Empty;
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("registerName")] public string RegisterName { get; init; } = string.Empty;
    [JsonPropertyName("requestedAt")] public DateTimeOffset RequestedAt { get; init; }
    [JsonPropertyName("endpoint")] public string Endpoint { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>
/// View model for the full consent queue grouped by register.
/// </summary>
public record ConsentQueueViewModel
{
    [JsonPropertyName("registers")] public List<RegisterConsentGroup> Registers { get; init; } = [];
    [JsonPropertyName("totalPending")] public int TotalPending { get; init; }
}

/// <summary>
/// A group of pending validators for a single register.
/// </summary>
public record RegisterConsentGroup
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("registerName")] public string RegisterName { get; init; } = string.Empty;
    [JsonPropertyName("registrationMode")] public string RegistrationMode { get; init; } = "Open";
    [JsonPropertyName("pendingValidators")] public List<PendingValidatorViewModel> PendingValidators { get; init; } = [];
}

// ApprovedValidatorInfo is defined in RegisterPolicyViewModels.cs
