// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Emits OpenTelemetry observable gauges describing the storage-provider
/// state of the running service. Two instruments on the
/// <c>Sorcha.Storage</c> meter:
/// <list type="bullet">
///   <item><c>sorcha_storage_provider_info</c> — one observation per registered audited interface (value <c>1</c>, tags identify the implementation and backend).</item>
///   <item><c>sorcha_storage_fallback_active</c> — one observation per registered audited interface (value <c>1</c> when in-memory, <c>0</c> when persistent).</item>
/// </list>
/// </summary>
/// <remarks>
/// Registered as a singleton via <c>AddStorageRegistration</c>. The
/// observable-gauge callbacks read from the registration log snapshot, so
/// observations always reflect the current state — though in practice the
/// snapshot is fixed after service startup.
/// </remarks>
public sealed class StorageRegistrationMetrics : IDisposable
{
    /// <summary>The meter source name. Must be added via <c>metrics.AddMeter(...)</c> in OpenTelemetry registration.</summary>
    public const string MeterName = "Sorcha.Storage";

    private readonly Meter _meter;
    private readonly IStorageRegistrationLog _log;
    private readonly string _serviceName;

    /// <summary>Creates and registers the observable instruments.</summary>
    public StorageRegistrationMetrics(
        IMeterFactory meterFactory,
        IStorageRegistrationLog log,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(environment);

        _meter = meterFactory.Create(MeterName);
        _log = log;
        _serviceName = environment.ApplicationName;

        _meter.CreateObservableGauge(
            name: "sorcha_storage_provider_info",
            observeValues: ObserveProviderInfo,
            unit: "{registration}",
            description: "Storage interface provider registration. One observation per audited interface; value is always 1.");

        _meter.CreateObservableGauge(
            name: "sorcha_storage_fallback_active",
            observeValues: ObserveFallbackActive,
            unit: "{registration}",
            description: "Set to 1 when an audited storage interface is on an in-memory backend; 0 when persistent.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveProviderInfo()
    {
        foreach (var record in _log.Snapshot())
        {
            if (!record.IsAudited)
            {
                continue;
            }

            yield return new Measurement<long>(
                value: 1,
                tags: new KeyValuePair<string, object?>[]
                {
                    new("service", _serviceName),
                    new("interface", record.InterfaceName),
                    new("implementation", record.ImplementationName),
                    new("backend", record.Backend),
                });
        }
    }

    private IEnumerable<Measurement<long>> ObserveFallbackActive()
    {
        foreach (var record in _log.Snapshot())
        {
            if (!record.IsAudited)
            {
                continue;
            }

            yield return new Measurement<long>(
                value: record.IsInMemory ? 1L : 0L,
                tags: new KeyValuePair<string, object?>[]
                {
                    new("service", _serviceName),
                    new("interface", record.InterfaceName),
                });
        }
    }
}
