// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// OpenTelemetry meter and instruments for the Sorcha SignalR notification
/// surface. Wired identically into every notification hub via
/// <see cref="AddSorchaHubExtensions.AddSorchaHub{THub, TClient}" />.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-039 — exposes connection lifecycle, message
/// fan-out, backplane state, and reconnect rate per hub.
/// </remarks>
public sealed class SignalRMetrics : IDisposable
{
    /// <summary>The meter name. Stable across versions.</summary>
    public const string MeterName = "Sorcha.SignalR";

    private readonly Meter _meter;
    private readonly Dictionary<string, int> _backplaneStates = new(StringComparer.Ordinal);
    private readonly object _backplaneLock = new();

    /// <summary>Counter: connection lifecycle. Tags: <c>hub</c>, <c>state</c>.</summary>
    public Counter<long> ConnectionsTotal { get; }

    /// <summary>Counter: outbound event count. Tags: <c>hub</c>, <c>event_type</c>.</summary>
    public Counter<long> MessagesSentTotal { get; }

    /// <summary>Counter: client reconnect attempts. Tags: <c>hub</c>, <c>reason</c>.</summary>
    public Counter<long> ReconnectsTotal { get; }

    /// <summary>Observable gauge: per-service backplane health. Tag: <c>service</c>. Values 0=down, 1=degraded, 2=up.</summary>
    public ObservableGauge<int> BackplaneState { get; }

    /// <summary>
    /// Constructs the meter and registers all instruments. Singleton — register
    /// once per service.
    /// </summary>
    public SignalRMetrics()
    {
        _meter = new Meter(MeterName);

        ConnectionsTotal = _meter.CreateCounter<long>(
            name: "sorcha_signalr_connections_total",
            unit: "{connection}",
            description: "SignalR hub connection lifecycle events.");

        MessagesSentTotal = _meter.CreateCounter<long>(
            name: "sorcha_signalr_messages_sent_total",
            unit: "{message}",
            description: "Outbound SignalR hub events broadcast to clients.");

        ReconnectsTotal = _meter.CreateCounter<long>(
            name: "sorcha_signalr_reconnects_total",
            unit: "{attempt}",
            description: "SignalR client reconnect attempts.");

        BackplaneState = _meter.CreateObservableGauge<int>(
            name: "sorcha_signalr_backplane_state",
            observeValues: ObserveBackplaneStates,
            unit: "{state}",
            description: "Per-service SignalR backplane health: 0=down, 1=degraded, 2=up.");
    }

    /// <summary>
    /// Updates the backplane-state gauge for a given service. Call from the
    /// service's backplane health probe.
    /// </summary>
    /// <param name="serviceShortName">e.g. <c>tenant</c>, <c>blueprint</c>, <c>wallet</c>, <c>register</c>.</param>
    /// <param name="state"><c>BackplaneStateValues.Down</c>, <c>Degraded</c>, or <c>Up</c>.</param>
    public void RecordBackplaneState(string serviceShortName, int state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceShortName);
        lock (_backplaneLock)
        {
            _backplaneStates[serviceShortName] = state;
        }
    }

    private IEnumerable<Measurement<int>> ObserveBackplaneStates()
    {
        lock (_backplaneLock)
        {
            // Materialise inside the lock so the enumerator returned to the OTel
            // collector cannot race with concurrent RecordBackplaneState calls.
            return _backplaneStates
                .Select(kvp => new Measurement<int>(kvp.Value, new KeyValuePair<string, object?>("service", kvp.Key)))
                .ToArray();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// Backplane-state numeric values for <see cref="SignalRMetrics.RecordBackplaneState" />.
/// </summary>
public static class BackplaneStateValues
{
    /// <summary>Backplane unreachable.</summary>
    public const int Down = 0;

    /// <summary>Backplane reachable but reporting errors or partial connectivity.</summary>
    public const int Degraded = 1;

    /// <summary>Backplane fully operational.</summary>
    public const int Up = 2;
}
