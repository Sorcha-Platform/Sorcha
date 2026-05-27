// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceDefaults.Storage;
using StackExchange.Redis;

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// Cross-cutting wiring for every Sorcha notification hub. Centralised so
/// every service ends up with identical reconnect, backplane isolation,
/// auth-claim guard, and observability behaviour.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-005 — FR-012. Called by
/// <see cref="AddSorchaHubExtensions.AddSorchaHub{THub, TClient}" /> once per
/// service; subsequent calls are no-ops via a sentinel registration.
/// </remarks>
public static class SorchaHubConventions
{
    /// <summary>
    /// Synthetic interface name recorded in the storage registration log
    /// for the SignalR backplane. Listed in
    /// <see cref="AuditedStorageInterfaces.Names" /> so Production / Staging
    /// fail-fast at startup when a hub-hosting service has no Redis
    /// backplane configured.
    /// </summary>
    public const string BackplaneRegistrationInterface = "Sorcha.ServiceDefaults.Hubs.SignalRBackplane";

    /// <summary>
    /// Wires SignalR + Redis backplane + metrics + the storage registration
    /// log entry once per service. Idempotent.
    /// </summary>
    /// <param name="services">DI surface.</param>
    /// <param name="configuration">App configuration. Used to resolve the Redis connection string via the SorchaConnections cascade.</param>
    /// <param name="serviceShortName">Short, lowercase service identifier used as the Redis backplane <c>ChannelPrefix</c>. e.g., <c>tenant</c>, <c>blueprint</c>, <c>wallet</c>, <c>register</c>.</param>
    /// <returns>The chained <see cref="ISignalRServerBuilder" /> when SignalR is configured the first time. <c>null</c> on subsequent calls.</returns>
    internal static ISignalRServerBuilder? ConfigureSignalRWithBackplane(
        IServiceCollection services,
        IConfiguration configuration,
        string serviceShortName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceShortName);

        // Sentinel: presence of SignalRMetrics means we have already wired the
        // shared infrastructure; subsequent AddSorchaHub calls only register
        // the route entry for the new hub but do not re-wire SignalR.
        if (services.Any(d => d.ServiceType == typeof(SignalRMetrics)))
        {
            return null;
        }

        services.AddStorageRegistration();
        services.AddSingleton<SignalRMetrics>();
        services.AddHostedService<SignalRMetricsActivator>();
        // HubRouteRegistry is created and registered lazily by AddSorchaHub
        // itself — see GetOrAddHubRouteRegistry. Registering both a type-based
        // and an instance-based singleton produces two descriptors; the second
        // AddSorchaHub call would then build a fresh registry and the first
        // call's hub entry would silently disappear.

        var redisConnectionString = TryGetSorchaRedisConnectionString(configuration, serviceShortName);
        var signalRBuilder = services.AddSignalR();

        var log = GetOrAddRegistrationLog(services);

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var channelPrefix = $"sorcha:signalr:{serviceShortName}";
            signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix = new RedisChannel(channelPrefix, RedisChannel.PatternMode.Literal);
            });
            log.RegisterPersistent(
                BackplaneRegistrationInterface,
                "Microsoft.AspNetCore.SignalR.StackExchangeRedis",
                $"redis (prefix={channelPrefix})");
        }
        else
        {
            log.RegisterInMemory(
                BackplaneRegistrationInterface,
                "Microsoft.AspNetCore.SignalR.LocalReplicaOnly",
                $"no Redis connection string in ConnectionStrings:{serviceShortName.ToLowerInvariant()}:Redis or ConnectionStrings:Sorcha:Redis — multi-replica fan-out will silently miss audiences on other replicas");
        }

        return signalRBuilder;
    }

    private static IStorageRegistrationLog GetOrAddRegistrationLog(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IStorageRegistrationLog));
        if (existing?.ImplementationInstance is IStorageRegistrationLog instance)
        {
            return instance;
        }

        // AddStorageRegistration above is responsible for the singleton instance
        // registration; this codepath is only reached if a future caller swaps
        // it out with a custom implementation that wasn't pre-instantiated.
        // Materialise a fallback so the backplane registration is recorded
        // even in that case — the test suite covers the standard flow.
        var fallback = new StorageRegistrationLog();
        services.RemoveAll<IStorageRegistrationLog>();
        services.AddSingleton<IStorageRegistrationLog>(fallback);
        return fallback;
    }

    private static string? TryGetSorchaRedisConnectionString(IConfiguration configuration, string serviceShortName)
    {
        // Service-specific override wins, falls back to the Sorcha cascade.
        var serviceKey = $"ConnectionStrings:{serviceShortName}:Redis";
        var sorchaKey = "ConnectionStrings:Sorcha:Redis";

        return configuration[serviceKey]
            ?? configuration[sorchaKey]
            // .NET's standard GetConnectionString convention as a final fallback.
            ?? configuration.GetConnectionString("Redis");
    }

    /// <summary>
    /// Hosted service whose only job is to force-resolve <see cref="SignalRMetrics" />
    /// at startup so the meter and instruments register with the OpenTelemetry
    /// collector even when no caller injects the metrics class directly.
    /// </summary>
    internal sealed class SignalRMetricsActivator : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly SignalRMetrics _metrics;
        private readonly ILogger<SignalRMetricsActivator> _logger;

        public SignalRMetricsActivator(SignalRMetrics metrics, ILogger<SignalRMetricsActivator> logger)
        {
            _metrics = metrics;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Sorcha.SignalR meter activated ({Type})", _metrics.GetType().FullName);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// In-memory registry of the (hub-type, route-path) pairs registered via
/// <see cref="AddSorchaHubExtensions.AddSorchaHub{THub, TClient}" />. Read by
/// <see cref="AddSorchaHubExtensions.MapSorchaHubs" /> at endpoint-mapping time.
/// </summary>
public sealed class HubRouteRegistry
{
    private readonly List<HubRouteEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>Records a hub-type → route-path pairing. Idempotent on hub type.</summary>
    public void Add(Type hubType, string routePath)
    {
        ArgumentNullException.ThrowIfNull(hubType);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePath);
        lock (_lock)
        {
            if (_entries.Any(e => e.HubType == hubType))
            {
                return;
            }
            _entries.Add(new HubRouteEntry(hubType, routePath));
        }
    }

    /// <summary>Returns an immutable snapshot of all recorded routes.</summary>
    public IReadOnlyList<HubRouteEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}

/// <summary>One (hub-type, route-path) pairing recorded in <see cref="HubRouteRegistry" />.</summary>
public sealed record HubRouteEntry(Type HubType, string RoutePath);
