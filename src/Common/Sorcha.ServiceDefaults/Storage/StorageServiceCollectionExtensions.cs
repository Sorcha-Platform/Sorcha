// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// DI wiring for the storage registration log, health check, metrics, and
/// fail-fast hosted service.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IStorageRegistrationLog"/>,
    /// <see cref="StorageRegistrationMetrics"/>, the
    /// <c>storage-providers</c> health check, and the
    /// <see cref="StorageEnforcementHostedService"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent — safe to call multiple times. The first call wires the
    /// log, metrics, health check, and enforcement hosted service; subsequent
    /// calls are no-ops. Services should resolve
    /// <see cref="IStorageRegistrationLog"/> via DI in their storage-wiring
    /// helpers and call <c>RegisterPersistent</c> / <c>RegisterInMemory</c>
    /// at the matching <c>AddScoped</c> / <c>AddSingleton</c> sites.
    /// </remarks>
    public static IServiceCollection AddStorageRegistration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Sentinel: presence of IStorageRegistrationLog means this method has already run.
        // Required because AddHealthChecks().AddCheck<T>(name, ...) appends unconditionally —
        // duplicate calls would register two checks with the same name.
        if (services.Any(d => d.ServiceType == typeof(IStorageRegistrationLog)))
        {
            return services;
        }

        services.AddSingleton<IStorageRegistrationLog, StorageRegistrationLog>();
        services.AddSingleton<StorageRegistrationMetrics>();

        // Force eager construction of the metrics class so its observable instruments are registered
        // with the Meter even if no caller resolves it directly. AddHostedService<T> is itself
        // dedup'd by TryAddEnumerable (service+impl type), but we sentinel-guard the whole method
        // for the AddCheck case anyway.
        services.AddHostedService<StorageMetricsActivator>();

        services.AddHealthChecks()
            .AddCheck<StorageProvidersHealthCheck>(
                StorageProvidersHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "storage"]);

        services.AddHostedService<StorageEnforcementHostedService>();

        return services;
    }

    /// <summary>
    /// Tiny <see cref="Microsoft.Extensions.Hosting.IHostedService"/> whose
    /// only purpose is to resolve <see cref="StorageRegistrationMetrics"/>
    /// from DI on startup, ensuring its observable-gauge instruments are
    /// active before the OpenTelemetry exporter polls them.
    /// </summary>
    internal sealed class StorageMetricsActivator : Microsoft.Extensions.Hosting.IHostedService
    {
        public StorageMetricsActivator(StorageRegistrationMetrics _) { }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
