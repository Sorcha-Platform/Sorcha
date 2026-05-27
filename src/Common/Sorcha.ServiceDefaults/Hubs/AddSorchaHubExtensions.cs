// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// Single-call DI registration for every Sorcha notification hub. Wires
/// JWT Bearer auth (via the existing service-defaults auth pipeline),
/// Redis backplane (per-service ChannelPrefix), reconnect-with-jitter,
/// OpenTelemetry instrumentation, and the storage registration log entry —
/// all behind one extension call.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-005 — FR-012. Used by every notification hub.
/// ChatHub is the deliberate exception (RPC-streamy; spec FR-019) and uses
/// the standard <c>services.AddSignalR()</c> path directly, alongside an
/// XML doc on the class marking it as the exception.
/// </remarks>
public static class AddSorchaHubExtensions
{
    /// <summary>
    /// Registers the supplied <typeparamref name="THub" /> + typed client
    /// with shared Sorcha hub conventions. Idempotent across multiple hubs
    /// in the same service.
    /// </summary>
    /// <typeparam name="THub">The strongly-typed hub class.</typeparam>
    /// <typeparam name="TClient">The typed client interface.</typeparam>
    /// <param name="services">DI surface.</param>
    /// <param name="configuration">App configuration. Used to resolve the Redis backplane connection string via the SorchaConnections cascade.</param>
    /// <param name="routePath">Route the hub will be mapped at by <see cref="MapSorchaHubs" />. e.g. <c>"/hubs/tenant"</c>.</param>
    /// <param name="serviceShortName">Short, lowercase service identifier used as the Redis backplane <c>ChannelPrefix</c>. e.g. <c>"tenant"</c>.</param>
    /// <returns>The DI surface for chaining.</returns>
    public static IServiceCollection AddSorchaHub<THub, TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        string routePath,
        string serviceShortName)
        where THub : Hub<TClient>
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceShortName);

        // Wires the cross-cutting infrastructure once per service; subsequent calls are no-ops.
        SorchaHubConventions.ConfigureSignalRWithBackplane(services, configuration, serviceShortName);

        // Record the route so MapSorchaHubs can wire it at endpoint mapping time.
        var registry = GetOrAddHubRouteRegistry(services);
        registry.Add(typeof(THub), routePath);

        return services;
    }

    /// <summary>
    /// Maps every hub registered via <see cref="AddSorchaHub{THub, TClient}" />
    /// at the route path provided at registration time.
    /// </summary>
    /// <remarks>
    /// Call once from <c>Program.cs</c> after <c>app.UseAuthentication()</c> /
    /// <c>app.UseAuthorization()</c>. Equivalent to writing an explicit
    /// <c>app.MapHub&lt;THub&gt;(route)</c> per hub.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder (typically <c>app</c>).</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSorchaHubs(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetService<HubRouteRegistry>();
        if (registry is null)
        {
            return endpoints;
        }

        // We use reflection here because MapHub<T> is a generic method and
        // the hub type is not known until runtime. The cost is one-time at
        // startup; the runtime impact of the resulting endpoint binding is
        // identical to a direct app.MapHub<T>("path") call.
        var mapHubMethod = typeof(HubEndpointRouteBuilderExtensions)
            .GetMethod(nameof(HubEndpointRouteBuilderExtensions.MapHub), [typeof(IEndpointRouteBuilder), typeof(string)])
            ?? throw new InvalidOperationException(
                "Could not locate HubEndpointRouteBuilderExtensions.MapHub(IEndpointRouteBuilder, string) — this is an ASP.NET Core ABI change.");

        foreach (var entry in registry.Snapshot())
        {
            var generic = mapHubMethod.MakeGenericMethod(entry.HubType);
            generic.Invoke(null, [endpoints, entry.RoutePath]);
        }

        return endpoints;
    }

    private static HubRouteRegistry GetOrAddHubRouteRegistry(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(HubRouteRegistry));
        if (existing?.ImplementationInstance is HubRouteRegistry instance)
        {
            return instance;
        }

        var registry = new HubRouteRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
