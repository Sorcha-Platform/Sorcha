// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Polly;
using Serilog;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.IntegrationTests.Configuration;
using Sorcha.Testing;
using Testcontainers.PostgreSql;

namespace Sorcha.Tenant.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Tenant Service integration tests.
/// Supports both InMemory database (fast, default) and PostgreSQL via Testcontainers (realistic).
/// Set environment variable TEST_DATABASE_MODE=PostgreSQL to use Testcontainers.
/// </summary>
public class TenantServiceWebApplicationFactory : SorchaWebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private PostgreSqlContainer? _postgresContainer;
    private string? _connectionString;
    private bool _seeded;
    private bool _containerInitialized;
    private readonly object _containerLock = new();

    public TenantServiceWebApplicationFactory()
    {
        // Unique database name per factory instance for isolation
        _databaseName = $"TenantServiceIntegrationTests_{Guid.NewGuid()}";
    }

    protected override RedisMockMode RedisMockMode => RedisMockMode.Stub;

    /// <summary>
    /// Ensures the PostgreSQL container is started before configuration.
    /// Called synchronously from ConfigureHostBuilder.
    /// </summary>
    private void EnsureContainerStarted()
    {
        if (!TestConfiguration.UsePostgreSQL || _containerInitialized)
            return;

        lock (_containerLock)
        {
            if (_containerInitialized)
                return;

            _postgresContainer = new PostgreSqlBuilder()
                .WithDatabase(_databaseName)
                .WithUsername("sorcha_test")
                .WithPassword("sorcha_test_pass")
                .WithCleanUp(true)
                .Build();

            _postgresContainer.StartAsync().GetAwaiter().GetResult();
            _connectionString = _postgresContainer.GetConnectionString();

            Console.WriteLine($"[TEST] PostgreSQL container started");
            Console.WriteLine($"[TEST] Connection string: {_connectionString}");
            Console.WriteLine($"[TEST] Host: {_postgresContainer.Hostname}");
            Console.WriteLine($"[TEST] Port: {_postgresContainer.GetMappedPublicPort(5432)}");

            _containerInitialized = true;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Disable Serilog to avoid the "logger is already frozen" issue
        builder.UseSerilog((_, _) => { }, preserveStaticLogger: true);
        return base.CreateHost(builder);
    }

    protected override void ConfigureHostBuilder(IWebHostBuilder builder)
    {
        // Ensure PostgreSQL container is started before any configuration reads
        // its connection string.
        EnsureContainerStarted();
    }

    protected override void ConfigureTestConfiguration(
        WebHostBuilderContext context,
        IConfigurationBuilder configuration)
    {
        var testConfig = new Dictionary<string, string?>
        {
            // Clear Redis connection string to prevent health check failure (we use a mock)
            ["Redis:ConnectionString"] = "",
        };

        if (TestConfiguration.UsePostgreSQL)
        {
            testConfig["ConnectionStrings:TenantDatabase"] = _connectionString;
        }
        else
        {
            // Empty string (not null) forces InMemory database usage
            testConfig["ConnectionStrings:TenantDatabase"] = "";
        }

        configuration.AddInMemoryCollection(testConfig);
    }

    protected override void ConfigureAuthentication(IServiceCollection services)
    {
        // Tenant keeps its own handler because it round-trips real JWTs
        // and defaults to seeded admin/member user IDs from TestDataSeeder.
        services.RemoveAll<IAuthenticationService>();
        services.RemoveAll<IAuthenticationHandlerProvider>();
        services.RemoveAll<IAuthenticationSchemeProvider>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Remove existing DbContext registrations (both InMemory and PostgreSQL modes).
        var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TenantDbContext>));
        if (dbContextDescriptor != null)
            services.Remove(dbContextDescriptor);

        var genericDbContextOptionsDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions));
        if (genericDbContextOptionsDescriptor != null)
            services.Remove(genericDbContextOptionsDescriptor);

        services.RemoveAll<TenantDbContext>();

        var efServiceTypes = services.Where(d =>
            d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
            d.ImplementationType?.FullName?.StartsWith("Npgsql") == true).ToList();
        foreach (var efService in efServiceTypes)
            services.Remove(efService);

        if (TestConfiguration.UseInMemory)
        {
            services.AddDbContext<TenantDbContext>((_, options) =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            });

            Console.WriteLine($"[TEST] Configured InMemory DbContext with database name: {_databaseName}");
        }
        else if (TestConfiguration.UsePostgreSQL)
        {
            services.AddDbContext<TenantDbContext>((_, options) =>
            {
                options.UseNpgsql(_connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            });

            Console.WriteLine($"[TEST] Configured DbContext with connection string: {_connectionString}");
        }

        // Remove DatabaseInitializerHostedService to prevent default seed data
        // that conflicts with our test data (e.g., "Sorcha Local" org vs "Test Organization").
        var databaseInitializerDescriptor = services.SingleOrDefault(d =>
            d.ImplementationType == typeof(DatabaseInitializerHostedService));
        if (databaseInitializerDescriptor != null)
            services.Remove(databaseInitializerDescriptor);

        services.RemoveAll<DatabaseInitializer>();

        // Reconfigure health checks to always-pass.
        var healthCheckBuilderDescriptors = services.Where(d =>
            d.ServiceType.FullName?.Contains("HealthCheck") == true).ToList();
        foreach (var descriptor in healthCheckBuilderDescriptors)
            services.Remove(descriptor);

        services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"]);

        // Register a no-op circuit breaker policy in place of production Polly.
        services.RemoveAll<IAsyncPolicy>();
        services.AddSingleton<IAsyncPolicy>(Policy.NoOpAsync());
    }

    /// <summary>
    /// Ensures the database is seeded with test data. Thread-safe.
    /// </summary>
    public async Task EnsureSeededAsync()
    {
        if (_seeded) return;

        if (TestConfiguration.UsePostgreSQL)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await context.Database.MigrateAsync();
        }

        await TestDataSeeder.SeedAsync(Services);
        _seeded = true;
    }

    /// <summary>
    /// Creates an HttpClient configured for an administrator user.
    /// Uses the seeded test admin user ID for claim provenance.
    /// </summary>
    public new HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.TestAdminUserId.ToString());
        return client;
    }

    /// <summary>
    /// Creates an HttpClient configured for service-to-service authentication.
    /// Used for testing endpoints that require service tokens (RequireService policy).
    /// </summary>
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-service-token");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Service");
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("TenantService")]
public class TenantServiceCollection : ICollectionFixture<TenantServiceWebApplicationFactory>
{
}
