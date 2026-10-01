// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Telemetry names for the system blueprint lifecycle (Feature 197, #1466). The instruments are
/// added by later tasks; the meter name is fixed here so the OpenTelemetry export list in
/// <c>Sorcha.ServiceDefaults</c> and the instruments cannot drift apart.
/// </summary>
public static class SystemBlueprintMetrics
{
    /// <summary>Meter name; must match the <c>AddMeter</c> registration in ServiceDefaults.</summary>
    public const string MeterName = "Sorcha.SystemBlueprints";
}
