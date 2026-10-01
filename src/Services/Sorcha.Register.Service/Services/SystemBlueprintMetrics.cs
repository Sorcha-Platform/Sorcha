// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}
