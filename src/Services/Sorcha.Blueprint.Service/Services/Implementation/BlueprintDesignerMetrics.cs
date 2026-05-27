// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for the Feature 142 Blueprint Design Lifecycle (designer
/// workspace, rehearsal harness, and governed Go-live). Instruments are registered
/// with the <c>Sorcha.Blueprint.Designer</c> meter so they surface via the standard
/// exporter configured in ServiceDefaults (the meter name is added to the export
/// allowlist in <c>Sorcha.ServiceDefaults.Extensions</c>).
/// </summary>
/// <remarks>
/// Scaffold stub introduced by T003. The meter source is created here so the
/// allowlist entry is live; the concrete instruments
/// (<c>rehearsal_run_total{mode,outcome}</c>, <c>rehearsal_duration_seconds</c>,
/// <c>publish_override_total</c>, <c>sandbox_provision_total</c>) are added in the
/// Polish phase (T058), at which point the rehearsal/publish services inject and
/// record against them.
/// </remarks>
public sealed class BlueprintDesignerMetrics
{
    /// <summary>Meter name used when publishing Blueprint-designer-lifecycle metrics.</summary>
    public const string MeterName = "Sorcha.Blueprint.Designer";

    private readonly Meter _meter;

    public BlueprintDesignerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName, "1.0.0");

        // Instruments added in T058 (Polish). The meter is created eagerly so the
        // ServiceDefaults export allowlist entry has a live source to bind to.
    }
}
