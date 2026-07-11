// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US3 (T039 / FR-028, data-model §7) — trusted-list snapshot observability on the shared
/// <c>Sorcha.Trust</c> meter. The <c>sorcha_trustlist_snapshot_info</c> observable gauge reports one
/// series (value 1) per currently-Active snapshot, tagged with its list identity + sequence, so
/// operators can see which trusted lists (and versions) are loaded. Import records the active
/// snapshot; delete removes it.
/// </summary>
public static class TrustListMetrics
{
    /// <summary>The shared trust meter name (same stream as the engine's <c>TrustMetrics</c>).</summary>
    public const string MeterName = "Sorcha.Trust";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // trustListId → sequence number of the current Active snapshot.
    private static readonly ConcurrentDictionary<string, long> ActiveSnapshots = new(StringComparer.Ordinal);

    static TrustListMetrics()
    {
        Meter.CreateObservableGauge(
            "sorcha_trustlist_snapshot_info",
            ObserveActiveSnapshots,
            unit: "snapshots",
            description: "One series (value 1) per Active trusted-list snapshot, tagged trust_list_id + sequence.");
    }

    /// <summary>Record (or update) the Active snapshot for a list identity after a successful import.</summary>
    public static void RecordSnapshotActive(string trustListId, long sequenceNumber)
        => ActiveSnapshots[trustListId] = sequenceNumber;

    /// <summary>Remove a list identity from the active set after deletion.</summary>
    public static void RecordSnapshotRemoved(string trustListId)
        => ActiveSnapshots.TryRemove(trustListId, out _);

    private static IEnumerable<Measurement<long>> ObserveActiveSnapshots()
    {
        foreach (var (trustListId, sequence) in ActiveSnapshots)
        {
            yield return new Measurement<long>(1,
                new KeyValuePair<string, object?>("trust_list_id", trustListId),
                new KeyValuePair<string, object?>("sequence", sequence));
        }
    }
}
