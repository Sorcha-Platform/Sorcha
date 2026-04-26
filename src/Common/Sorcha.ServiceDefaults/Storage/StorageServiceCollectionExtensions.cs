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
    /// <para>
    /// Idempotent — safe to call multiple times. The first call wires the
    /// log, metrics, health check, and enforcement hosted service; subsequent
    /// calls are no-ops.
    /// </para>
    /// <para>
    /// Services call <see cref="GetStorageRegistrationLog"/> from inside
    /// their storage-wiring extension methods (e.g.,
    /// <c>AddWalletDatabase</c>) to obtain the log instance and call
    /// <c>RegisterPersistent</c> / <c>RegisterInMemory</c> at the matching
    /// <c>AddScoped</c> / <c>AddSingleton</c> site. The log is registered as
    /// an instance (eagerly constructed) so it is resolvable before the DI
    /// container is built — necessary because storage wiring runs at
    /// <see cref="IServiceCollection"/>-extension time.
    /// </para>
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

        // Eager-construct so service-side AddXxxDatabase extensions can call
        // GetStorageRegistrationLog at IServiceCollection-extension time.
        services.AddSingleton<IStorageRegistrationLog>(new StorageRegistrationLog());
        services.AddSingleton<StorageRegistrationMetrics>();

        // Force eager construction of the metrics class so its observable instruments are registered
        // with the Meter even if no caller resolves it directly. AddHostedService<T> is itself
        // dedup'd by TryAddEnumerable (service+impl type), but we sentinel-guard the whole method
        // for the AddCheck case anyway.
        services.AddHostedService<StorageMetricsActivator>();

        services.AddHealthChecks()
            .AddCheck<StorageProvidersHealthCheck>(
                StorageProvidersHealthCheck.Name,
                // failureStatus is the result if CheckHealthAsync throws an unhandled exception.
                // The check itself returns Healthy or Degraded — never Unhealthy.
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "storage"]);

        services.AddHostedService<StorageEnforcementHostedService>();

        return services;
    }

    /// <summary>
    /// Returns the <see cref="IStorageRegistrationLog"/> instance registered
    /// by <see cref="AddStorageRegistration"/>. Service-side storage wiring
    /// (e.g., <c>AddWalletDatabase</c>) calls this from inside an
    /// <see cref="IServiceCollection"/> extension method to register
    /// persistent or in-memory entries before the DI container is built.
    /// </summary>
    /// <remarks>
    /// Defensive: if <see cref="AddStorageRegistration"/> has not yet been
    /// called on the service collection, this method calls it first. That
    /// keeps service-extension methods independently unit-testable —
    /// callers do not have to remember the prerequisite. In production,
    /// <c>builder.AddServiceDefaults()</c> always wires the log first, so
    /// the defensive call is a no-op via the idempotency sentinel.
    /// </remarks>
    public static IStorageRegistrationLog GetStorageRegistrationLog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddStorageRegistration();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IStorageRegistrationLog))
            ?? throw new InvalidOperationException(
                $"{nameof(AddStorageRegistration)} was called but no {nameof(IStorageRegistrationLog)} " +
                $"descriptor was found in the service collection. This is a bug — please report.");

        if (descriptor.ImplementationInstance is IStorageRegistrationLog instance)
        {
            return instance;
        }

        throw new InvalidOperationException(
            $"{nameof(IStorageRegistrationLog)} is registered without an instance. " +
            $"This indicates {nameof(AddStorageRegistration)} was bypassed by a custom " +
            "factory or type-based registration that does not match the eager-construction pattern.");
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
