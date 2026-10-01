// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Telemetry names for the system blueprint lifecycle (Feature 197, #1466). The drift gauge is registered by
/// <see cref="SystemBlueprintDriftMonitor"/>; the meter name is fixed here so the OpenTelemetry export list in
/// <c>Sorcha.ServiceDefaults</c> and the instruments cannot drift apart.
/// </summary>
public static class SystemBlueprintMetrics
{
    /// <summary>Meter name; must match the <c>AddMeter</c> registration in ServiceDefaults.</summary>
    public const string MeterName = "Sorcha.SystemBlueprints";

    /// <summary>Observable gauge: 1 per blueprint for its current drift state.</summary>
    public const string DriftGaugeName = "sorcha_system_blueprint_drift";

    /// <summary>Counter of operator publish attempts, tagged <c>outcome</c> (see <see cref="PublishOutcomeNames"/>).</summary>
    public const string PublishCounterName = "sorcha_system_blueprint_publish_total";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> PublishCounter = Meter.CreateCounter<long>(
        PublishCounterName,
        description: "Operator system blueprint publish attempts by outcome");

    /// <summary>Counts one operator publish attempt under <paramref name="outcomeTag"/> (a <see cref="PublishOutcomeNames"/> value).</summary>
    /// <param name="outcomeTag">The outcome tag value.</param>
    public static void RecordPublish(string outcomeTag)
        => PublishCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcomeTag));
}
