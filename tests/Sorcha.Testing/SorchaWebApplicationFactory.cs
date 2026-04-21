// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Testing;

/// <summary>
/// Shared base class for Sorcha service integration test factories.
/// Wires up the common shape (Testing environment, JWT appsettings, test auth,
/// mock Redis) and exposes hooks for service-specific overrides.
/// </summary>
public abstract class SorchaWebApplicationFactory<TEntryPoint>
    : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
{
    /// <summary>
    /// Kind of Redis mock to register. Services that need round-trip storage
    /// pick <see cref="RedisMockMode.Stateful"/>; services that just need the
    /// multiplexer to exist pick <see cref="RedisMockMode.Stub"/>; services
    /// that wire their own multiplexer pick <see cref="RedisMockMode.None"/>.
    /// </summary>
    protected virtual RedisMockMode RedisMockMode => RedisMockMode.Stub;

    /// <summary>
    /// Default claims issued by <see cref="TestAuthHandler"/>. Override to
    /// supply service-specific identity (validator_id, organization_id, etc.).
    /// </summary>
    protected virtual void ConfigureTestAuth(TestAuthHandlerOptions options)
    {
    }

    /// <summary>
    /// Hook for services to add extra appsettings values. The shared JWT
    /// block is already applied before this method is called.
    /// </summary>
    protected virtual void ConfigureTestConfiguration(
        WebHostBuilderContext context,
        IConfigurationBuilder configuration)
    {
    }

    /// <summary>
    /// Hook for services to override DI registrations. Test authentication
    /// and Redis mocks are already registered before this method is called.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Installs the authentication scheme used by the factory. Override to
    /// wire in a service-specific handler (e.g. one that accepts real JWT
    /// tokens) instead of the shared <see cref="TestAuthHandler"/>.
    /// </summary>
    protected virtual void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddTestAuthentication(ConfigureTestAuth);
    }

    /// <summary>
    /// Hook called before <see cref="ConfigureWebHost"/> wires anything up.
    /// Use for `builder.UseSetting(...)` values that must be visible during
    /// the host-builder phase (i.e. before <see cref="ConfigureAppConfiguration"/>
    /// fires), and for hooks like `UseSerilog` / `UseEnvironment`.
    /// </summary>
    protected virtual void ConfigureHostBuilder(IWebHostBuilder builder)
    {
    }

    /// <summary>
    /// When true (default), the shared JWT appsettings block from
    /// <see cref="TestConfigurationDefaults.Jwt"/> is applied before
    /// <see cref="ConfigureTestConfiguration"/> runs. Override to false for
    /// projects whose Program.cs reads JwtSettings eagerly and breaks when a
    /// non-empty issuer is supplied with unsigned/test tokens.
    /// </summary>
    protected virtual bool ApplyDefaultJwtConfig => true;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        ConfigureHostBuilder(builder);

        builder.ConfigureAppConfiguration((context, config) =>
        {
            if (ApplyDefaultJwtConfig)
            {
                config.AddInMemoryCollection(TestConfigurationDefaults.Jwt);
            }
            ConfigureTestConfiguration(context, config);
        });

        builder.ConfigureServices(services =>
        {
            ConfigureAuthentication(services);

            if (RedisMockMode != RedisMockMode.None)
            {
                services.RemoveAll<IConnectionMultiplexer>();
                var multiplexer = RedisMockMode == RedisMockMode.Stateful
                    ? MockRedisBuilder.Stateful()
                    : MockRedisBuilder.Stub();
                services.AddSingleton(multiplexer);
            }

            ConfigureTestServices(services);
        });
    }

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public new virtual async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    /// <summary>
    /// Creates an HttpClient with a bearer token header set.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient that authenticates as an Administrator.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with no Authorization header.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient() => CreateClient();
}

public enum RedisMockMode
{
    None,
    Stub,
    Stateful,
}
