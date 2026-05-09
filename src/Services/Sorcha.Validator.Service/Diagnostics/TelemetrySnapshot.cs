// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Validator.Service.Diagnostics;

/// <summary>
/// JSON-serialisable snapshot of the validator telemetry collector. Written by
/// the capture harness to <c>bench/baseline-2026-05/walkthrough-runs/...</c>.
/// </summary>
public sealed class TelemetrySnapshot
{
    public string? CaptureLabel { get; init; }
    public long StartedAtUnixMs { get; init; }
    public long EndedAtUnixMs { get; init; }
    public long TickFrequency { get; init; }
    public Dictionary<string, RuleSnapshot> Sections { get; init; } = new();
    public Dictionary<string, RuleSnapshot> Rules { get; init; } = new();
}

public sealed class RuleSnapshot
{
    public long Evaluations { get; init; }
    public long Emissions { get; init; }
    public long TotalNanos { get; init; }
    public long MinNanos { get; init; }
    public long MaxNanos { get; init; }
    public long P50Nanos { get; init; }
    public long P95Nanos { get; init; }
    public long P99Nanos { get; init; }
}

[JsonSerializable(typeof(TelemetrySnapshot))]
[JsonSerializable(typeof(RuleSnapshot))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SnapshotJsonContext : JsonSerializerContext
{
}
