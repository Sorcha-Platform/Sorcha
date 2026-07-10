// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 181 (FR-028) — counts rejections of the retired Presentation Exchange dialect so
/// operators can see external integrators still speaking the pre-DCQL shape after the clean break.
/// </summary>
public static class HaipDialectMetrics
{
    /// <summary>Meter name — add to the ServiceDefaults OTel allowlist if not already exported.</summary>
    public const string MeterName = "Sorcha.Haip";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> DialectRejections = Meter.CreateCounter<long>(
        "sorcha_dialect_rejection_total",
        description: "Requests/responses rejected for using the retired Presentation Exchange dialect (LEGACY_DIALECT).");

    /// <summary>Record one rejection, tagged by the surface that refused it.</summary>
    public static void RecordRejection(string surface)
        => DialectRejections.Add(1, new KeyValuePair<string, object?>("surface", surface));
}
