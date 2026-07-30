// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Test harness for inspecting endpoint metadata without issuing a request and without standing up
/// the full tenant service graph. Maps the real <c>Map*Endpoints</c> extension onto a builder whose
/// service provider answers <see cref="IServiceProviderIsService"/> permissively, so
/// RequestDelegateFactory treats every complex handler parameter as a DI service at delegate-build
/// time (services are only <em>resolved</em> when an endpoint is invoked — never here).
///
/// <para>Mirrors <c>Sorcha.Wallet.Service.Tests.Endpoints.EndpointAuthorizationMetadata</c>. Needed
/// here because a behavioural HTTP test cannot prove the ABSENCE of a gate: the review of #1346
/// showed a "deliberate non-change" test driving a SystemAdmin client passed whether or not the gate
/// was applied, because that principal is exempt from the gate either way. Metadata assertions can
/// express "this route must NOT be bound" and "this route MUST be bound" without that ambiguity.</para>
/// </summary>
internal static class EndpointAuthorizationMetadata
{
    /// <summary>Maps endpoints via <paramref name="map"/> and returns the resulting route endpoints.</summary>
    public static IReadOnlyList<RouteEndpoint> Collect(Action<IEndpointRouteBuilder> map)
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var routeBuilder = new TestEndpointRouteBuilder(new PermissiveServiceProvider(app.Services));
        map(routeBuilder);

        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    /// <summary>Minimal <see cref="IEndpointRouteBuilder"/> whose service provider we control.</summary>
    private sealed class TestEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public TestEndpointRouteBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    /// <summary>
    /// Wraps a real provider but answers <see cref="IServiceProviderIsService"/> /
    /// <see cref="IServiceProviderIsKeyedService"/> with "yes" for every type, so
    /// RequestDelegateFactory treats all complex handler parameters as services at build time.
    /// </summary>
    private sealed class PermissiveServiceProvider
        : IServiceProvider, IServiceProviderIsService, IServiceProviderIsKeyedService
    {
        private readonly IServiceProvider _inner;

        public PermissiveServiceProvider(IServiceProvider inner) => _inner = inner;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProviderIsService) ||
                serviceType == typeof(IServiceProviderIsKeyedService))
            {
                return this;
            }

            return _inner.GetService(serviceType);
        }

        public bool IsService(Type serviceType) => true;

        public bool IsKeyedService(Type serviceType, object? serviceKey) => true;
    }
}
