// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

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

    // Well-known IDs delegate to WellKnownIds (single source of truth)
    public static readonly Guid DefaultOrganizationId = WellKnownIds.SystemAdminOrgId;
    public static readonly Guid DefaultAdminUserId = WellKnownIds.DefaultAdminUserId;

    // Well-known IDs for service principals
    public static readonly Guid BlueprintServicePrincipalId = new("00000000-0000-0000-0002-000000000001");
    public static readonly Guid WalletServicePrincipalId = new("00000000-0000-0000-0002-000000000002");
    public static readonly Guid RegisterServicePrincipalId = new("00000000-0000-0000-0002-000000000003");
    public static readonly Guid PeerServicePrincipalId = new("00000000-0000-0000-0002-000000000004");
    public static readonly Guid ValidatorServicePrincipalId = new("00000000-0000-0000-0002-000000000005");
    public static readonly Guid TenantServicePrincipalId = new("00000000-0000-0000-0002-000000000006");
    public static readonly Guid HaipServicePrincipalId = new("00000000-0000-0000-0002-000000000007");
    public static readonly Guid VerifierServicePrincipalId = new("00000000-0000-0000-0002-000000000008");

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

        // --- 1. System Admin Organisation ---
        await SeedSystemAdminOrgAsync(dbContext, cancellationToken);

        // --- 2. Public Organisation ---
        await SeedPublicOrgAsync(dbContext, cancellationToken);

        // --- 3. Admin PlatformUser + UserIdentity + OrgMembership ---
        await SeedAdminUserAsync(dbContext, cancellationToken);

        // --- 4. PlatformSettings ---
        await SeedPlatformSettingsAsync(dbContext, cancellationToken);

        // --- 5. System Register Owner Subscription ---
        await SeedSystemRegisterSubscriptionAsync(dbContext, cancellationToken);
    }

    private async Task SeedSystemAdminOrgAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.SystemAdminOrgId, cancellationToken);

        if (existingOrg != null)
        {
            _logger.LogInformation("System admin organisation already exists: {Name}", existingOrg.Name);
            return;
        }

        _logger.LogInformation("Creating system admin organisation: {Name}", DefaultOrganizationName);

        var systemAdminOrg = new Organization
        {
            Id = WellKnownIds.SystemAdminOrgId,
            Name = _configuration["Seed:OrganizationName"] ?? DefaultOrganizationName,
            Subdomain = _configuration["Seed:OrganizationSubdomain"] ?? DefaultOrganizationSubdomain,
            Status = OrganizationStatus.Active,
            IsPlatformOrg = true,
            OrgType = OrgType.Standard,
            CreatedAt = DateTimeOffset.UtcNow,
            Branding = new BrandingConfiguration
            {
                PrimaryColor = "#6366f1",
                SecondaryColor = "#8b5cf6",
                CompanyTagline = "Decentralised Register Platform"
            }
        };

        dbContext.Organizations.Add(systemAdminOrg);
        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("System admin organisation created with ID: {Id}", systemAdminOrg.Id);
    }

    private async Task SeedPublicOrgAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, cancellationToken);

        if (existingOrg != null)
        {
            _logger.LogInformation("Public organisation already exists: {Name}", existingOrg.Name);
            return;
        }

        _logger.LogInformation("Creating public organisation");

        var publicOrg = new Organization
        {
            Id = WellKnownIds.PublicOrgId,
            Name = "Sorcha Public",
            Subdomain = "public",
            Status = OrganizationStatus.Suspended,
            IsPlatformOrg = true,
            OrgType = OrgType.Public,
            SelfRegistrationEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            Branding = new BrandingConfiguration
            {
                PrimaryColor = "#10b981",
                SecondaryColor = "#34d399",
                CompanyTagline = "Open Access Platform"
            }
        };

        dbContext.Organizations.Add(publicOrg);
        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Public organisation created with ID: {Id} (Status: Suspended)", publicOrg.Id);
    }

    private async Task SeedAdminUserAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        // Check if PlatformUser already exists
        var existingPlatformUser = await dbContext.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == WellKnownIds.DefaultAdminUserId, cancellationToken);

        var adminEmail = _configuration["Seed:AdminEmail"] ?? DefaultAdminEmail;
        var adminPassword = _configuration["Seed:AdminPassword"] ?? DefaultAdminPassword;

        if (existingPlatformUser == null)
        {
            _logger.LogInformation("Creating admin PlatformUser: {Email}", adminEmail);

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);

            var platformUser = new PlatformUser
            {
                Id = WellKnownIds.DefaultAdminUserId,
                Email = adminEmail,
                DisplayName = "System Administrator",
                PasswordHash = passwordHash,
                EmailVerified = true,
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                Status = PlatformUserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.PlatformUsers.Add(platformUser);
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Admin PlatformUser created with ID: {Id}", platformUser.Id);
            _logger.LogWarning("Default admin credentials - Email: {Email}, Password: {Password} - CHANGE IN PRODUCTION!",
                adminEmail, adminPassword);
        }
        else
        {
            _logger.LogInformation("Admin PlatformUser already exists: {Email}", existingPlatformUser.Email);
        }

        // Check if UserIdentity exists in system admin org
        var existingIdentity = await dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == WellKnownIds.DefaultAdminUserId
                && u.OrganizationId == WellKnownIds.SystemAdminOrgId, cancellationToken);

        if (existingIdentity == null)
        {
            _logger.LogInformation("Creating admin UserIdentity in system admin org");

            var adminIdentity = new UserIdentity
            {
                Id = DefaultAdminUserId,
                OrganizationId = WellKnownIds.SystemAdminOrgId,
                PlatformUserId = WellKnownIds.DefaultAdminUserId,
                Email = adminEmail,
                DisplayName = "System Administrator",
                Roles =
                [
                    UserRole.Administrator,
                    UserRole.SystemAdmin,
                    UserRole.Designer,
                    UserRole.Auditor,
                    UserRole.Consumer
                ],
                Status = IdentityStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.UserIdentities.Add(adminIdentity);
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Admin UserIdentity created with ID: {Id}", adminIdentity.Id);
        }

        // Check if OrgMembership exists
        var existingMembership = await dbContext.PlatformUserOrgMemberships
            .FirstOrDefaultAsync(m => m.PlatformUserId == WellKnownIds.DefaultAdminUserId
                && m.OrganizationId == WellKnownIds.SystemAdminOrgId, cancellationToken);

        if (existingMembership == null)
        {
            _logger.LogInformation("Creating admin PlatformUserOrgMembership");

            var membership = new PlatformUserOrgMembership
            {
                PlatformUserId = WellKnownIds.DefaultAdminUserId,
                OrganizationId = WellKnownIds.SystemAdminOrgId,
                Role = UserRole.SystemAdmin.ToString(),
                JoinedAt = DateTimeOffset.UtcNow
            };

            dbContext.PlatformUserOrgMemberships.Add(membership);
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Admin org membership created for system admin org");
        }
    }

    private async Task SeedPlatformSettingsAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingSettings = await dbContext.PlatformSettings
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSettings != null)
        {
            _logger.LogInformation("PlatformSettings already seeded");
            return;
        }

        // Feature 115 FR-019: seed default for PublicOrgEnabled is configurable
        // per-environment so a fresh n1 deploy can do social signup without a
        // manual database edit. Read PlatformSettings:SeedPublicOrgEnabled, default
        // to false (production-safe). Seed-time only — admin UI/API toggles win
        // on subsequent boots because the existingSettings check above short-circuits.
        var seedPublicOrgEnabled = _configuration.GetValue<bool>(
            "PlatformSettings:SeedPublicOrgEnabled",
            defaultValue: false);

        _logger.LogInformation(
            "Seeding PlatformSettings (PublicOrgEnabled={PublicOrgEnabled}, MaxOrgsPerUser=1)",
            seedPublicOrgEnabled);

        var settings = new PlatformSettings
        {
            PublicOrgEnabled = seedPublicOrgEnabled,
            MaxOrgsPerUser = 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = WellKnownIds.DefaultAdminUserId
        };

        dbContext.PlatformSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("PlatformSettings seeded with ID: {Id}", settings.Id);
    }

    /// <summary>
    /// Ensures the system admin org has an Owner subscription to the system register.
    /// The system register is created by Register Service's SystemRegisterBootstrapper,
    /// but it doesn't create the Tenant-side subscription. This seed ensures the
    /// system register appears in the admin's register list from first login.
    /// </summary>
    /// <remarks>
    /// On fresh first-boot, Tenant Service may start before Register Service has created
    /// the system register. This is safe because the subscription is just a reference by
    /// register ID — it doesn't require the register to exist in MongoDB yet. When the
    /// Register Service finishes bootstrapping, the subscription will resolve correctly.
    ///
    /// These constants mirror SystemRegisterConstants in Sorcha.Register.Models.
    /// Tenant Service does not reference that assembly to avoid a circular dependency.
    /// If the system register ID changes, update both locations.
    /// </remarks>
    private async Task SeedSystemRegisterSubscriptionAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        // System register ID: SHA-256("sorcha-system-register") first 32 hex chars
        // Mirrors: Sorcha.Register.Models.Constants.SystemRegisterConstants.SystemRegisterId
        const string systemRegisterId = "aebf26362e079087571ac0932d4db973";
        const string systemRegisterName = "Sorcha System Register";

        var exists = await dbContext.OrganizationRegisterSubscriptions
            .AnyAsync(s => s.OrganizationId == WellKnownIds.SystemAdminOrgId
                        && s.RegisterId == systemRegisterId, cancellationToken);

        if (exists)
        {
            _logger.LogInformation("System register subscription already exists for system admin org");
            return;
        }

        _logger.LogInformation("Creating Owner subscription to system register for system admin org");

        var subscription = new OrganizationRegisterSubscription
        {
            OrganizationId = WellKnownIds.SystemAdminOrgId,
            RegisterId = systemRegisterId,
            RegisterName = systemRegisterName,
            SubscriptionType = SubscriptionType.Owner,
            Status = SubscriptionStatus.Active,
            SubscribedAt = DateTimeOffset.UtcNow,
            SubscribedByUserId = WellKnownIds.DefaultAdminUserId
        };

        dbContext.OrganizationRegisterSubscriptions.Add(subscription);
        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("System register Owner subscription created for org {OrgId}", WellKnownIds.SystemAdminOrgId);
    }

    private async Task SeedServicePrincipalsAsync(TenantDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for service principals...");

        // Define service principals to seed
        var servicePrincipals = new (Guid Id, string ServiceName, string ClientId, string[] Scopes)[]
        {
            (
                BlueprintServicePrincipalId,
                "Blueprint Service",
                "service-blueprint",
                new[] { "blueprints:read", "blueprints:write", "wallets:sign", "register:write" }
            ),
            (
                WalletServicePrincipalId,
                "Wallet Service",
                "service-wallet",
                new[] { "wallets:read", "wallets:write", "wallets:sign", "wallets:encrypt", "wallets:decrypt", "registers:read" }
            ),
            (
                RegisterServicePrincipalId,
                "Register Service",
                "register-service",
                new[] { "registers:read", "registers:write", "registers:query", "validator:write", "wallets:sign" }
            ),
            (
                PeerServicePrincipalId,
                "Peer Service",
                "service-peer",
                new[] { "peers:read", "peers:write", "registers:read" }
            ),
            (
                ValidatorServicePrincipalId,
                "Validator Service",
                "validator-service",
                new[] { "validator:read", "validator:write", "wallets:sign", "registers:read", "blueprints:read" }
            ),
            (
                TenantServicePrincipalId,
                "Tenant Service",
                "tenant-service",
                // persona:crypto grants access to the Wallet Service's
                // /persona/encrypt|decrypt endpoints used by the Consumer
                // persona feature (092). Only the Tenant Service is issued
                // this scope.
                new[] { "wallets:read", "wallets:sign", "wallets:verify", "persona:crypto" }
            ),
            (
                HaipServicePrincipalId,
                "HAIP Service",
                "service-haip",
                // HAIP Service uses its service token to call Blueprint Service's
                // presentation-callback endpoint (Feature 111). RequireService
                // policy only checks the token_type=service claim, so the scope
                // list is informational here. blueprints:read kept narrow.
                new[] { "blueprints:read" }
            ),
            (
                VerifierServicePrincipalId,
                "Verifier Service",
                "service-verifier",
                // The Open/desk Verifier (Sorcha.Verifier) uses its service token to call the HAIP
                // Service's authenticated create-request + result endpoints (Feature 164 / #1189).
                // RequireAuthorization only checks for any authenticated caller, so the scope list is
                // informational here.
                new[] { "blueprints:read" }
            )
        };

        // Environment used to decide fallback/fail-closed behaviour in ServicePrincipalSecretResolver.
        var environment = _configuration["ASPNETCORE_ENVIRONMENT"] ??
                           Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        foreach (var sp in servicePrincipals)
        {
            var existing = await dbContext.ServicePrincipals
                .FirstOrDefaultAsync(s => s.Id == sp.Id, cancellationToken);

            if (existing == null)
            {
                // Per-deploy secret (issue #1412) — configured value wins in every environment;
                // Production/Staging FAIL CLOSED rather than seeding a random secret the client
                // services can never know.
                var configuredSecret = _configuration[$"Seed:ServicePrincipals:{sp.ClientId}"];
                var (clientSecret, secretSource) = ServicePrincipalSecretResolver.Resolve(
                    sp.ClientId, configuredSecret, environment);
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

                // Never log the secret value itself — only where it came from.
                _logger.LogInformation(
                    "Service Principal Created - {ServiceName}, Client ID: {ClientId}, Secret source: {SecretSource}",
                    sp.ServiceName, sp.ClientId, secretSource);
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
    private readonly DatabaseReadySignal _readySignal;
    private readonly ILogger<DatabaseInitializerHostedService> _logger;

    public DatabaseInitializerHostedService(
        DatabaseInitializer initializer,
        DatabaseReadySignal readySignal,
        ILogger<DatabaseInitializerHostedService> logger)
    {
        _initializer = initializer;
        _readySignal = readySignal;
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
        finally
        {
            // Signal readiness even on failure so background services don't hang forever.
            // They will encounter DB errors naturally and log/retry as appropriate.
            _readySignal.Signal();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
