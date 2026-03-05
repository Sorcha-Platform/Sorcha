// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cli.Models;

/// <summary>
/// Placeholder for aggregated validator metrics.
/// CLI commands use JsonDocument for flexible deserialization from HttpResponseMessage.
/// </summary>
public record ValidatorMetricsSummary;

/// <summary>
/// Threshold setup request sent to the validator service.
/// </summary>
public record ThresholdSetupRequest
{
    public string RegisterId { get; init; } = string.Empty;
    public int Threshold { get; init; }
    public int TotalValidators { get; init; }
    public List<string> ValidatorIds { get; init; } = [];
}
