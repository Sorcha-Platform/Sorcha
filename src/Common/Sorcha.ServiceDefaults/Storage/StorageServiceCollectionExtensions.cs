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
    /// Idempotent — safe to call multiple times. Services should resolve
    /// <see cref="IStorageRegistrationLog"/> via DI in their storage-wiring
    /// helpers and call <c>RegisterPersistent</c> / <c>RegisterInMemory</c>
    /// at the matching <c>AddScoped</c> / <c>AddSingleton</c> sites.
    /// </remarks>
    public static IServiceCollection AddStorageRegistration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IStorageRegistrationLog, StorageRegistrationLog>();
        services.TryAddSingleton<StorageRegistrationMetrics>();

        // Force eager construction of the metrics class so its observable instruments are registered
        // with the Meter even if no caller resolves it directly.
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
