// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering verified transaction queue services.
/// </summary>
public static class VerifiedQueueExtensions
{
    /// <summary>
    /// Adds the verified transaction queue and related services to the service
    /// collection. Selects <see cref="RedisVerifiedTransactionQueue"/> when a
    /// Redis connection string resolves via the SorchaConnections cascade
    /// (<c>ConnectionStrings:Validator:Redis</c> →
    /// <c>ConnectionStrings:Sorcha:Redis</c>); falls back to
    /// <see cref="InMemoryVerifiedTransactionQueue"/> otherwise.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddVerifiedTransactionQueue(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<VerifiedQueueConfiguration>(
            configuration.GetSection(VerifiedQueueConfiguration.SectionName));

        var storageLog = services.GetStorageRegistrationLog();
        var interfaceName = typeof(IVerifiedTransactionQueue).FullName!;

        var hasRedisConfig =
            !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Validator:Redis"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Redis"]);

        if (hasRedisConfig)
        {
            // The connection multiplexer may already be registered by builder.AddRedisClient
            // in Program.cs; if not, wire one up here using the resolved cascade string.
            if (!services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer)))
            {
                var connectionString = configuration.GetSorchaRedisConnectionString("Validator");
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                {
                    // Harden against stale connections after long idle (issue #814): KeepAlive PINGs
                    // detect a dead socket, AbortOnConnectFail=false + ConnectRetry auto-reconnect,
                    // and explicit sync/async timeouts ensure a wedged connection makes operations
                    // THROW (callers catch and retry) rather than hang and freeze the validation loop.
                    var options = ConfigurationOptions.Parse(connectionString);
                    options.AbortOnConnectFail = false;
                    options.KeepAlive = 30;        // seconds between PINGs to detect a dead socket
                    options.ConnectRetry = 5;
                    options.ConnectTimeout = 5000;
                    options.SyncTimeout = 5000;
                    options.AsyncTimeout = 5000;
                    return ConnectionMultiplexer.Connect(options);
                });
            }
            services.AddSingleton<IVerifiedTransactionQueue, RedisVerifiedTransactionQueue>();
            storageLog.RegisterPersistent(
                interfaceName,
                typeof(RedisVerifiedTransactionQueue).FullName!,
                "redis");
        }
        else
        {
            services.AddSingleton<IVerifiedTransactionQueue, InMemoryVerifiedTransactionQueue>();
            storageLog.RegisterInMemory(
                interfaceName,
                typeof(InMemoryVerifiedTransactionQueue).FullName!,
                "no Redis connection string in ConnectionStrings:Validator:Redis or ConnectionStrings:Sorcha:Redis. Validator mempool is in-process — does not survive restart and cannot share state across replicas.");
        }

        // OTel mempool metrics — registered as singleton so the observable gauge survives.
        services.AddSingleton<ValidatorMempoolMetrics>();
        // Force eager construction so the observable instrument is registered with the Meter.
        services.AddHostedService<ValidatorMempoolMetricsActivator>();

        // Background cleanup service runs CleanupExpired periodically. Cheap for both
        // implementations; the Redis impl returns 0 since lease auto-release happens in
        // ClaimAsync, but the cleanup hook lets future implementations do passive work.
        services.AddHostedService<VerifiedQueueCleanupService>();

        return services;
    }
}

/// <summary>
/// Tiny <see cref="Microsoft.Extensions.Hosting.IHostedService"/> whose only purpose
/// is to resolve <see cref="ValidatorMempoolMetrics"/> from DI on startup, ensuring
/// its observable-gauge instrument is active before the OpenTelemetry exporter polls.
/// </summary>
internal sealed class ValidatorMempoolMetricsActivator : Microsoft.Extensions.Hosting.IHostedService
{
    public ValidatorMempoolMetricsActivator(ValidatorMempoolMetrics _) { }
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
