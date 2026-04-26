// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// OpenTelemetry metric for HAIP replay-protection-state consumption
/// outcomes. One counter, three known stores, two outcomes — set up
/// once at startup, incremented at each consume site.
/// </summary>
public sealed class HaipNonceMetrics : IDisposable
{
    /// <summary>The meter source name. Must be added via <c>metrics.AddMeter(...)</c>.</summary>
    public const string MeterName = "Sorcha.Haip.Nonces";

    private readonly Meter _meter;
    private readonly Counter<long> _consumeTotal;

    /// <summary>Creates the metric meter and registers the consume counter.</summary>
    public HaipNonceMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _consumeTotal = _meter.CreateCounter<long>(
            name: "sorcha_haip_nonce_consume_total",
            unit: "{consume}",
            description: "HAIP replay-protection-state consume attempts. Tagged by store (nonce/preauth/presentation) and outcome (success/miss).");
    }

    /// <summary>Records a successful consume on the given store.</summary>
    public void RecordSuccess(StoreKind store) =>
        _consumeTotal.Add(1, new KeyValuePair<string, object?>("store", StoreLabel(store)),
            new KeyValuePair<string, object?>("outcome", "success"));

    /// <summary>Records a miss on the given store (key absent or already consumed).</summary>
    public void RecordMiss(StoreKind store) =>
        _consumeTotal.Add(1, new KeyValuePair<string, object?>("store", StoreLabel(store)),
            new KeyValuePair<string, object?>("outcome", "miss"));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    /// <summary>Discriminator for the three replay-protection stores in HAIP.</summary>
    public enum StoreKind
    {
        Nonce,
        PreAuth,
        Presentation,
    }

    private static string StoreLabel(StoreKind kind) => kind switch
    {
        StoreKind.Nonce => "nonce",
        StoreKind.PreAuth => "preauth",
        StoreKind.Presentation => "presentation",
        _ => "unknown",
    };
}
