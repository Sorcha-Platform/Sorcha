// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Wallet.Service.Extensions;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Test harness for inspecting endpoint authorization metadata without issuing a request and without
/// standing up the full wallet service graph. Maps the real <c>Map*Endpoints</c> extension onto a
/// builder whose service provider returns a permissive <see cref="IServiceProviderIsService"/>, so
/// RequestDelegateFactory treats every complex handler parameter as a DI service at delegate-build time
/// (the services are only <em>resolved</em> when an endpoint is invoked — never here).
/// </summary>
internal static class EndpointAuthorizationMetadata
{
    /// <summary>Maps endpoints via <paramref name="map"/> and returns the resulting route endpoints.</summary>
    public static IReadOnlyList<RouteEndpoint> Collect(Action<IEndpointRouteBuilder> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWalletAuthorization();
        var app = builder.Build();

        var routeBuilder = new TestEndpointRouteBuilder(new PermissiveServiceProvider(app.Services));
        map(routeBuilder);

        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    /// <summary>Finds the single route endpoint at <paramref name="rawPath"/> (leading slash optional).</summary>
    public static RouteEndpoint FindByPath(IReadOnlyList<RouteEndpoint> endpoints, string rawPath) =>
        endpoints.SingleOrDefault(e =>
            string.Equals(e.RoutePattern.RawText?.TrimStart('/'), rawPath.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase))
        ?? throw new Xunit.Sdk.XunitException($"No endpoint mapped at '{rawPath}'.");

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
    /// <see cref="IServiceProviderIsKeyedService"/> with "yes" for every type, so RequestDelegateFactory
    /// treats all complex handler parameters as services at delegate-build time.
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
