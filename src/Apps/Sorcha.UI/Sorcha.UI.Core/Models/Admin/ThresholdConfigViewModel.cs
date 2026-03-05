// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public record ThresholdConfigViewModel
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("groupPublicKey")] public string GroupPublicKey { get; init; } = string.Empty;
    [JsonPropertyName("threshold")] public uint Threshold { get; init; }
    [JsonPropertyName("totalValidators")] public uint TotalValidators { get; init; }
    [JsonPropertyName("validatorIds")] public string[] ValidatorIds { get; init; } = [];
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("collectedShares")] public int? CollectedShares { get; init; }
}

public record ThresholdSetupRequest
{
    [JsonPropertyName("registerId")] public string RegisterId { get; set; } = string.Empty;
    [JsonPropertyName("threshold")] public uint Threshold { get; set; }
    [JsonPropertyName("totalValidators")] public uint TotalValidators { get; set; }
    [JsonPropertyName("validatorIds")] public string[] ValidatorIds { get; set; } = [];
}
