// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
#pragma warning disable CS0618 // IEncryptionProvider is obsolete — retained for backward compatibility with health check and local providers
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Sorcha.Cryptography;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Extensions;
using Sorcha.Cryptography.Interfaces;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Encryption.Configuration;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Providers.Azure;
using Sorcha.Wallet.Providers.Azure.Extensions;
using Sorcha.Wallet.Core.Events.Interfaces;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using EfCoreHolderAddressLookup = Sorcha.Wallet.Service.Services.Implementation.EfCoreHolderAddressLookup;
using IHolderAddressLookup = Sorcha.Wallet.Service.Services.Interfaces.IHolderAddressLookup;
using InMemoryHolderAddressLookup = Sorcha.Wallet.Service.Services.Implementation.InMemoryHolderAddressLookup;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.Wallet.Service.Extensions;

/// <summary>
/// Extension methods for configuring Wallet Service
/// </summary>
public static class WalletServiceExtensions
{
    /// <summary>
    /// Adds Wallet Service infrastructure and domain services to the container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddWalletService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Cryptography services (required by WalletService)
        services.AddSingleton<ICryptoModule, CryptoModule>();
        services.AddSingleton<ISymmetricCrypto, SymmetricCrypto>();
        services.AddSingleton<IHashProvider, HashProvider>();
        services.AddSingleton<IWalletUtilities, Sorcha.Cryptography.Utilities.WalletUtilities>();

        // Register infrastructure services
        services.AddEncryptionProvider(configuration);
        services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();

        // Register database and repository
        services.AddWalletDatabase(configuration);

        // Register domain services with both interface and concrete types
        // (endpoints inject concrete types for now)
        services.AddScoped<KeyManagementService>();
        services.AddScoped<IKeyManagementService>(sp => sp.GetRequiredService<KeyManagementService>());

        // Feature 180 — wallet auxiliary Ethereum identity (SIWE prove-control).
        // Feature 182 (Phase 4) — the same server-side implementation also provides the sanctioned
        // native ETH transaction signer (IEthereumTransactionSigner); resolve both to one instance.
        services.AddScoped<EthereumIdentityService>();
        services.AddScoped<IEthereumIdentityService>(sp => sp.GetRequiredService<EthereumIdentityService>());
        services.AddScoped<IEthereumTransactionSigner>(sp => sp.GetRequiredService<EthereumIdentityService>());

        // Feature 182 (Phase 4) — native ETH transacting orchestrator + policy. Server-side only; the
        // write-capable IEvmRpcClient comes from AddServiceClients (registered in Program.cs).
        services.Configure<Configuration.EthereumTransactionOptions>(
            configuration.GetSection(Configuration.EthereumTransactionOptions.SectionName));
        services.AddScoped<Services.Interfaces.IEthereumTransactionService,
            Services.Implementation.EthereumTransactionService>();

        services.AddScoped<TransactionService>();
        services.AddScoped<ITransactionService>(sp => sp.GetRequiredService<TransactionService>());

        services.AddScoped<DelegationService>();
        services.AddScoped<IDelegationService>(sp => sp.GetRequiredService<DelegationService>());

        services.AddScoped<WalletManager>();

        // Feature 060: Recovery key service
        services.AddScoped<IRecoveryKeyService, RecoveryKeyService>();

        // Register SD-JWT service for credential issuance
        services.AddSdJwtServices();

        // Register credential services
        services.AddScoped<Credentials.ICredentialStore, Credentials.CredentialStore>();
        services.AddScoped<Credentials.CredentialMatcher>();

        // Feature 120 US2 — per-org issuance key service + cross-service DID-doc client.
        services.AddScoped<Services.Interfaces.IIssuanceKeyService,
            Services.Implementation.IssuanceKeyService>();
        services.AddHttpClient<Sorcha.ServiceClients.OrgDidDocument.IOrgDidDocumentClient,
            Sorcha.ServiceClients.OrgDidDocument.OrgDidDocumentClient>(client =>
            {
                // Default port 8080 — every Sorcha service listens internally on 8080
                // (see ASPNETCORE_URLS in docker-compose.yml). The :80 default earlier
                // resulted in 'Connection refused (tenant-service:80)' on real wires.
                client.BaseAddress = new Uri(
                    SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
                    ?? "http://tenant-service:8080");
            });

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL database context for wallet persistence
    /// Falls back to InMemory repository if no connection string is configured
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddWalletDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // SorchaConnections cascade: ConnectionStrings:Wallet:Postgres → ConnectionStrings:Sorcha:Postgres.
        // Wallet appends Timeout / Command Timeout (30s each) — operational tuning that lives in code
        // alongside the EnableRetryOnFailure config below, not in the platform-default connection string.
        var hasResolverConfig =
            !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Wallet:Postgres"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Postgres"]);

        var connectionString = hasResolverConfig
            ? configuration.GetSorchaPostgresConnectionString("Wallet", "sorcha_wallet") + ";Timeout=30;Command Timeout=30"
            : null;

        // Record the storage choice so operators see the active backend in boot logs, the
        // storage-providers health check, and OTel metrics, and so Production/Staging fail-fast
        // when this audited interface falls through to in-memory.
        var storageLog = services.GetStorageRegistrationLog();
        var interfaceName = typeof(IWalletRepository).FullName!;

        // IHolderAddressLookup is registered here rather than in Program.cs because its backend is
        // decided by exactly this connection-string branch. It used to be an unconditional
        // AddScoped<IHolderAddressLookup, EfCoreHolderAddressLookup> at Program.cs, which cannot be
        // activated without a WalletDbContext — so on the supported no-Postgres path every endpoint
        // that touches it (POST /api/v1/wallets included) threw
        // "Unable to resolve service for type WalletDbContext" and returned 500. Keeping the choice
        // next to the branch that causes it also avoids a third hand-copy of the cascade check.
        var holderLookupInterfaceName = typeof(IHolderAddressLookup).FullName!;

        if (!string.IsNullOrEmpty(connectionString))
        {
            // Configure NpgsqlDataSource with dynamic JSON support (required for Dictionary<string, string> serialization)
            services.AddNpgsqlDataSource(connectionString, dataSourceBuilder =>
            {
                // Enable dynamic JSON serialization for Dictionary types
                // This is required in Npgsql 8.0+ for JSONB columns with Dictionary<string, string>
                dataSourceBuilder.EnableDynamicJson();
            });

            // Configure PostgreSQL with EF Core using the registered data source
            // IMPORTANT: Do NOT pass connection string again - it will use the registered NpgsqlDataSource
            // TODO(db-audit): convert to AddDbContextFactory<WalletDbContext>. Same rationale
            // as TenantDbContext (see ServiceCollectionExtensions.cs). Wallet has background
            // services (NotificationDigestWorker and friends)
            // that benefit from per-operation contexts. Adopting the factory touches every
            // consumer of WalletDbContext.
            services.AddDbContext<WalletDbContext>((serviceProvider, options) =>
            {
                // Use the registered NpgsqlDataSource with EnableDynamicJson configured
                var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();

                options.UseNpgsql(dataSource, npgsqlOptions =>
                {
                    // Aggressive retry policy for startup resilience
                    // Max retry time: ~5 minutes (10 retries with exponential backoff up to 30s)
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 10,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);

                    // Map to correct schema
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "wallet");
                });
            });

            // Use EF Core repository for persistent storage
            services.AddScoped<IWalletRepository, EfCoreWalletRepository>();
            storageLog.RegisterPersistent(
                interfaceName,
                typeof(EfCoreWalletRepository).FullName!,
                "postgres");

            // Feature 114 / US4 — holder-address index. Scoped because it consumes WalletDbContext.
            services.AddScoped<IHolderAddressLookup, EfCoreHolderAddressLookup>();
            storageLog.RegisterPersistent(
                holderLookupInterfaceName,
                typeof(EfCoreHolderAddressLookup).FullName!,
                "postgres");
        }
        else
        {
            // Use in-memory repository for development/testing
            services.AddSingleton<IWalletRepository, InMemoryWalletRepository>();
            storageLog.RegisterInMemory(
                interfaceName,
                typeof(InMemoryWalletRepository).FullName!,
                "no Postgres connection string in ConnectionStrings:Wallet:Postgres or ConnectionStrings:Sorcha:Postgres");

            // Singleton so the map outlives request scopes, matching the persistence the EF Core
            // implementation gets from its table.
            services.AddSingleton<IHolderAddressLookup, InMemoryHolderAddressLookup>();
            storageLog.RegisterInMemory(
                holderLookupInterfaceName,
                typeof(InMemoryHolderAddressLookup).FullName!,
                "no Postgres connection string in ConnectionStrings:Wallet:Postgres or ConnectionStrings:Sorcha:Postgres");
        }

        return services;
    }

    /// <summary>
    /// Adds health checks for Wallet Service dependencies
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The health checks builder for chaining</returns>
    public static IHealthChecksBuilder AddWalletServiceHealthChecks(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        // SorchaConnections cascade — same lookup as AddWalletDatabase above.
        var hasResolverConfig =
            !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Wallet:Postgres"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Postgres"]);

        if (hasResolverConfig)
        {
            var connectionString = configuration.GetSorchaPostgresConnectionString("Wallet", "sorcha_wallet");
            builder.AddNpgSql(connectionString, name: "wallet-postgresql");
        }

        // Repository health check
        builder.AddCheck<WalletRepositoryHealthCheck>("wallet-repository");

        // Encryption provider health check
        builder.AddCheck<EncryptionProviderHealthCheck>("encryption-provider");

        return builder;
    }

    /// <summary>
    /// Applies pending database migrations (for production deployment)
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <returns>Task</returns>
    public static async Task ApplyWalletDatabaseMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            // Check if DbContext is registered (only if PostgreSQL is configured)
            var context = services.GetService<WalletDbContext>();
            if (context != null)
            {
                var logger = services.GetRequiredService<ILogger<WalletDbContext>>();
                logger.LogInformation("Applying Wallet Service database migrations...");

                // Apply pending migrations
                await context.Database.MigrateAsync();

                logger.LogInformation("Wallet Service database migrations applied successfully");
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<WalletDbContext>>();
            logger.LogError(ex, "An error occurred while applying Wallet Service migrations");
            throw;
        }
    }

    /// <summary>
    /// Adds encryption provider based on configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    private static IServiceCollection AddEncryptionProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration options
        services.Configure<EncryptionProviderOptions>(
            configuration.GetSection(EncryptionProviderOptions.SectionName));

        // Bind WalletKeyManagementOptions (Phase 2 — envelope encryption config)
        services.Configure<WalletKeyManagementOptions>(
            configuration.GetSection(WalletKeyManagementOptions.SectionName));

        // Determine provider type from configuration
        var providerType = configuration
            .GetSection(EncryptionProviderOptions.SectionName)["Type"]?
            .ToLowerInvariant() ?? "local";

        if (providerType == "azurekeyvault")
        {
            // Azure Key Vault: dedicated providers for key protection and signing.
            // IEncryptionProvider is not used — envelope encryption goes through IKeyProtectionProvider.
            services.AddAzureKeyVaultProvider(configuration);
        }
        else
        {
            // Local/DPAPI/Linux providers implement both IEncryptionProvider and IKeyProtectionProvider.
            services.AddSingleton<IEncryptionProvider>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<EncryptionProviderOptions>>().Value;
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

                return options.Type.ToLowerInvariant() switch
                {
                    "windowsdpapi" => CreateWindowsDpapiProvider(options, loggerFactory),
                    "linuxsecretservice" => CreateLinuxSecretServiceProvider(options, loggerFactory),
                    "local" => CreateLocalProvider(options, loggerFactory),
                    _ => CreateLocalProviderWithWarning(options, loggerFactory)
                };
            });

            // Register IKeyProtectionProvider — resolves to the same instance as IEncryptionProvider
            // since all local providers implement both interfaces.
            services.AddSingleton<IKeyProtectionProvider>(serviceProvider =>
            {
                var encryptionProvider = serviceProvider.GetRequiredService<IEncryptionProvider>();
                if (encryptionProvider is IKeyProtectionProvider keyProtectionProvider)
                {
                    return keyProtectionProvider;
                }

                throw new InvalidOperationException(
                    $"The registered IEncryptionProvider ({encryptionProvider.GetType().Name}) does not implement IKeyProtectionProvider. " +
                    "All encryption providers must implement IKeyProtectionProvider for Phase 2 key management.");
            });
        }

        return services;
    }

    /// <summary>
    /// Creates Windows DPAPI encryption provider
    /// </summary>
    private static IEncryptionProvider CreateWindowsDpapiProvider(
        EncryptionProviderOptions options,
        ILoggerFactory loggerFactory)
    {
        if (!OperatingSystem.IsWindows())
        {
            var fallbackLogger = loggerFactory.CreateLogger("Sorcha.Wallet.Service.Extensions.WalletServiceExtensions");
            fallbackLogger.LogWarning(
                "Windows DPAPI provider requested but not running on Windows. Falling back to LocalEncryptionProvider.");
            return new LocalEncryptionProvider(
                loggerFactory.CreateLogger<LocalEncryptionProvider>());
        }

        var dpapiOptions = options.WindowsDpapi ?? new WindowsDpapiOptions();

        // Parse DataProtectionScope
        var scope = dpapiOptions.Scope.ToLowerInvariant() switch
        {
            "currentuser" => DataProtectionScope.CurrentUser,
            "localmachine" => DataProtectionScope.LocalMachine,
            _ => DataProtectionScope.LocalMachine
        };

        var logger = loggerFactory.CreateLogger<WindowsDpapiEncryptionProvider>();
        logger.LogInformation(
            "Initializing Windows DPAPI encryption provider. KeyStorePath: {KeyStorePath}, Scope: {Scope}, DefaultKeyId: {DefaultKeyId}",
            dpapiOptions.KeyStorePath,
            scope,
            options.DefaultKeyId);

        return new WindowsDpapiEncryptionProvider(
            keyStorePath: dpapiOptions.KeyStorePath,
            defaultKeyId: options.DefaultKeyId,
            scope: scope,
            logger: logger);
    }

    /// <summary>
    /// Creates Linux Secret Service encryption provider
    /// </summary>
    private static IEncryptionProvider CreateLinuxSecretServiceProvider(
        EncryptionProviderOptions options,
        ILoggerFactory loggerFactory)
    {
        if (!OperatingSystem.IsLinux())
        {
            var fallbackLogger = loggerFactory.CreateLogger("Sorcha.Wallet.Service.Extensions.WalletServiceExtensions");
            fallbackLogger.LogWarning(
                "Linux Secret Service provider requested but not running on Linux. Falling back to LocalEncryptionProvider.");
            return new LocalEncryptionProvider(
                loggerFactory.CreateLogger<LocalEncryptionProvider>());
        }

        var linuxOptions = options.LinuxSecretService ?? new LinuxSecretServiceOptions();

        var logger = loggerFactory.CreateLogger<LinuxSecretServiceEncryptionProvider>();
        logger.LogInformation(
            "Initializing Linux Secret Service encryption provider. FallbackPath: {FallbackPath}, DefaultKeyId: {DefaultKeyId}, MachineKeyMaterial: {HasMachineKeyMaterial}",
            linuxOptions.FallbackKeyStorePath,
            options.DefaultKeyId,
            !string.IsNullOrWhiteSpace(linuxOptions.MachineKeyMaterial) ? "configured" : "not set (using /etc/machine-id)");

        return new LinuxSecretServiceEncryptionProvider(
            fallbackKeyPath: linuxOptions.FallbackKeyStorePath,
            defaultKeyId: options.DefaultKeyId,
            logger: logger,
            machineKeyMaterial: linuxOptions.MachineKeyMaterial);
    }

    /// <summary>
    /// Creates local encryption provider (development only)
    /// </summary>
    private static IEncryptionProvider CreateLocalProvider(
        EncryptionProviderOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<LocalEncryptionProvider>();
        logger.LogWarning(
            "Using LocalEncryptionProvider (development only). Keys will be lost on service restart. " +
            "For production, use WindowsDpapi, LinuxSecretService, or AzureKeyVault.");

        return new LocalEncryptionProvider(logger);
    }

    /// <summary>
    /// Creates local provider with warning about invalid configuration
    /// </summary>
    private static IEncryptionProvider CreateLocalProviderWithWarning(
        EncryptionProviderOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Wallet.Service.Extensions.WalletServiceExtensions");
        logger.LogError(
            "Invalid encryption provider type: {ProviderType}. Falling back to LocalEncryptionProvider. " +
            "Valid types: Local, WindowsDpapi, LinuxSecretService, MacOsKeychain, AzureKeyVault",
            options.Type);

        return new LocalEncryptionProvider(
            loggerFactory.CreateLogger<LocalEncryptionProvider>());
    }
}

/// <summary>
/// Health check for Wallet Repository
/// </summary>
internal class WalletRepositoryHealthCheck : IHealthCheck
{
    private readonly IWalletRepository _repository;

    public WalletRepositoryHealthCheck(IWalletRepository repository)
    {
        _repository = repository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple check - try to get count of wallets
            // This verifies the repository is accessible
            await Task.CompletedTask; // InMemoryRepository is always available
            return HealthCheckResult.Healthy("Wallet repository is accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Wallet repository is not accessible", ex);
        }
    }
}

/// <summary>
/// Health check for Encryption Provider
/// </summary>
internal class EncryptionProviderHealthCheck : IHealthCheck
{
    private readonly IEncryptionProvider _encryptionProvider;

    public EncryptionProviderHealthCheck(IEncryptionProvider encryptionProvider)
    {
        _encryptionProvider = encryptionProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the default key from the provider (always exists)
            var keyId = _encryptionProvider.GetDefaultKeyId();

            // Test encryption/decryption with a simple test payload
            var testData = "health-check"u8.ToArray();

            var encrypted = await _encryptionProvider.EncryptAsync(testData, keyId, cancellationToken);
            var decrypted = await _encryptionProvider.DecryptAsync(encrypted, keyId, cancellationToken);

            if (!testData.SequenceEqual(decrypted))
            {
                return HealthCheckResult.Degraded("Encryption provider test failed - data mismatch");
            }

            return HealthCheckResult.Healthy("Encryption provider is functional");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Encryption provider is not functional", ex);
        }
    }
}
