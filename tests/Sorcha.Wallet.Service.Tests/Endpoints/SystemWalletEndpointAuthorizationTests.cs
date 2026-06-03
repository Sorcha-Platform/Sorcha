// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Extensions;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Endpoint-metadata regression tests for the system-wallet endpoints (Feature 147 / review H1).
/// Guards against re-introducing <c>AllowAnonymous</c> and confirms each endpoint requires its
/// intended policy. Maps the real <see cref="WalletEndpoints.MapWalletEndpoints"/> and inspects
/// the resulting <see cref="RouteEndpoint"/> metadata — no request is issued.
/// <para>
/// Reading <see cref="EndpointDataSource.Endpoints"/> forces <c>RequestDelegateFactory</c> to build
/// each endpoint's delegate, which consults <see cref="IServiceProviderIsService"/> to decide whether
/// a complex handler parameter (e.g. <c>WalletManager</c>) is a DI service. We route mapping through a
/// builder whose service provider returns a permissive <see cref="IServiceProviderIsService"/> so every
/// complex parameter is treated as a service — letting all delegates build without standing up the full
/// wallet service graph. The services are only ever <em>resolved</em> when an endpoint is invoked
/// (never here), so this stays a pure metadata inspection and is decoupled from the handlers' deps.
/// </para>
/// </summary>
public class SystemWalletEndpointAuthorizationTests
{
    private static IReadOnlyList<RouteEndpoint> MapAndCollect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddWalletAuthorization();
        var app = builder.Build();

        var routeBuilder = new TestEndpointRouteBuilder(new PermissiveServiceProvider(app.Services));
        routeBuilder.MapWalletEndpoints();

        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static RouteEndpoint FindByPath(IReadOnlyList<RouteEndpoint> endpoints, string rawPath) =>
        endpoints.SingleOrDefault(e =>
            string.Equals(e.RoutePattern.RawText?.TrimStart('/'), rawPath.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase))
        ?? throw new Xunit.Sdk.XunitException($"No endpoint mapped at '{rawPath}'.");

    [Fact]
    public void SystemWalletCreate_HasNoAllowAnonymous_AndRequiresService()
    {
        var endpoints = MapAndCollect();
        var create = FindByPath(endpoints, "api/v1/wallets/system");

        create.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            "the system-wallet create endpoint must not be anonymous (review H1)");
        create.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == AuthorizationPolicies.RequireService).Should().BeTrue(
            "system-wallet create must require the RequireService policy");
    }

    [Fact]
    public void SystemWalletRecover_HasNoAllowAnonymous_AndRequiresRecoveryPolicy()
    {
        var endpoints = MapAndCollect();
        var recover = FindByPath(endpoints, "api/v1/wallets/system/recover");

        recover.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            "the system-wallet recover endpoint must not be anonymous (review H1)");
        recover.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == "CanRecoverSystemWallet").Should().BeTrue(
            "system-wallet recover must require the CanRecoverSystemWallet policy");
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
    /// <see cref="IServiceProviderIsKeyedService"/> queries with "yes" for every type, so
    /// RequestDelegateFactory treats all complex handler parameters as services at delegate-build time.
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
