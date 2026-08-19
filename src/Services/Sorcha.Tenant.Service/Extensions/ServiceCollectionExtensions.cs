// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.CircuitBreaker;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Telemetry;
using StackExchange.Redis;

namespace Sorcha.Tenant.Service.Extensions;

/// <summary>
/// Extension methods for WebApplication to add database initialization.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Registers database initializer and ready signal for explicit startup initialization.
    /// Migrations and seeding are run in Program.cs before app.Run() to prevent
    /// race conditions with background services that query the database.
    /// </summary>
    public static IServiceCollection AddDatabaseInitializer(this IServiceCollection services)
    {
        services.AddSingleton<DatabaseReadySignal>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }

    /// <summary>
    /// Adds the audit cleanup background service for automatic purge of expired audit entries.
    /// </summary>
    public static IServiceCollection AddAuditCleanup(this IServiceCollection services)
    {
        services.AddHostedService<AuditCleanupService>();
        return services;
    }

    /// <summary>
    /// Feature 116: registers the re-authentication challenge primitive
    /// (entity repository, ladder service, OpenTelemetry meter, and the
    /// daily cleanup BackgroundService) and the last-method floor service
    /// shared across every Remove endpoint and the aggregate read.
    /// </summary>
    public static IServiceCollection AddTenantAccountManagement(this IServiceCollection services)
    {
        services.AddScoped<IAuthChallengeRepository, AuthChallengeRepository>();
        services.AddScoped<IAuthChallengeService, AuthChallengeService>();
        services.AddScoped<IAuthMethodService, AuthMethodService>();
        services.AddScoped<ISocialLinkService, SocialLinkService>();
        services.AddScoped<IPasswordManagementService, PasswordManagementService>();

        // Feature 168 — step-up social account linking: signing key (singleton, derived once)
        // and token service (scoped, stateless operations).
        services.AddSingleton<LinkPendingTokenKey>();
        services.AddScoped<ILinkPendingTokenService, LinkPendingTokenService>();

        // AuthMetrics is a singleton wrapper around the OpenTelemetry meter —
        // counters are process-wide and must outlive any individual scope.
        services.AddSingleton<AuthMetrics>();

        // Daily prune of consumed/expired challenge tokens (7-day retention).
        services.AddHostedService<AuthChallengeTokenCleanupService>();

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

        // Demo-environment banner flag (e.g. n1.sorcha.dev). Default off; flipped
        // on per deployment via the DemoEnvironment__Enabled env var.
        services.Configure<DemoEnvironmentSettings>(configuration.GetSection("DemoEnvironment"));

        // Feature 120 US2 — per-org DID document service.
        services.Configure<Configuration.TenantSettings>(
            configuration.GetSection(Configuration.TenantSettings.SectionName));
        services.AddScoped<IOrgDidDocumentService, OrgDidDocumentService>();

        // Add FIDO2/WebAuthn services
        services.AddFido2WebAuthn(configuration);

        // Add in-memory cache (used by OIDC discovery, password breach check, etc.)
        services.AddMemoryCache();

        // Add distributed cache for OIDC flow state (upgraded to Redis in production via Aspire)
        services.AddDistributedMemoryCache();

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL database context with multi-tenant support.
    /// Uses the SorchaConnections cascade: ConnectionStrings:Tenant:Postgres → ConnectionStrings:Sorcha:Postgres.
    /// Falls back to an in-memory provider when neither key is configured (tests / no-DB scenarios).
    /// </summary>
    public static IServiceCollection AddTenantDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var hasResolverConfig =
            !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Tenant:Postgres"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Postgres"]);

        if (hasResolverConfig)
        {
            var connectionString = configuration.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");

            // Build a Npgsql data source with dynamic JSON support (required for
            // Dictionary<string, object> → JSONB columns like AuditLogEntry.Details)
            var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            // TODO(db-audit): convert to AddDbContextFactory<TenantDbContext>. Scoped lifetime
            // forces background services (AuditCleanupService,
            // CustomDomainVerificationService) to wrap each DB call in IServiceScopeFactory
            // ceremony; the factory pattern (used by Blueprint + Peer) is safer for parallel
            // and background work. Adopting it touches every consumer of TenantDbContext.
            services.AddDbContext<TenantDbContext>(options =>
            {
                options.UseNpgsql(dataSource, npgsqlOptions =>
                {
                    // Aggressive retry policy for startup resilience
                    // Max retry time: ~5 minutes (10 retries with exponential backoff up to 30s)
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 10,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                });
            });
        }
        else
        {
            services.AddDbContext<TenantDbContext>(options =>
            {
                // Use in-memory database for testing
                options.UseInMemoryDatabase("TenantServiceTestDb");
            });
        }

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
        // Spec 136: tiered-audience identity metrics (Sorcha.Identity meter). Singleton so the
        // instruments are created once; consumed by TokenService to record tokens minted per tier.
        services.AddSingleton<Sorcha.ServiceDefaults.Auth.IdentityMetrics>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IServiceAuthService, ServiceAuthService>();
        services.AddScoped<ITotpService, TotpService>();
        // Feature 146 — at-rest secret protection (AES-256-GCM; key derived from the JWT signing key
        // via HKDF, or the Tenant:SecretProtection:Key override; fail-closed in Production/Staging).
        services.AddSingleton<TenantSecretKeyResolver>();
        services.AddSingleton<ISecretProtectionProvider>(sp =>
        {
            var (key, keyId) = sp.GetRequiredService<TenantSecretKeyResolver>().ResolveProtectionKey();
            return new SoftwareSecretProtectionProvider(key, keyId);
        });
        // Feature 146 — login-token HMAC key, derived once from the JWT signing key (stable across replicas/restarts).
        services.AddSingleton<LoginTokenSigningKey>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        services.AddScoped<IRegisterSubscriptionService, RegisterSubscriptionService>();
        services.AddScoped<IRegisterInvitationService, RegisterInvitationService>();
        services.AddScoped<ISocialLoginService, SocialLoginService>();
        services.AddScoped<ILoginService, LoginService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        // Platform services
        services.AddScoped<IPlatformUserService, PlatformUserService>();
        // Feature 118 / US3 follow-up #3 — Tenant-side inbox writer for org membership events.
        services.AddScoped<ITenantMembershipInboxWriter, TenantMembershipInboxWriter>();
        // Feature 118 — Tenant-side inbox writer for security events (2FA enable/disable).
        services.AddScoped<ITenantSecurityInboxWriter, TenantSecurityInboxWriter>();
        // Feature 150 — always-notify: composes the inbox writer + a Sorcha-branded email.
        services.AddScoped<ISecurityChangeNotifier, SecurityChangeNotifier>();

        // Feature 150 US2 — server-sent OTP (Redis GETDEL via IAtomicDistributedCache) + the
        // verification-channel registry. The Email channel is always registered; the SMS channel
        // (US3) is added by AddSmsChannel only when an ISmsSender provider is configured, so an
        // unconfigured install never resolves SMS. TimeProvider drives deterministic OTP expiry.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IServerSentOtpService, ServerSentOtpService>();
        services.AddScoped<IVerificationChannel, EmailOtpChannel>();
        services.AddScoped<IVerificationChannelRegistry, VerificationChannelRegistry>();
        services.AddScoped<IPlatformSettingsService, PlatformSettingsService>();
        services.AddScoped<IOrgProvisioningService, OrgProvisioningService>();
        services.AddScoped<IPlatformUserProvisioningService, PlatformUserProvisioningService>();

        // Feature 114: Citizen wallet device registry
        services.AddScoped<IPlatformUserDeviceService, PlatformUserDeviceService>();

        // Issue #1264: live (non-token) resolution of identity claims, so a decision made after a
        // token was minted reads current server state rather than a mint-time snapshot.
        services.AddScoped<ILivePlatformUserClaimsService, LivePlatformUserClaimsService>();

        // IDP configuration services
        services.AddHttpClient<IOidcDiscoveryService, OidcDiscoveryService>();
        services.AddScoped<IIdpConfigurationService, IdpConfigurationService>();
        services.AddHttpClient();

        // OIDC authentication flow services
        // Review M3a: resolve + cache the provider JWKS so ID-token signatures are verified.
        services.AddMemoryCache();
        services.AddScoped<IOidcSigningKeyResolver, JwksOidcSigningKeyResolver>();
        services.AddScoped<IOidcExchangeService, OidcExchangeService>();
        services.AddScoped<IOidcProvisioningService, OidcProvisioningService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();

        // Password policy service (NIST + HIBP breach check)
        services.AddHttpClient<IPasswordPolicyService, PasswordPolicyService>();

        // Invitation and dashboard services
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Custom domain services
        services.AddScoped<ICustomDomainRepository, CustomDomainRepository>();
        services.AddScoped<ICustomDomainService, CustomDomainService>();
        services.AddSingleton<IDnsResolver, DnsResolver>();
        services.AddHostedService<CustomDomainVerificationService>();

        // Background reconciliation for org wallet provisioning
        // OrgWalletReconciliationService REMOVED (#1525). It swept every 60s for orgs with no
        // wallet and created one server-side — generating a BIP39 recovery phrase with no human
        // present to receive it. The phrase is shown once and never stored, so the sweep silently
        // destroyed it and left organisations unrecoverable. It also masked the fact that the
        // platform-admin creation path never created a wallet at all, which is what #1518 was
        // really about. An organisation's wallet is now created by its OWN admin, deliberately,
        // via POST /api/organizations/{id}/wallet — a null WalletAddress is the awaiting state,
        // not something for a timer to quietly fix.

        return services;
    }

    /// <summary>
    /// Adds Redis connection with circuit breaker for token revocation.
    /// Uses the SorchaConnections cascade: ConnectionStrings:Tenant:Redis → ConnectionStrings:Sorcha:Redis.
    /// </summary>
    public static IServiceCollection AddTenantRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString =
            configuration["ConnectionStrings:Tenant:Redis"]
            ?? configuration["ConnectionStrings:Sorcha:Redis"];

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
                throw new InvalidOperationException(
                    "Redis is not configured. Set ConnectionStrings:Sorcha:Redis (platform default) " +
                    "or ConnectionStrings:Tenant:Redis (override)."));
        }

        // Configure token revocation
        services.Configure<TokenRevocationConfiguration>(
            configuration.GetSection("TokenRevocation"));

        services.AddScoped<ITokenRevocationService, TokenRevocationService>();

        return services;
    }

    /// <summary>
    /// Adds email sending services: backend (SMTP or Azure Communication Services),
    /// Scriban-backed template renderer, branding resolver, transactional facade, and
    /// the welcome-email dispatcher.
    /// </summary>
    public static IServiceCollection AddTenantEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailSettings>(configuration.GetSection("Email"));

        // Backend selection: Azure Communication Services when a connection string is
        // supplied; otherwise SMTP via MailKit. Both implementations emit multipart
        // HTML + plaintext.
        var acsConnectionString = configuration["Email:AcsConnectionString"];
        if (!string.IsNullOrEmpty(acsConnectionString))
        {
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        // Feature 150 US3 — SMS OTP, config-gated: only when an operator configures a provider
        // (Sms:AcsConnectionString) do we register ISmsSender + the SMS channel + the phone-verify
        // service. Unconfigured ⇒ the SMS channel never resolves and SmsAvailable stays false.
        if (!string.IsNullOrEmpty(configuration["Sms:AcsConnectionString"]))
        {
            // Bind SmsSettings so AcsSmsSender can read AllowNoOpSender / FromNumber via IOptions.
            services.Configure<Services.Sms.SmsSettings>(configuration.GetSection("Sms"));
            services.AddSingleton<Services.Sms.ISmsSender, Services.Sms.AcsSmsSender>();
            services.AddScoped<IVerificationChannel, SmsOtpChannel>();
            services.AddScoped<ISmsPhoneVerificationService, SmsPhoneVerificationService>();
        }

        // Template renderer is a singleton — templates are parsed once at startup and
        // rendering is pure. Fails fast on parse errors during first resolution.
        services.AddSingleton<IEmailTemplateRenderer, ScribanEmailTemplateRenderer>();

        // Branding resolver and facade are scoped — they read options but do not hold
        // state across requests. Matches the DI lifetime of the other Tenant services.
        services.AddScoped<IEmailBrandingResolver, EmailBrandingResolver>();
        services.AddScoped<ITransactionalEmailService, TransactionalEmailService>();
        services.AddScoped<IWelcomeEmailDispatcher, WelcomeEmailDispatcher>();

        return services;
    }

    /// <summary>
    /// Adds FIDO2/WebAuthn services for passkey authentication.
    /// Binds configuration from the "Fido2" section in appsettings.json.
    /// </summary>
    public static IServiceCollection AddFido2WebAuthn(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var fido2Config = configuration.GetSection("Fido2");

        var serverDomain = fido2Config["ServerDomain"];
        var origins = fido2Config.GetSection("Origins").Get<HashSet<string>>();

        if (string.IsNullOrWhiteSpace(serverDomain))
        {
            throw new InvalidOperationException(
                "Fido2:ServerDomain configuration is required for WebAuthn passkey support.");
        }

        if (origins is null || origins.Count == 0)
        {
            throw new InvalidOperationException(
                "Fido2:Origins configuration is required for WebAuthn passkey support. " +
                "Specify at least one allowed origin URL.");
        }

        services.AddFido2(options =>
        {
            options.ServerDomain = serverDomain;
            options.ServerName = fido2Config["ServerName"];
            options.Origins = origins;
            options.TimestampDriftTolerance = fido2Config.GetValue<int>("TimestampDriftTolerance", 300000);
        });

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

        var hasResolverConfig =
            !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Tenant:Postgres"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Postgres"]);

        if (hasResolverConfig)
        {
            var connectionString = configuration.GetSorchaPostgresConnectionString("Tenant", "sorcha_tenant");
            healthChecks.AddNpgSql(connectionString, name: "postgresql");
        }

        var redisConnectionString =
            configuration["ConnectionStrings:Tenant:Redis"]
            ?? configuration["ConnectionStrings:Sorcha:Redis"];
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            healthChecks.AddRedis(redisConnectionString, name: "redis");
        }

        return services;
    }
}
