// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for per-register threshold signing configuration.
/// </summary>
public record ThresholdConfigViewModel
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    [JsonPropertyName("registerName")]
    public string RegisterName { get; init; } = string.Empty;

    [JsonPropertyName("groupPublicKey")]
    public string GroupPublicKey { get; init; } = string.Empty;

    [JsonPropertyName("threshold")]
    public int Threshold { get; init; }

    [JsonPropertyName("totalValidators")]
    public int TotalValidators { get; init; }

    [JsonPropertyName("validatorIds")]
    public string[] ValidatorIds { get; init; } = [];

    [JsonPropertyName("isConfigured")]
    public bool IsConfigured { get; init; }

    [JsonPropertyName("collectedShares")]
    public int CollectedShares { get; init; }
}

/// <summary>
/// Request model for setting up threshold signing on a register.
/// </summary>
public record ThresholdSetupRequest
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public int Threshold { get; set; }

    [JsonPropertyName("totalValidators")]
    public int TotalValidators { get; set; }

    [JsonPropertyName("validatorIds")]
    public string[] ValidatorIds { get; set; } = [];
}
