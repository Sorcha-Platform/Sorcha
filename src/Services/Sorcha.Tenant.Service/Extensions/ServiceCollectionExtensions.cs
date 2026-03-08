// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Services;
using StackExchange.Redis;

namespace Sorcha.Tenant.Service.Extensions;

/// <summary>
/// Extension methods for WebApplication to add database initialization.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Adds automatic database migration and seeding on startup.
    /// Creates default organization (sorcha.local) and admin user if not exists.
    /// </summary>
    public static IServiceCollection AddDatabaseInitializer(this IServiceCollection services)
    {
        services.AddSingleton<DatabaseInitializer>();
        services.AddHostedService<DatabaseInitializerHostedService>();
        return services;
    }
}

/// <summary>
/// Extension methods for registering Tenant Service dependencies.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all Tenant Service dependencies to the service collection.
    /// </summary>
    public static IServiceCollection AddTenantServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add HTTP context accessor for tenant resolution
        services.AddHttpContextAccessor();

        // Add tenant provider
        services.AddScoped<ITenantProvider, TenantProvider>();

        // Add database context
        services.AddTenantDatabase(configuration);

        // Add repositories
        services.AddTenantRepositories();

        // Add Redis and token revocation
        services.AddTenantRedis(configuration);

        // Add email sender
        services.AddTenantEmail(configuration);

        // Add in-memory cache (used by OIDC discovery, password breach check, etc.)
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL database context with multi-tenant support.
    /// </summary>
    public static IServiceCollection AddTenantDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TenantDatabase");

        services.AddDbContext<TenantDbContext>(options =>
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    // Aggressive retry policy for startup resilience
                    // Max retry time: ~5 minutes (10 retries with exponential backoff up to 30s)
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 10,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                });
            }
            else
            {
                // Use in-memory database for testing
                options.UseInMemoryDatabase("TenantServiceTestDb");
            }
        });

        return services;
    }

    /// <summary>
    /// Adds repository implementations.
    /// </summary>
    public static IServiceCollection AddTenantRepositories(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IParticipantRepository, ParticipantRepository>();

        // Add application services
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IParticipantService, ParticipantService>();
        services.AddScoped<IWalletVerificationService, WalletVerificationService>();
        services.AddScoped<IParticipantPublishingService, ParticipantPublishingService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IServiceAuthService, ServiceAuthService>();
        services.AddScoped<ITotpService, TotpService>();

        // IDP configuration services
        services.AddHttpClient<IOidcDiscoveryService, OidcDiscoveryService>();
        services.AddScoped<IIdpConfigurationService, IdpConfigurationService>();
        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// Adds Redis connection with circuit breaker for token revocation.
    /// </summary>
    public static IServiceCollection AddTenantRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetValue<string>("Redis:ConnectionString");

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            // Configure circuit breaker for Redis
            var circuitBreakerPolicy = Policy
                .Handle<RedisConnectionException>()
                .Or<RedisTimeoutException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(30));

            services.AddSingleton<IAsyncPolicy>(circuitBreakerPolicy);

            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var options = ConfigurationOptions.Parse(redisConnectionString);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 3;
                options.ConnectTimeout = 5000;
                options.SyncTimeout = 5000;

                return ConnectionMultiplexer.Connect(options);
            });
        }
        else
        {
            // Register a null implementation for testing without Redis
            services.AddSingleton<IConnectionMultiplexer>(sp =>
                throw new InvalidOperationException("Redis is not configured. Set Redis:ConnectionString in configuration."));
        }

        // Configure token revocation
        services.Configure<TokenRevocationConfiguration>(
            configuration.GetSection("TokenRevocation"));

        services.AddScoped<ITokenRevocationService, TokenRevocationService>();

        return services;
    }

    /// <summary>
    /// Adds email sending services with SMTP configuration.
    /// </summary>
    public static IServiceCollection AddTenantEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailSettings>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }

    /// <summary>
    /// Adds health checks for Tenant Service dependencies.
    /// </summary>
    public static IServiceCollection AddTenantHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();

        var connectionString = configuration.GetConnectionString("TenantDatabase");
        if (!string.IsNullOrEmpty(connectionString))
        {
            healthChecks.AddNpgSql(connectionString, name: "postgresql");
        }

        var redisConnectionString = configuration.GetValue<string>("Redis:ConnectionString");
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            healthChecks.AddRedis(redisConnectionString, name: "redis");
        }

        return services;
    }
}
