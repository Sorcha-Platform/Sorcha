// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data;

/// <summary>
/// Handles database initialization including migrations and seed data.
/// Runs automatically on application startup.
/// </summary>
public class DatabaseInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IConfiguration _configuration;

    // Well-known IDs for default organization and admin
    public static readonly Guid DefaultOrganizationId = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid DefaultAdminUserId = new("00000000-0000-0000-0001-000000000001");

    // Well-known IDs for service principals
    public static readonly Guid BlueprintServicePrincipalId = new("00000000-0000-0000-0002-000000000001");
    public static readonly Guid WalletServicePrincipalId = new("00000000-0000-0000-0002-000000000002");
    public static readonly Guid RegisterServicePrincipalId = new("00000000-0000-0000-0002-000000000003");
    public static readonly Guid PeerServicePrincipalId = new("00000000-0000-0000-0002-000000000004");
    public static readonly Guid ValidatorServicePrincipalId = new("00000000-0000-0000-0002-000000000005");
    public static readonly Guid TenantServicePrincipalId = new("00000000-0000-0000-0002-000000000006");

    // Default credentials (can be overridden via configuration)
    public const string DefaultAdminEmail = "admin@sorcha.local";
    public const string DefaultAdminPassword = "Dev_Pass_2025!";
    public const string DefaultOrganizationName = "Sorcha Local";
    public const string DefaultOrganizationSubdomain = "sorcha-local";

    public DatabaseInitializer(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializer> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Initializes the database by applying migrations and seeding default data.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database initialization...");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

            // Check if we're using a real database (not in-memory)
            var isInMemory = dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";

            if (!isInMemory)
            {
                // Apply pending migrations
                await ApplyMigrationsAsync(dbContext, cancellationToken);
            }
            else
            {
                // Ensure in-memory database is created
                await dbContext.Database.EnsureCreatedAsync(cancellationToken);
                _logger.LogInformation("In-memory database created");
            }

            // Seed default data
            await SeedDefaultDataAsync(dbContext, cancellationToken);

            // Seed service principals
            await SeedServicePrincipalsAsync(dbContext, cancellationToken);

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed");
            throw;
        }
    }

    private async Task ApplyMigrationsAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for pending database migrations...");

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        var pendingList = pendingMigrations.ToList();

        if (pendingList.Count > 0)
        {
            _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pendingList.Count, string.Join(", ", pendingList));

            await dbContext.Database.MigrateAsync(cancellationToken);

            _logger.LogInformation("Database migrations applied successfully");
        }
        else
        {
            _logger.LogInformation("Database is up to date, no migrations to apply");
        }
    }

    private async Task SeedDefaultDataAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for seed data...");

        // Check if default organization exists
        var existingOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == DefaultOrganizationId, cancellationToken);

        if (existingOrg == null)
        {
            _logger.LogInformation("Creating default organization: {Name}", DefaultOrganizationName);

            var organization = new Organization
            {
                Id = DefaultOrganizationId,
                Name = _configuration["Seed:OrganizationName"] ?? DefaultOrganizationName,
                Subdomain = _configuration["Seed:OrganizationSubdomain"] ?? DefaultOrganizationSubdomain,
                Status = OrganizationStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                Branding = new BrandingConfiguration
                {
                    PrimaryColor = "#6366f1",
                    SecondaryColor = "#8b5cf6",
                    CompanyTagline = "Distributed Ledger Platform"
                }
            };

            dbContext.Organizations.Add(organization);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Default organization created with ID: {Id}", organization.Id);
        }
        else
        {
            _logger.LogInformation("Default organization already exists: {Name}", existingOrg.Name);
        }

        // Check if default admin user exists
        var existingAdmin = await dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Id == DefaultAdminUserId, cancellationToken);

        if (existingAdmin == null)
        {
            var adminEmail = _configuration["Seed:AdminEmail"] ?? DefaultAdminEmail;
            var adminPassword = _configuration["Seed:AdminPassword"] ?? DefaultAdminPassword;

            _logger.LogInformation("Creating default administrator: {Email}", adminEmail);

            // Hash the password using BCrypt
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);

            var adminUser = new UserIdentity
            {
                Id = DefaultAdminUserId,
                OrganizationId = DefaultOrganizationId,
                Email = adminEmail,
                DisplayName = "System Administrator",
                PasswordHash = passwordHash,
                ExternalIdpSubject = null, // Local authentication
                Roles =
                [
                    UserRole.Administrator,
                    UserRole.SystemAdmin,
                    UserRole.Designer,
                    UserRole.Auditor,
                    UserRole.Member
                ],
                Status = IdentityStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.UserIdentities.Add(adminUser);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Default administrator created with ID: {Id}", adminUser.Id);
            _logger.LogWarning("Default admin credentials - Email: {Email}, Password: {Password} - CHANGE IN PRODUCTION!",
                adminEmail, adminPassword);
        }
        else
        {
            _logger.LogInformation("Default administrator already exists: {Email}", existingAdmin.Email);
        }
    }

    private async Task SeedServicePrincipalsAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for service principals...");

        // Define service principals to seed
        // DevSecret is used in Development environment for docker-compose compatibility
        var servicePrincipals = new (Guid Id, string ServiceName, string ClientId, string? DevSecret, string[] Scopes)[]
        {
            (
                BlueprintServicePrincipalId,
                "Blueprint Service",
                "service-blueprint",
                "blueprint-service-secret",
                new[] { "blueprints:read", "blueprints:write", "wallets:sign", "register:write" }
            ),
            (
                WalletServicePrincipalId,
                "Wallet Service",
                "service-wallet",
                "wallet-service-secret",
                new[] { "wallets:read", "wallets:write", "wallets:sign", "wallets:encrypt", "wallets:decrypt" }
            ),
            (
                RegisterServicePrincipalId,
                "Register Service",
                "register-service",
                "register-service-secret",
                new[] { "registers:read", "registers:write", "registers:query", "validator:write", "wallets:sign" }
            ),
            (
                PeerServicePrincipalId,
                "Peer Service",
                "service-peer",
                "peer-service-secret",
                new[] { "peers:read", "peers:write", "registers:read" }
            ),
            (
                ValidatorServicePrincipalId,
                "Validator Service",
                "validator-service",
                "validator-service-secret",
                new[] { "validator:read", "validator:write", "wallets:sign", "registers:read" }
            ),
            (
                TenantServicePrincipalId,
                "Tenant Service",
                "tenant-service",
                "tenant-service-secret",
                new[] { "wallets:read", "wallets:sign", "wallets:verify" }
            )
        };

        // Check if we're in development mode - use predictable secrets for docker-compose
        var isDevelopment = _configuration["ASPNETCORE_ENVIRONMENT"] == "Development" ||
                           Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        foreach (var sp in servicePrincipals)
        {
            var existing = await dbContext.ServicePrincipals
                .FirstOrDefaultAsync(s => s.Id == sp.Id, cancellationToken);

            if (existing == null)
            {
                // Use dev secret in Development, otherwise generate random
                var clientSecret = isDevelopment && !string.IsNullOrEmpty(sp.DevSecret)
                    ? sp.DevSecret
                    : GenerateClientSecret();
                var encryptedSecret = EncryptClientSecret(clientSecret);

                _logger.LogInformation("Creating service principal: {ServiceName}", sp.ServiceName);

                var servicePrincipal = new ServicePrincipal
                {
                    Id = sp.Id,
                    ServiceName = sp.ServiceName,
                    ClientId = sp.ClientId,
                    ClientSecretEncrypted = encryptedSecret,
                    Scopes = sp.Scopes,
                    Status = ServicePrincipalStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                dbContext.ServicePrincipals.Add(servicePrincipal);
                await dbContext.SaveChangesAsync(cancellationToken);

                if (isDevelopment && !string.IsNullOrEmpty(sp.DevSecret))
                {
                    _logger.LogInformation(
                        "Service Principal Created (Development) - {ServiceName}, Client ID: {ClientId}",
                        sp.ServiceName, sp.ClientId);
                }
                else
                {
                    _logger.LogWarning(
                        "Service Principal Created - {ServiceName}\n" +
                        "  Client ID:     {ClientId}\n" +
                        "  Client Secret: {ClientSecret}\n" +
                        "  Scopes:        {Scopes}\n" +
                        "  ⚠️  SAVE THIS SECRET - It will not be shown again!",
                        sp.ServiceName, sp.ClientId, clientSecret, string.Join(", ", sp.Scopes));
                }
            }
            else
            {
                // Update scopes if they've changed (e.g., new scopes added in code)
                var existingScopes = new HashSet<string>(existing.Scopes);
                var expectedScopes = new HashSet<string>(sp.Scopes);
                if (!existingScopes.SetEquals(expectedScopes))
                {
                    existing.Scopes = sp.Scopes;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation(
                        "Updated scopes for service principal {ServiceName}: {Scopes}",
                        existing.ServiceName, string.Join(", ", sp.Scopes));
                }
                else
                {
                    _logger.LogInformation("Service principal already exists: {ServiceName}", existing.ServiceName);
                }
            }
        }
    }

    /// <summary>
    /// Generates a cryptographically secure client secret.
    /// </summary>
    private static string GenerateClientSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Encrypts the client secret for storage using SHA256 hash.
    /// </summary>
    private static byte[] EncryptClientSecret(string secret)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(secret));
    }
}

/// <summary>
/// Hosted service that runs database initialization on application startup.
/// </summary>
public class DatabaseInitializerHostedService : IHostedService
{
    private readonly DatabaseInitializer _initializer;
    private readonly ILogger<DatabaseInitializerHostedService> _logger;

    public DatabaseInitializerHostedService(
        DatabaseInitializer initializer,
        ILogger<DatabaseInitializerHostedService> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Database initializer hosted service starting...");

        try
        {
            await _initializer.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database. Service may not function correctly.");
            // Don't rethrow - allow service to start even if DB init fails
            // Health checks will report unhealthy status
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
