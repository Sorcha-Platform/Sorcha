// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry instrumentation for the citizen presentation store (Feature 114,
/// US5 PR3). Records each store operation on the existing
/// <c>Sorcha.Wallet.Service</c> meter as
/// <c>sorcha_citizen_presentation_store_total{op=upsert|list|delete}</c>.
/// </summary>
internal static class CitizenPresentationStoreMetrics
{
    private static readonly Meter Meter = new("Sorcha.Wallet.Service");

    private static readonly Counter<long> StoreCounter =
        Meter.CreateCounter<long>("sorcha_citizen_presentation_store_total");

    /// <summary>Record one store operation (<c>upsert</c>, <c>list</c>, or <c>delete</c>).</summary>
    public static void RecordOp(string op) =>
        StoreCounter.Add(1, new KeyValuePair<string, object?>("op", op));
}
